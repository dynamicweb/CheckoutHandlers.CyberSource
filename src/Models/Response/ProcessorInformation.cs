using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Response;

[DataContract]
internal sealed class ProcessorInformation
{
    [DataMember(Name = "networkTransactionId")]
    public string NetworkTransactionId { get; set; }

    [DataMember(Name = "responseDetails")]
    public string ResponseDetails { get; set; }
}
