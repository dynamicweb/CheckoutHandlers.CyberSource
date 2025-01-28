using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Common;

[DataContract]
internal sealed class PaymentInformation
{
    [DataMember(Name = "legacyToken")]
    public LegacyToken LegacyToken { get; set; }
}