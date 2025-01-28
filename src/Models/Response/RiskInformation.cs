using System.Runtime.Serialization;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Response;

[DataContract]
internal sealed class RiskInformation
{
    [DataMember(Name = "id")]
    public string Id { get; set; }

    [DataMember(Name = "fraudDecision")]
    public string FraudDecision { get; set; }

    [DataMember(Name = "fraudDecisionReason")]
    public string FraudDecisionReason { get; set; }
}
