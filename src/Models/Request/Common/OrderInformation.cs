using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Common;

[DataContract]
internal sealed class OrderInformation
{
    [DataMember(Name = "amountDetails")]
    public AmountDetails AmountDetails { get; set; }

    [DataMember(Name = "billTo", EmitDefaultValue = false)]
    public BillTo BillTo { get; set; }
}
