namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Service;

/// <summary>
/// REST API Commands
/// </summary>
internal enum ApiCommand
{
    /// <summary>
    /// Create a payment operation to authorize the amount for the transaction.
    /// POST /pts/v2/payments
    /// See: https://developer.cybersource.com/api-reference-assets/index.html#payments_payments_process-a-payment
    /// </summary>
    CreatePayment,

    /// <summary>
    /// Captures payment. Include the payment ID in the POST request to capture the payment amount.
    /// POST /pts/v2/payments/{operatorId}/captures
    /// See: https://developer.cybersource.com/api-reference-assets/index.html#payments_capture_capture-a-payment
    /// </summary>
    CapturePayment,
}
