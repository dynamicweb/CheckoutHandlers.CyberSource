using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Error;

internal sealed class CybersourceError
{
    [DataMember(Name = "submitTimeUtc")]
    public string SubmitTimeUtc { get; set; }

    [DataMember(Name = "status")]
    public string Status { get; set; }

    [DataMember(Name = "reason")]
    public string Reason { get; set; }

    [DataMember(Name = "message")]
    public string Message { get; set; }

    [DataMember(Name = "details")]
    public IEnumerable<ErrorDetail> Details { get; set; }
}
