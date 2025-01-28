using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Common;

[DataContract]
internal sealed class ClientReferenceInformation
{
    /// <summary>
    /// Merchant-generated order reference or tracking number. It is recommended that you send a unique value for each transaction so that you can perform meaningful searches for the transaction.
    /// </summary>
    [DataMember(Name = "code")]
    public string Code { get; set; }
}
