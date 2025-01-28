using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Response;

/// <summary>
/// Response for payment operation
/// See: https://developer.cybersource.com/api-reference-assets/index.html#payments_capture_capture-a-payment_responsefielddescription_201
/// </summary>
[DataContract]
internal sealed class CaptureResponse
{
    [DataMember(Name = "id")]
    public string Id { get; set; }

    [DataMember(Name = "reconciliationId")]
    public string ReconciliationId { get; set; }

    [DataMember(Name = "status")]
    public string Status { get; set; }

    [DataMember(Name = "processorInformation")]
    public ProcessorInformation ProcessorInformation { get; set; }
}
