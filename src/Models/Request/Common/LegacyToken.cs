using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Common;

[DataContract]
internal sealed class LegacyToken
{
    /// <summary>
    /// Unique identifier for the legacy Secure Storage token used in the transaction.
    /// When you include this value in your request, many of the fields that are normally required for an authorization or credit become optional.
    /// </summary>
    [DataMember(Name = "id")]
    public string Id { get; set; }
}
