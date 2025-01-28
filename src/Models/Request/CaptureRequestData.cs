using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Common;
using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request;

/// <summary>
/// Request to capture the payment
/// See: https://developer.cybersource.com/api-reference-assets/index.html#payments_capture_capture-a-payment
/// </summary>
[DataContract]
internal sealed class CaptureRequestData
{
    [DataMember(Name = "clientReferenceInformation")]
    public ClientReferenceInformation ClientReferenceInformation { get; set; }

    [DataMember(Name = "orderInformation")]
    public OrderInformation OrderInformation { get; set; }
}