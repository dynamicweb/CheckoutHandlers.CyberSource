using Dynamicweb.Core;
using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Helpers;
using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Error;
using Dynamicweb.Ecommerce.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Service;

/// <summary>
/// Sends request to CyberSource and gets response.
/// </summary>
internal sealed class CyberSourceRequest
{
    public string MerchantId { get; set; }

    public string CertificateFile { get; set; }

    public string CertificatePassword { get; set; }

    public CyberSourceRequest(string merchantId, string certificateFile, string certificatePassword)
    {
        MerchantId = merchantId;
        CertificateFile = certificateFile;
        CertificatePassword = certificatePassword;
    }

    public string SendRequest(Order order, string host, CommandConfiguration configuration)
    {
        using (HttpMessageHandler messageHandler = GetMessageHandler())
        {
            using var client = new HttpClient(messageHandler);

            client.Timeout = new TimeSpan(0, 0, 0, 90);
            client.DefaultRequestHeaders.Add("Host", host);

            UriBuilder baseAddress = GetBaseAddress(host);
            client.BaseAddress = new Uri(baseAddress.ToString());

            HttpMethod method = configuration.CommandType switch
            {
                ApiCommand.CreatePayment or
                ApiCommand.CapturePayment => HttpMethod.Post,
                _ => throw new NotSupportedException($"Unknown operation was used. The operation code: {configuration.CommandType}.")
            };

            string data = Converter.Serialize(configuration.Data);
            string jwtToken = GenerateJWT(order, method, data);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

            string apiCommand = GetCommandLink(baseAddress, configuration.CommandType, configuration.OperatorId);
            Task<HttpResponseMessage> requestTask = method switch
            {
                _ when method == HttpMethod.Post => client.PostAsync(apiCommand, GetContent(data)),
                _ => throw new NotSupportedException($"Unknown http method was used: {method.ToString()}.")
            };

            try
            {
                using (HttpResponseMessage response = requestTask.GetAwaiter().GetResult())
                {
                    Log(order, $"Remote server response: HttpStatusCode = {response.StatusCode}, HttpStatusDescription = {response.ReasonPhrase}");
                    string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Log(order, $"Remote server ResponseText: {responseText}");

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorResponse = Converter.Deserialize<CybersourceError>(responseText);
                        if (string.IsNullOrEmpty(errorResponse.Status))
                            throw new Exception($"Unhandled exception. Operation failed: '{response.ReasonPhrase}'. Response text: '{responseText}'");

                        string errorMessage = $"Operation failed. Status: '{errorResponse.Status}'. Reason: '{errorResponse.Reason}'. Message: '{errorResponse.Message}'.";
                        if (response.StatusCode is HttpStatusCode.BadRequest)
                        {
                            if (errorResponse.Details?.Any() is true)
                            {
                                var detailsMessage = new StringBuilder();
                                foreach (ErrorDetail detail in errorResponse.Details)
                                    detailsMessage.AppendLine($"{detail.Field}: {detail.Reason}");

                                errorMessage += $" Details: '{detailsMessage.ToString()}'";
                            }
                        }
                        throw new Exception(errorMessage);
                    }

                    return responseText;
                }
            }
            catch (HttpRequestException requestException)
            {
                throw new Exception($"An error occurred during CyberSource request. Error code: {requestException.StatusCode}");
            }
        }

        HttpMessageHandler GetMessageHandler() => new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
        };

        HttpContent GetContent(string content) => new StringContent(content, Encoding.UTF8, "application/json");
    }

    private UriBuilder GetBaseAddress(string host) => new UriBuilder(Uri.UriSchemeHttps, host);

    private string GetCommandLink(UriBuilder baseAddress, ApiCommand command, string operatorId)
    {
        return command switch
        {
            ApiCommand.CreatePayment => GetCommandLink("payments"),
            ApiCommand.CapturePayment => GetCommandLink($"payments/{operatorId}/captures"),
            _ => throw new NotSupportedException($"The api command is not supported. Command: {command}")
        };

        string GetCommandLink(string gateway)
        {
            baseAddress.Path = $"pts/v2/{gateway}";
            return baseAddress.ToString();
        }
    }

    private void Log(Order order, string message)
    {
        if (order is null)
            return;

        Services.OrderDebuggingInfos.Save(order, message, typeof(CyberSource).FullName, DebuggingInfoType.Undefined);
    }

    /// <summary>
    /// This method demonstrates the creation of the JWT Authentication credential
    /// Takes Request Payload and Http method(GET/POST) as input.
    /// This code is an example from: https://github.com/CyberSource/cybersource-rest-samples-csharp/blob/master/Source/Samples/Authentication/StandAloneJWT.cs
    /// </summary>
    /// <param name="data">Value from which to generate JWT</param>
    /// <param name="method">The HTTP Verb that is needed for generating the credential</param>
    /// <returns>String containing the JWT Authentication credential</returns>
    private string GenerateJWT(Order order, HttpMethod method, string data)
    {
        string digest;
        string token = "TOKEN_PLACEHOLDER";

        try
        {
            // Generate the hash for the payload
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] payloadBytes = sha256Hash.ComputeHash(Encoding.ASCII.GetBytes(data));
                digest = Convert.ToBase64String(payloadBytes);
            }

            // Create the JWT payload (aka claimset / JWTBody)
            string jwtBody = "0";

            if (method == HttpMethod.Post)
                jwtBody = "{\n\"digest\":\"" + digest + "\", \"digestAlgorithm\":\"SHA-256\", \"iat\":\"" + DateTime.Now.ToUniversalTime().ToString("r") + "\"}";
            else if (method == HttpMethod.Get)
                jwtBody = "{\"iat\":\"" + DateTime.Now.ToUniversalTime().ToString("r") + "\"}";

            string certificatePath = Helper.GetCertificateFilePath(CertificateFile);
            if (string.IsNullOrEmpty(certificatePath))
                throw new Exception("Certificate for REST API is not found");

            // P12 certificate public key is sent in the header and the private key is used to sign the token
            X509Certificate2 x5Cert = new X509Certificate2(certificatePath, CertificatePassword, X509KeyStorageFlags.MachineKeySet);

            // Extracting Public Key from .p12 file
            string x5cPublicKey = Convert.ToBase64String(x5Cert.RawData);

            // Extracting Private Key from .p12 file
            var privateKey = x5Cert.GetRSAPrivateKey();

            // Extracting serialNumber
            string serialNumber = null;
            string serialNumberPrefix = "SERIALNUMBER=";

            string principal = x5Cert.Subject;

            int beg = principal.IndexOf(serialNumberPrefix);
            if (beg >= 0)
            {
                int x5cBase64List = principal.IndexOf(",", beg);
                if (x5cBase64List == -1)
                    x5cBase64List = principal.Length;

                serialNumber = principal.Substring(serialNumberPrefix.Length, x5cBase64List - serialNumberPrefix.Length);
            }

            // Create the JWT Header custom fields
            var x5cList = new List<string>()
            {
                x5cPublicKey
            };

            var cybsHeaders = new Dictionary<string, object>()
            {
                { "v-c-merchant-id", MerchantId },
                { "x5c", x5cList }
            };

            // JWT token is Header plus the Body plus the Signature of the Header & Body
            // Here the Jose-JWT helper library (https://github.com/dvsekhvalnov/jose-jwt) is used create the JWT
            token = Jose.JWT.Encode(jwtBody, privateKey, Jose.JwsAlgorithm.RS256, cybsHeaders);
        }
        catch (Exception ex)
        {
            throw new Exception("JWT token create failed", ex);
        }

        return token;
    }
}
