using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Request.Common;

[DataContract]
internal sealed class BillTo
{
    /// <summary>
    /// Customer’s first name. This name must be the same as the name on the card.
    /// </summary>
    [DataMember(Name = "firstName")]
    public string FirstName { get; set; }

    /// <summary>
    /// Customer’s last name. This name must be the same as the name on the card.
    /// </summary>
    [DataMember(Name = "lastName")]
    public string LastName { get; set; }

    /// <summary>
    /// Payment card billing street address as it appears on the credit card issuer’s records.
    /// </summary>
    [DataMember(Name = "address1")]
    public string Address1 { get; set; }

    /// <summary>
    /// Payment card billing city.
    /// </summary>
    [DataMember(Name = "locality")]
    public string Locality { get; set; }

    /// <summary>
    /// State or province of the billing address. Use the State, Province, and Territory Codes for the United States and Canada.
    /// </summary>
    [DataMember(Name = "administrativeArea")]
    public string AdministrativeArea { get; set; }

    /// <summary>
    /// Postal code for the billing address. The postal code must consist of 5 to 9 digits.
    /// When the billing country is the U.S., the 9-digit postal code must follow this format:
    /// [5 digits][dash][4 digits]
    /// Example 12345-6789
    /// When the billing country is Canada, the 6-digit postal code must follow this format:
    /// [alpha][numeric][alpha][space][numeric][alpha][numeric]
    /// Example A1B 2C3
    /// </summary>
    [DataMember(Name = "postalCode")]
    public string PostalCode { get; set; }

    /// <summary>
    /// Payment card billing country. Use the two-character ISO Standard Country Codes.
    /// </summary>
    [DataMember(Name = "country")]
    public string Country { get; set; }

    /// <summary>
    /// Customer's email address, including the full domain name.
    /// </summary>
    [DataMember(Name = "email")]
    public string Email { get; set; }

    /// <summary>
    /// Customer’s phone number.
    /// It is recommended that you include the country code when the order is from outside the U.S.
    /// </summary>
    [DataMember(Name = "phoneNumber")]
    public string PhoneNumber { get; set; }
}