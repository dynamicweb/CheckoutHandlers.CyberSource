using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Error;

[DataContract]
internal sealed class ErrorDetail
{
    [DataMember(Name = "field")]
    public string Field { get; set; }

    [DataMember(Name = "reason")]
    public string Reason { get; set; }
}
