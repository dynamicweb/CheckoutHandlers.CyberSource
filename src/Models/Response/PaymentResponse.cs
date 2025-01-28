using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Response;

/// <summary>
/// Response for payment operation
/// See: https://developer.cybersource.com/api-reference-assets/index.html#payments_payments_create-a-payment-order-request_responsefielddescription_201_clientReferenceInformation
/// </summary>
[DataContract]
internal sealed class PaymentResponse
{
    [DataMember(Name = "id")]
    public string Id { get; set; }

    [DataMember(Name = "reconciliationId")]
    public string ReconciliationId { get; set; }

    [DataMember(Name = "status")]
    public string Status { get; set; }

    [DataMember(Name = "riskInformation")]
    public RiskInformation RiskInformation { get; set; }
}
