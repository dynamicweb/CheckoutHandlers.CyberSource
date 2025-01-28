using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Common;
using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request;

/// <summary>
/// A payment authorizes the amount for the transaction. There are a number of supported payment features, such as E-commerce and Card Present - Credit Card/Debit Card, Echeck, e-Wallets, Level II/III Data, etc.
/// See: https://developer.cybersource.com/api-reference-assets/index.html#payments_payments_process-a-payment
/// </summary>
[DataContract]
internal sealed class PaymentRequestData
{
    [DataMember(Name = "clientReferenceInformation")]
    public ClientReferenceInformation ClientReferenceInformation { get; set; }

    [DataMember(Name = "paymentInformation")]
    public PaymentInformation PaymentInformation { get; set; }

    [DataMember(Name = "orderInformation")]
    public OrderInformation OrderInformation { get; set; }
}