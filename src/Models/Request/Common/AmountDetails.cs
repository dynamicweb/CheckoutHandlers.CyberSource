using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Common;

[DataContract]
internal sealed class AmountDetails
{
    /// <summary>
    /// Grand total for the order. This value cannot be negative. You can include a decimal point (.), but no other special characters.
    /// CyberSource truncates the amount to the correct number of decimal places.
    /// </summary>
    [DataMember(Name = "totalAmount")]
    public string TotalAmount { get; set; }

    /// <summary>
    /// Currency used for the order. Use the three-character ISO Standard Currency Codes.
    /// </summary>
    [DataMember(Name = "currency")]
    public string Currency { get; set; }
}
