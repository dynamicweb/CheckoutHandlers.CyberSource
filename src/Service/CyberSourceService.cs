using Dynamicweb.Core;
using Dynamicweb.Ecommerce.Cart;
using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Helpers;
using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request;
using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Response;
using Dynamicweb.Ecommerce.Orders;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Service;

internal sealed class CyberSourceService
{
    public CyberSourceRequest Request { get; }

    public string BaseAddress { get; }

    public CyberSourceService(string baseAddress, string merchantId, string certificateFile, string certificatePassword)
    {
        Request = new(merchantId, certificateFile, certificatePassword);
        BaseAddress = baseAddress;
    }

    public PaymentResponse CreatePayment(Order order, PaymentCardToken savedCard)
    {
        PaymentRequestData requestData = PreparePaymentRequest(order, savedCard);
        var configuration = new CommandConfiguration
        {
            CommandType = ApiCommand.CreatePayment,
            Data = requestData
        };

        string response = Request.SendRequest(order, BaseAddress, configuration);
        return Converter.Deserialize<PaymentResponse>(response);
    }

    public CaptureResponse Capture(Order order, string transactionNumber)
    {
        var captureRequestData = new CaptureRequestData
        {
            ClientReferenceInformation = new()
            {
                Code = order.Id
            },
            OrderInformation = new()
            {
                AmountDetails = new()
                {
                    Currency = order.Price.Currency.Code,
                    TotalAmount = Helper.GetTransactionAmount(order)
                }
            }
        };

        var configuration = new CommandConfiguration
        {
            CommandType = ApiCommand.CapturePayment,
            OperatorId = transactionNumber,
            Data = captureRequestData
        };

        string response = Request.SendRequest(order, BaseAddress, configuration);
        return Converter.Deserialize<CaptureResponse>(response);
    }

    private PaymentRequestData PreparePaymentRequest(Order order, PaymentCardToken savedCard)
    {
        string customerName = order.CustomerName?.Trim() ?? string.Empty;
        string firstName = Helper.GetCustomerFirstName(order, customerName);
        string lastName = Helper.GetCustomerLastName(order, customerName);

        return new()
        {
            ClientReferenceInformation = new()
            {
                Code = order.Id
            },
            PaymentInformation = new()
            {
                LegacyToken = new()
                {
                    Id = savedCard.Token
                }
            },
            OrderInformation = new()
            {
                AmountDetails = new()
                {
                    TotalAmount = Helper.GetTransactionAmount(order),
                    Currency = order.Price.Currency.Code
                },
                BillTo = new()
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Address1 = Converter.ToString(order.CustomerAddress),
                    Locality = Converter.ToString(order.CustomerCity),
                    AdministrativeArea = Converter.ToString(order.CustomerRegion),
                    PostalCode = Converter.ToString(order.CustomerZip),
                    Country = Converter.ToString(order.CustomerCountryCode),
                    Email = Converter.ToString(order.CustomerEmail),
                    PhoneNumber = Converter.ToString(order.CustomerPhone)
                }
            }
        };
    }

}
