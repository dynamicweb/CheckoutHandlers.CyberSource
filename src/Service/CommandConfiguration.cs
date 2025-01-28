namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Service;

internal sealed class CommandConfiguration
{
    /// <summary>
    /// Cyber source command. See operation urls in <see cref="CyberSourceRequest"/> and <see cref="ApiCommand"/>
    /// </summary>
    public ApiCommand CommandType { get; set; }

    /// <summary>
    /// Command operator id, like https://.../pts/v2/payments/{OperatorId}/captures
    /// </summary>
    public string OperatorId { get; set; }

    /// <summary>
    /// Data to serialize
    /// </summary>
    public object Data { get; set; }
}
