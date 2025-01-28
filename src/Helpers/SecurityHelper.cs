using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Security.Cryptography;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Helpers;

internal static class SecurityHelper
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="publicKey"></param>
    /// <param name="secretKey"></param>
    /// <param name="signature"></param>
    /// <returns></returns>
    /// <remarks>ToDo: Params property hasn't been defined in new Dynamicweb.Context class.</remarks>
    public static bool ValidateResponseSignation(string publicKey, string secretKey, out string signature)
    {
        var parameters = Context.Current.Request.Params;
        return ValidateResponseSignation(parameters, publicKey, secretKey, out signature);
    }

    /// <summary>
    /// Validates transaction signature
    /// </summary>
    /// <param name="parameters">Transaction parameters</param>
    /// <param name="publicKey">public key</param>
    /// <param name="secretKey">secret key</param>
    /// <param name="signature">signature</param>
    /// <returns>Boolean result of validating transaction signature</returns>
    public static bool ValidateResponseSignation(NameValueCollection parameters, string publicKey, string secretKey, out string signature)
    {
        var transactionSignature = parameters["signature"];
        var signedFieldNames = parameters["signed_field_names"].Split(',');

        var dataToSign = new List<string>();
        foreach (string signedFieldName in signedFieldNames)
        {
            dataToSign.Add(signedFieldName + "=" + parameters[signedFieldName]);
        }
        signature = Sign(string.Join(",", dataToSign), secretKey).Replace("\n", string.Empty);

        return transactionSignature.Equals(signature);
    }

    /// <summary>
    /// Signs parameters with secret key
    /// </summary>
    /// <param name="parameters">set of key value pairs</param>
    /// <param name="secretKey">key that is used for encription</param>
    /// <returns>Encrypted string</returns>
    public static string Sign(Dictionary<string, string> parameters, string secretKey)
    {
        return Sign(BuildSignation(parameters), secretKey);
    }

    private static string Sign(string data, string secretKey)
    {
        var encoding = new System.Text.UTF8Encoding();
        var keyBytes = encoding.GetBytes(secretKey);

        using (var hmacsha256 = new HMACSHA256(keyBytes))
        {
            var messageBytes = encoding.GetBytes(data);
            return Convert.ToBase64String(hmacsha256.ComputeHash(messageBytes));
        }
    }

    private static string BuildSignation(IDictionary<string, string> parameters)
    {
        var signedFieldNames = parameters["signed_field_names"].Split(',');
        var dataToSign = new List<string>();

        foreach (string signedFieldName in signedFieldNames)
        {
            dataToSign.Add(signedFieldName + "=" + parameters[signedFieldName]);
        }

        return string.Join(",", dataToSign);
    }
}
