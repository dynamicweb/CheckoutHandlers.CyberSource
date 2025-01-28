using Dynamicweb.Ecommerce.Cart;
using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Helpers;
using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Models.Response;
using Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Service;
using Dynamicweb.Ecommerce.Orders;
using Dynamicweb.Ecommerce.Orders.Gateways;
using Dynamicweb.Extensibility.AddIns;
using Dynamicweb.Extensibility.Editors;
using Dynamicweb.Frontend;
using Dynamicweb.Rendering;
using Dynamicweb.Security.UserManagement;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource;

/// <summary>
/// CyberSource Checkout Handler
/// </summary>
[AddInName("CyberSource")]
[AddInDescription("Payment system, http://www.cybersource.com")]
public class CyberSource : CheckoutHandlerWithStatusPage, IParameterOptions, IRemoteCapture, ISavedCard, IRecurring, ICheckAuthorizationStatus
{
    private const string FormTemplateFolder = "eCom7/CheckoutHandler/CyberSource/Payment";
    private const string CancelTemplateFolder = "eCom7/CheckoutHandler/CyberSource/Cancel";
    private const string ErrorTemplateFolder = "eCom7/CheckoutHandler/CyberSource/Error";

    private static HashSet<string> SupportedCountryCodes { get; set; }
    private static HashSet<string> SupportedCurrencyCodes { get; set; }

    private static Dictionary<string, string> CardTypes = new()
    {
        {"001", "Visa"},
        {"002", "MasterCard, Eurocard"},
        {"003", "American Express"},
        {"004", "Discover"},
        {"005", "Diners Club"},
        {"006", "Carte Blanche"},
        {"007", "JCB"},
        {"014", "EnRoute"},
        {"021", "JAL"},
        {"024", "Maestro (UK Domestic)"},
        {"031", "Delta, Global Collect"},
        {"033", "Visa Electron"},
        {"034", "Dankort"},
        {"036", "Carte Bleu"},
        {"037", "Carta Si"},
        {"042", "Maestro (International)"},
        {"043", "GE Money UK card"}
    };

    static CyberSource()
    {
        List<RegionInfo> cultures = new List<RegionInfo>();
        foreach (CultureInfo cultureInfo in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                cultures.Add(new(cultureInfo.Name));
            }
            catch
            {
            }
        }

        SupportedCountryCodes = cultures.Select(regionInfo => regionInfo.TwoLetterISORegionName.ToUpper()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        SupportedCurrencyCodes = cultures.Select(regionInfo => regionInfo.ISOCurrencySymbol.ToUpper()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Default constructor
    /// </summary>
    public CyberSource()
    {
        ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
        ErrorTemplate = "eCom7/CheckoutHandler/CyberSource/Error/checkouthandler_error.html";
        CancelTemplate = "eCom7/CheckoutHandler/CyberSource/Cancel/checkouthandler_cancel.html";
        PaymentTemplate = "eCom7/CheckoutHandler/CyberSource/Payment/Payment.html";
    }

    #region Addin parameters

    [AddInParameter("Merchant id"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true;infoText=This is the name of your sandbox account;")]
    public string MerchantId { get; set; }

    [AddInParameter("Profile id"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true;infoText=This is a security key generated in the CyberSource Business Center under: Tools & Settings > Profiles > Security;")]
    public string ProfileId { get; set; }

    [AddInParameter("Access key"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true;infoText=This is the public component of the security key;")]
    public string AccessKey { get; set; }

    [AddInParameter("Secret key"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true;TextArea=true;infoText=This is the secret component of the security key;")]
    public string SecretKey { get; set; }

    [AddInParameter("Certificate"), AddInParameterEditor(typeof(FileManagerEditor), "NewGUI=true;allowBrowse=true;folder=System;showfullpath=true;infoText=The certificate for REST API, which should be uploaded to the Dynamicweb File Archive;")]
    public string CertificateFile { get; set; }

    [AddInParameter("Certificate password"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true;infoText=The password to read certificate;")]
    public string CertificatePassword { get; set; }

    private TransactionTypes transactionType = TransactionTypes.Sale;

    [AddInParameter("Transaction type")]
    [AddInParameterEditor(typeof(RadioParameterEditor), "")]
    public string TransactionType
    {
        get => transactionType.ToString();
        set => Enum.TryParse(value, out transactionType);
    }

    [AddInParameter("Forced tokenization"), AddInParameterEditor(typeof(YesNoParameterEditor), "infoText=;Forces the token to be saved on order or card for logged in users who have not chosen \"Save card\";")]
    public bool ForceTokenization { get; set; }

    private string paymentTemplate;

    [AddInParameter("Payment template"), AddInParameterEditor(typeof(TemplateParameterEditor), $"folder=Templates/{FormTemplateFolder}")]
    public string PaymentTemplate
    {
        get => TemplateHelper.GetTemplateName(paymentTemplate);
        set => paymentTemplate = value;
    }

    private string cancelTemplate;

    [AddInParameter("Cancel template"), AddInParameterEditor(typeof(TemplateParameterEditor), $"folder=Templates/{CancelTemplateFolder}")]
    public string CancelTemplate
    {
        get => TemplateHelper.GetTemplateName(cancelTemplate);
        set => cancelTemplate = value;
    }

    private string errorTemplate;

    [AddInParameter("Error template"), AddInParameterEditor(typeof(TemplateParameterEditor), $"folder=Templates/{ErrorTemplateFolder}")]
    public string ErrorTemplate
    {
        get => TemplateHelper.GetTemplateName(errorTemplate);
        set => errorTemplate = value;
    }

    private WorkModes workMode = WorkModes.Test;

    [AddInParameter("Work Mode"), AddInParameterEditor(typeof(RadioParameterEditor), "")]
    public string WorkMode
    {
        get => workMode.ToString();
        set => Enum.TryParse(value, out workMode);
    }

    private WindowModes windowMode = WindowModes.Redirect;

    [AddInParameter("Window Mode"), AddInParameterEditor(typeof(RadioParameterEditor), "Explanation=Select if the payment window should redirect or if it should be embedded;")]
    public string WindowMode
    {
        get => windowMode.ToString();
        set => Enum.TryParse(value, out windowMode);
    }

    private string declineAVSFlag;

    [AddInParameter("Review AVS Codes"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true;Explanation=Cybersource supports AVS (Address Verification System) validation;Hint=Should contain the AVS codes you want to receive an AVS validation for;")]
    public string Decline_AVS_Flag
    {
        get => string.IsNullOrEmpty(declineAVSFlag) ? "N" : declineAVSFlag;
        set => declineAVSFlag = value;
    }

    [AddInParameter("Ignore AVS Result"), AddInParameterEditor(typeof(YesNoParameterEditor), "infoText=When Ignore AVS results is checked, you will receive no AVS declines;")]
    public bool Ignore_AVS_Result { get; set; } = false;

    [AddInParameter("Approve AVS Code"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true; Explanation=Cybersource supports AVS (Address Verification System) validation;Hint=Should contain a comma-separated list of AVS codes which will permit the transaction to be approved;")]
    public string Result_AVS_Flag { get; set; }

    #endregion

    private string GetHost()
    {
        string apiType = workMode is WorkModes.Production ? "api" : "apitest";

        return $"{apiType}.cybersource.com";
    }

    /// <summary>
    /// Gets options according to behavior mode
    /// </summary>
    /// <param name="parameterName"></param>
    /// <returns>Key-value pairs of settings</returns>
    public IEnumerable<ParameterOption> GetParameterOptions(string parameterName)
    {
        try
        {
            switch (parameterName)
            {
                case "Work Mode":
                    return new List<ParameterOption>
                    {
                        new("Test", WorkModes.Test.ToString()) { Hint = "Choose Test to simulate payment transactions without involving real money transfers" },
                        new("Production", WorkModes.Production.ToString()) { Hint =  "Choose Production when you are ready to go live" }
                    };

                case "Window Mode":
                    return new List<ParameterOption>
                    {
                        new("Redirect", WindowModes.Redirect.ToString()),
                        new("Embedded", WindowModes.Embedded.ToString())
                    };

                case "Transaction type":
                    return new List<ParameterOption>
                    {
                        new("Authorization (zero amount)", TransactionTypes.ZeroAuthorization.ToString())
                        {
                            Hint = "All transactions are zero authorized. " +
                            "Capture is performed through AX or similar and you can carry out account " +
                            "verification checks to check the validity of a Visa/MasterCard Debit or credit card"
                        },
                        new("Authorization (order amount)", TransactionTypes.Authorization.ToString())
                        {
                            Hint = " The order is authorized at AuthorizeNET and then you can " +
                            "manually authorize from ecommerce backend order list. This is used for usual transactions"
                        },
                        new("Sale",TransactionTypes.Sale.ToString())
                        {
                            Hint = "The amount is sent for authorization, and if approved, is automatically submitted for settlement"
                        }
                    };

                default:
                    throw new ArgumentException(string.Format("Unknown dropdown name: '{0}'", parameterName));
            }
        }
        catch (ThreadAbortException)
        {
            return null;
        }
        catch (Exception ex)
        {
            LogError(null, ex, "Unhandled exception with message: {0}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Send capture request to transaction service
    /// </summary>
    /// <param name="order">Order to be captured</param>
    /// <returns>Response from transaction service</returns>
    OrderCaptureInfo IRemoteCapture.Capture(Order order)
    {
        try
        {
            string errorMessage = string.Empty;
            if (order is null || string.IsNullOrEmpty(order.Id))
                errorMessage = "No valid Order object set";
            else if (string.IsNullOrWhiteSpace(order.TransactionNumber))
                errorMessage = "No transaction number set on the order";

            string certPath = Helper.GetCertificateFilePath(CertificateFile);
            if (string.IsNullOrWhiteSpace(certPath))
                errorMessage = "Certificate for REST API is not found";

            if (!string.IsNullOrEmpty(errorMessage))
            {
                LogEvent(order, errorMessage);
                return new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Failed, errorMessage);
            }

            var service = new CyberSourceService(GetHost(), MerchantId, CertificateFile, CertificatePassword);
            CaptureResponse response = service.Capture(order, order.TransactionNumber);
            LogEvent(order, "Capture successful", DebuggingInfoType.CaptureResult);

            return new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, "Capture successful");
        }
        catch (ThreadAbortException)
        {
            return new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Failed, "System.Threading.ThreadAbortException");
        }
        catch (Exception ex)
        {
            string message = string.Format("Remote capture failed with the message: {0}", ex.Message);
            LogEvent(order, message, DebuggingInfoType.CaptureResult);
            LogError(order, ex, message);

            return new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Failed, message);
        }
    }

    /// <summary>
    /// Starts order checkout procedure
    /// </summary>
    /// <param name="order">Order to be checked out</param>
    public override OutputResult BeginCheckout(Order order, CheckoutParameters parameters)
    {
        try
        {
            string errorMessage;
            if (!ValidateOrderFields(order, out errorMessage))
                return OnError(order, errorMessage);

            bool isIFrameMode = windowMode is WindowModes.Embedded;

            Dictionary<string, string> form;
            string gatewayUrl;

            if (order.IsRecurringOrderTemplate || !string.IsNullOrWhiteSpace(GetSavedCardName(order)))
            {
                gatewayUrl = transactionType is TransactionTypes.ZeroAuthorization
                    ? GetCreateCardGatewayUrl(isIFrameMode)
                    : GetGatewayUrl(isIFrameMode);

                form = PrepareCreateCardRequest(order);
            }
            else
            {
                gatewayUrl = GetGatewayUrl(isIFrameMode);
                form = transactionType is TransactionTypes.Sale
                    ? PrepareSaleRequest(order)
                    : PrepareAuthorizationRequest(order);
            }

            if (isIFrameMode)
                return RenderPaymentFrame(order, gatewayUrl, form);

            return GetSubmitFormResult(gatewayUrl, form);
        }
        catch (ThreadAbortException)
        {
            return NoActionOutputResult.Default;
        }
        catch (Exception ex)
        {
            LogError(order, ex, "Unhandled exception with message: {0}", ex.Message);

            return OnError(order, ex.Message);
        }
    }

    private OutputResult RenderPaymentFrame(Order order, string gatewayUrl, Dictionary<string, string> form)
    {
        if (string.IsNullOrWhiteSpace(PaymentTemplate))
        {
            LogError(order, "Embedded payment template not set");

            return OnError(order, "Embedded payment template not set");
        }

        // Get template
        var formTemplate = new Template(TemplateHelper.GetTemplatePath(PaymentTemplate, FormTemplateFolder));

        // Render tags
        formTemplate.SetTag("CyberSource.HostedPaymentURL", gatewayUrl);
        formTemplate.SetTag("CyberSource.CancelURL", GetCancelUrl(order));

        Template loopTemplate = formTemplate.GetLoop("CyberSourceFields");
        foreach ((string key, string value) in form)
        {
            loopTemplate.SetTag("CyberSource.FieldName", key);
            loopTemplate.SetTag("CyberSource.FieldValue", value);
            loopTemplate.CommitLoop();
        }

        return new ContentOutputResult
        {
            Content = Render(order, formTemplate)
        };
    }

    public override OutputResult HandleRequest(Order order)
    {
        LogEvent(order, "Redirected to CyberSource CheckoutHandler");

        switch (Context.Current.Request["cmd"])
        {
            case "Accept":
                if (ValidateAVSCode(order) is ContentOutputResult errorAcceptResult)
                    return errorAcceptResult;
                return StateOk(order);
            case "CardSaved":
                if (ValidateAVSCode(order) is ContentOutputResult errorCardSavedResult)
                    return errorCardSavedResult;
                return StateCardSaved(order);
            case "Cancel":
                return StateCancel(order);
            case "IFrameError":
                return StateIFrameError(order);
            default:
                return ContentOutputResult.Empty;
        }
    }

    private OutputResult ValidateAVSCode(Order order)
    {
        string transact = Context.Current.Request["transaction_id"];
        string avsResult = Context.Current.Request["auth_avs_code"];
        string avsResultRaw = Context.Current.Request["auth_avs_code_raw"];

        var resultCodesAllowed = new List<string>();
        if (!string.IsNullOrWhiteSpace(Result_AVS_Flag))
        {
            string formattedResult = Result_AVS_Flag.Replace(' ', ',');
            resultCodesAllowed.AddRange(formattedResult.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        LogEvent(order, "CyberSource response: avs_code: '{0}', avs_code_raw: '{1}'.", avsResult, avsResultRaw);

        if (!string.IsNullOrEmpty(avsResult) && resultCodesAllowed.Any() && !resultCodesAllowed.Contains(avsResult))
        {
            LogEvent(order, "Transaction {0} not approved.", transact);

            string message = Context.Current.Request["message"];
            return OnError(order, $"Transaction {transact} not approved. {message} (code={avsResult})", windowMode is WindowModes.Embedded);
        }

        return NoActionOutputResult.Default;
    }

    private bool ValidateOrderFields(Order order, out string errorMessage)
    {
        string supportedCodes = string.Join(",", SupportedCurrencyCodes);

        if (!SupportedCurrencyCodes.Any(code => code.Equals(order.CurrencyCode, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = $"Only {supportedCodes} currency codes is allowed. Order currency: {order.CurrencyCode}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(order.CustomerCountryCode))
        {
            errorMessage = "Required customer country code";
            return false;
        }

        if (!SupportedCountryCodes.Any(code => code.Equals(order.CustomerCountryCode, StringComparison.OrdinalIgnoreCase)))
        {
            errorMessage = $"Only {supportedCodes} country codes is supported. Order country code: {order.CustomerCountryCode}";
            return false;
        }

        errorMessage = string.Empty;

        return true;
    }

    private OutputResult StateCancel(Order order)
    {
        LogEvent(order, "State cancel");
        string calculatedSignature;
        if (windowMode is not WindowModes.Embedded && !SecurityHelper.ValidateResponseSignation(AccessKey, SecretKey, out calculatedSignature))
        {
            string signature = Context.Current.Request["signature"];
            LogError(order, "The signature returned from callback does not match: {0}, calculated: {1}", signature, calculatedSignature);
            return OnError(order, "Wrong signature");
        }

        order.TransactionStatus = "Cancelled";
        Services.Orders.Save(order);
        CheckoutDone(order);

        var cancelTemplate = new Template(TemplateHelper.GetTemplatePath(CancelTemplate, CancelTemplateFolder));

        return new ContentOutputResult
        {
            Content = Render(order, cancelTemplate)
        };
    }

    private OutputResult StateIFrameError(Order order)
    {
        string errorMessage = Context.Current.Request["ErrorMessage"];

        return OnError(order, errorMessage);
    }

    private OutputResult StateOk(Order order)
    {
        LogEvent(order, "State ok");

        if (!order.Complete)
            return ProcessOrder(order);

        return PassToCart(order);
    }

    private OutputResult StateCardSaved(Order order)
    {
        LogEvent(order, "CyberSource Card Authorized successfully");

        string cardName = WebUtility.UrlDecode(Context.Current.Request["CardTokenName"]);
        if (string.IsNullOrEmpty(cardName))
            cardName = order.Id;

        if (Context.Current.Request["reason_code"] == "100")
        {
            string requestCardType = Context.Current.Request["req_card_type"];
            string subscribtionId = Context.Current.Request["payment_token"];
            string cardType = CardTypes.Keys.Any(key => key.Equals(requestCardType, StringComparison.OrdinalIgnoreCase))
                ? CardTypes[requestCardType]
                : $"Unrecognized card type - {requestCardType}";

            string cardNubmer = Context.Current.Request["req_card_number"].ToUpper();
            order.TransactionCardNumber = cardNubmer;

            order.TransactionCardType = cardType;
            string transactionId = Context.Current.Request["transaction_id"];
            User user = UserManagementServices.Users.GetUserById(order.CustomerAccessUserId);
            if (user is not null)
            {
                PaymentCardToken savedCard = Services.PaymentCard.CreatePaymentCard(user.ID, order.PaymentMethodId, cardName, cardType, cardNubmer, subscribtionId);
                order.SavedCardId = savedCard.ID;
            }
            else
                order.TransactionToken = subscribtionId;

            int decimals = string.IsNullOrEmpty(order.Currency.Rounding?.Id)
                ? 2
                : order.Currency.Rounding.Decimals;

            if (!order.IsRecurringOrderTemplate && transactionType is TransactionTypes.ZeroAuthorization)
            {
                order.TransactionAmount = 0.00;
                order.TransactionStatus = "Succeeded";
                order.TransactionCardType = cardType;
                order.TransactionCardNumber = cardNubmer;
                order.TransactionType = "Zero authorization";
                string msg = "Zero authorization succeeded";
                LogEvent(order, msg);
                order.CaptureInfo = new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, msg);
            }
            else if (transactionType is TransactionTypes.Sale)
            {
                order.TransactionAmount = Math.Round(order.Price.Price, decimals);
                order.TransactionStatus = "Succeeded";
                string msg = "Capture succeeded";
                LogEvent(order, msg);
                order.CaptureInfo = new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, msg);
            }
            else if (transactionType is TransactionTypes.Authorization)
            {
                order.TransactionType = "Zero authorization";
                order.TransactionAmount = Math.Round(order.Price.Price, decimals);
                order.TransactionStatus = "Succeeded";
                string msg = "Authorization succeeded";
                LogEvent(order, msg);
            }

            Services.Orders.Save(order);
            LogEvent(order, "Saved Card created");
            SetOrderComplete(order, transactionId);
            LogEvent(order, "Create Card successful and order completed");
        }
        else
            LogError(order, string.Format("Create card failed. Decision: '{0}' messageId: '{1}', messageText: '{2}'", Context.Current.Request["decision"], Context.Current.Request["reason_code"], Context.Current.Request["message"]));

        CheckoutDone(order);
        if (!order.Complete)
            return OnError(order, "Some error happened on creating saved card.", windowMode == WindowModes.Embedded);

        if (windowMode is not WindowModes.Embedded)
            return PassToCart(order);

        return new ContentOutputResult
        {
            Content = $"<script>parent.location.href = '{GetBaseUrl(order)}&cmd=Accept';</script>"
        };
    }

    private OutputResult ProcessOrder(Order order)
    {
        bool orderWasCompleted = order.Complete;

        try
        {
            bool errorOccured = false;
            string calculatedSignature;

            if (!SecurityHelper.ValidateResponseSignation(AccessKey, SecretKey, out calculatedSignature))
            {
                errorOccured = true;
                LogError(order, "The signature returned from callback does not match: {0}, calculated: {1}", Context.Current.Request["signature"], calculatedSignature);
            }

            string transactionId = Context.Current.Request["transaction_id"];
            if (string.IsNullOrEmpty(transactionId))
            {
                errorOccured = true;
                LogEvent(order, "No transaction number sent to callback");
            }

            if (Context.Current.Request["reason_code"] != "100")
            {
                errorOccured = true;
                string messageId = Context.Current.Request["auth_response"];
                string messageText = Context.Current.Request["message"];
                LogEvent(order, "Transaction {0} not approved. CyberSource response: messageId: '{1}', messageText: '{2}'.", transactionId, messageId, messageText);

                return OnError(order, $"Transaction {transactionId} not approved. {messageText}", windowMode is WindowModes.Embedded);
            }

            string amount = Context.Current.Request["req_amount"];
            int decimals = string.IsNullOrEmpty(order.Currency.Rounding?.Id)
                ? 2
                : order.Currency.Rounding.Decimals;

            if (errorOccured)
            {
                LogError(order, "At least one validation error exists - exiting callback routine.");
                order.TransactionStatus = "Failed";
                Services.Orders.Save(order);
            }
            else
            {
                LogEvent(order, "Payment succeeded with transaction number {0}", transactionId);
                string requestCardType = Context.Current.Request["req_card_type"];
                string cardType = CardTypes.Keys.Any(key => key.Equals(requestCardType, StringComparison.OrdinalIgnoreCase))
                    ? CardTypes[requestCardType]
                    : $"Unrecognized card type - {requestCardType}";

                order.TransactionAmount = Math.Round(order.Price.Price, decimals);
                order.TransactionStatus = "Succeeded";
                order.TransactionCardType = cardType;
                order.TransactionCardNumber = HideCardNumber(Context.Current.Request["req_card_number"]);

                if (transactionType is TransactionTypes.Sale)
                {
                    string msg = "Capture succeeded";
                    LogEvent(order, msg);
                    order.CaptureInfo = new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, msg);
                }
                else if (transactionType is TransactionTypes.ZeroAuthorization)
                {
                    order.TransactionAmount = 0.00;
                    order.TransactionType = "Zero authorization";
                    string msg = "Zero authorization succeeded";
                    LogEvent(order, msg);
                    order.CaptureInfo = new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, msg);
                }
                SetOrderComplete(order, transactionId);
            }
        }
        catch (Exception ex)
        {
            LogError(order, ex, $"Unhandled exception: {ex.Message}");
        }

        if (!orderWasCompleted && order.Complete)
        {
            CheckoutDone(order);

            if (windowMode is not WindowModes.Embedded)
                return PassToCart(order);

            return new ContentOutputResult
            {
                Content = $"<script>parent.location.href = '{GetBaseUrl(order)}&cmd=Accept';</script>"
            };
        }

        return ContentOutputResult.Empty;
    }

    private OutputResult OnError(Order order, string message, bool isIFrameError = false)
    {
        if (windowMode is WindowModes.Embedded && isIFrameError)
        {
            return new ContentOutputResult
            {
                Content = $"<script>parent.location.href = '{GetBaseUrl(order)}&cmd=IFrameError&ErrorMessage={WebUtility.UrlEncode(message)}';</script>"
            };
        }

        order.TransactionAmount = 0;
        order.TransactionStatus = "Failed";
        order.Errors.Add(message);
        Services.Orders.Save(order);

        Services.Orders.DowngradeToCart(order);
        order.TransactionStatus = string.Empty;
        Common.Context.SetCart(order);

        if (string.IsNullOrWhiteSpace(ErrorTemplate))
            return PassToCart(order);

        var errorTemplate = new Template(TemplateHelper.GetTemplatePath(ErrorTemplate, ErrorTemplateFolder));
        errorTemplate.SetTag("CheckoutHandler:ErrorMessage", message);

        return new ContentOutputResult
        {
            Content = Render(order, errorTemplate)
        };
    }

    private Dictionary<string, string> PrepareAuthorizationRequest(Order order, string token = "") => PrepareRequest(order, "authorization", token);

    private Dictionary<string, string> PrepareSaleRequest(Order order, string token = "") => PrepareRequest(order, "sale", token);

    private Dictionary<string, string> PrepareCreateCardRequest(Order order) => transactionType switch
    {
        TransactionTypes.Sale => PrepareRequest(order, "sale,create_payment_token", ""),
        TransactionTypes.Authorization => PrepareRequest(order, "authorization,create_payment_token", ""),
        _ => PrepareRequest(order, "create_payment_token", "")
    };

    #region Request building

    private Dictionary<string, string> PrepareRequest(Order order, string requestTransactionType, string token)
    {
        string customerName = order.CustomerName?.Trim() ?? "";
        string firstName = Helper.GetCustomerFirstName(order, customerName);
        string lastName = Helper.GetCustomerLastName(order, customerName);
        string amount = transactionType is TransactionTypes.ZeroAuthorization
            ? "0.00"
            : Helper.GetTransactionAmount(order);

        var requestParameters = new Dictionary<string, string>
        {
            ["profile_id"] = ProfileId,
            ["access_key"] = AccessKey,
            ["transaction_uuid"] = Guid.NewGuid().ToString(),
            ["signed_date_time"] = DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["unsigned_field_names"] = "",
            ["locale"] = GetLanguageCode(),
            ["transaction_type"] = requestTransactionType,
            ["payment_method"] = "card",
            ["reference_number"] = !order.IsRecurringOrderTemplate ? order.Id : order.CustomerAccessUserId.ToString(),
            ["amount"] = amount,
            ["currency"] = order.Price.Currency.Code,
            ["override_custom_cancel_page"] = GetCancelUrl(order),
            ["override_custom_receipt_page"] = GetAcceptUrl(order),
            ["businessRules_declineAVSFlags"] = Decline_AVS_Flag,
            ["businessRules_ignoreAVSResult"] = Ignore_AVS_Result.ToString().ToLower()
        };

        if (!string.IsNullOrWhiteSpace(token))
        {
            requestParameters.Add("payment_token", token);
        }
        else
        {
            requestParameters = requestParameters.Union(new Dictionary<string, string>
            {
                ["bill_to_forename"] = firstName,
                ["bill_to_surname"] = lastName,
                ["bill_to_email"] = order.CustomerEmail ?? "",
                ["bill_to_phone"] = order.CustomerPhone ?? "",
                ["bill_to_company_name"] = order.CustomerCompany ?? "",
                ["bill_to_address_line1"] = order.CustomerAddress ?? "",
                ["bill_to_address_line2"] = order.CustomerAddress2 ?? "",
                ["bill_to_address_city"] = order.CustomerCity ?? "",
                ["bill_to_address_state"] = order.CustomerRegion ?? "",
                ["bill_to_address_postal_code"] = order.CustomerZip ?? "",
                ["bill_to_address_country"] = order.CustomerCountryCode ?? ""
            }).ToDictionary(param => param.Key, param => param.Value);
        }

        bool useBillInfoForDelivery = string.IsNullOrEmpty(order.DeliveryAddress);
        if (useBillInfoForDelivery)
        {
            requestParameters.Add("ship_to_forename", firstName);
            requestParameters.Add("ship_to_surname", lastName);
            requestParameters.Add("ship_to_email", order.CustomerEmail ?? "");
            requestParameters.Add("ship_to_phone", order.CustomerPhone ?? "");
            requestParameters.Add("ship_to_company_name", order.CustomerCompany ?? "");
            requestParameters.Add("ship_to_address_line1", order.CustomerAddress ?? "");
            requestParameters.Add("ship_to_address_line2", order.CustomerAddress2 ?? "");
            requestParameters.Add("ship_to_address_city", order.CustomerCity ?? "");
            requestParameters.Add("ship_to_address_state", order.CustomerRegion ?? "");
            requestParameters.Add("ship_to_address_postal_code", order.CustomerZip ?? "");
            requestParameters.Add("ship_to_address_country", order.CustomerCountryCode ?? "");
        }
        else
        {
            requestParameters.Add("ship_to_forename", string.IsNullOrWhiteSpace(order.DeliveryFirstName) ? firstName : order.DeliveryFirstName);
            requestParameters.Add("ship_to_surname", string.IsNullOrWhiteSpace(order.DeliverySurname) ? lastName : order.DeliverySurname);
            requestParameters.Add("ship_to_email", order.DeliveryEmail ?? "");
            requestParameters.Add("ship_to_phone", order.DeliveryPhone ?? "");
            requestParameters.Add("ship_to_company_name", order.DeliveryCompany ?? "");
            requestParameters.Add("ship_to_address_line1", order.DeliveryAddress ?? "");
            requestParameters.Add("ship_to_address_line2", order.DeliveryAddress2 ?? "");
            requestParameters.Add("ship_to_address_city", order.DeliveryCity ?? "");
            requestParameters.Add("ship_to_address_state", order.DeliveryRegion ?? "");
            requestParameters.Add("ship_to_address_postal_code", order.DeliveryZip ?? "");
            requestParameters.Add("ship_to_address_country", SupportedCountryCodes.Any(code => code.Equals(order.DeliveryCountryCode, StringComparison.OrdinalIgnoreCase))
                ? order.DeliveryCountryCode
                : string.Empty
            );
        }

        requestParameters = requestParameters
            .Where(param => !string.IsNullOrEmpty(param.Value))
            .ToDictionary(param => param.Key, param => param.Value);

        string signedFieldNames = string.Join(",", requestParameters.Keys) + ",signed_field_names";
        requestParameters.Add("signed_field_names", signedFieldNames);
        requestParameters.Add("signature", SecurityHelper.Sign(requestParameters, SecretKey));

        return requestParameters;
    }

    #endregion

    private string GetSavedCardName(Order order)
    {
        string fallbackCardName = order.DoSaveCardToken || order.IsRecurringOrderTemplate || ForceTokenization
            ? order.Id
            : string.Empty;

        return !string.IsNullOrWhiteSpace(order.SavedCardDraftName)
            ? order.SavedCardDraftName
            : fallbackCardName;
    }

    private string GetAcceptUrl(Order order)
    {
        string cardName = GetSavedCardName(order);
        string command = order.IsRecurringOrderTemplate || !string.IsNullOrWhiteSpace(cardName)
            ? "CardSaved"
            : "Accept";
        string queryString = !string.IsNullOrWhiteSpace(cardName)
            ? $"&CardTokenName={WebUtility.UrlEncode(cardName)}"
            : string.Empty;

        return $"{GetBaseUrl(order)}&cmd={command}{queryString}";
    }

    private string GetCancelUrl(Order order) => $"{GetBaseUrl(order)}&cmd=Cancel";

    private string GetLanguageCode() => Environment.ExecutingContext.GetCulture(true).TwoLetterISOLanguageName;

    #region Gateway URLs

    private string GetGatewayUrl(bool isIFrameMode)
    {
        if (isIFrameMode)
        {
            if (workMode is WorkModes.Production)
                return "https://secureacceptance.cybersource.com/embedded/pay";
            return "https://testsecureacceptance.cybersource.com/embedded/pay";
        }

        if (workMode is WorkModes.Production)
            return "https://secureacceptance.cybersource.com/pay";
        return "https://testsecureacceptance.cybersource.com/pay";
    }

    private string GetCreateCardGatewayUrl(bool isIFrameMode)
    {
        if (isIFrameMode)
        {
            if (workMode is WorkModes.Production)
                return "https://secureacceptance.cybersource.com/embedded/token/create";
            return "https://testsecureacceptance.cybersource.com/embedded/token/create";
        }

        if (workMode is WorkModes.Production)
            return "https://secureacceptance.cybersource.com/token/create";
        return "https://testsecureacceptance.cybersource.com/token/create";
    }

    #endregion

    #region ISavedCard interface

    public void DeleteSavedCard(int savedCardID)
    {
        //not supported due new cybersouyrce api
    }

    /// <summary>
    /// Directs checkout handler to use saved card
    /// </summary>
    /// <param name="order">Order that should be processed using saved card information</param>
    /// <returns>Empty string, if operation succeeded, otherwise string template with exception mesage</returns>
    public string UseSavedCard(Order order)
    {
        /*PassToCart part doesn't work because of changes in Redirect behavior.
        * We need to return RedirectOutputResult as OutputResult, and handle output result to make it work.
        * It means, that we need to change ISavedCard.UseSavedCard method, probably create new one (with OutputResult as returned type)
        * To make it work (temporarily), we use Response.Redirect here                 
        */

        try
        {
            if (UseSavedCardInternal(order) is RedirectOutputResult redirectResult)
                RedirectToCart(redirectResult);

            if (!order.Complete)
                return ProcessError("Some error happened on creating payment using saved card");

            return string.Empty;
        }
        catch (ThreadAbortException)
        {
            return string.Empty;
        }
        catch (Exception ex)
        {
            return ProcessError(ex.Message);
        }

        string ProcessError(string errorMessage)
        {
            LogEvent(order, $"Order not complete on using saved card. Error: {errorMessage}", DebuggingInfoType.UseSavedCard);
            OutputResult errorResult = OnError(order, $"Some error happened on creating payment using saved card: {errorMessage}");
            if (errorResult is ContentOutputResult contentErrorResult)
                return contentErrorResult.Content;
            if (errorResult is RedirectOutputResult redirectErrorResult)
                RedirectToCart(redirectErrorResult);

            return string.Empty;
        }
    }

    public bool SavedCardSupported(Order order)
    {
        string certPath = Helper.GetCertificateFilePath(CertificateFile);

        return !string.IsNullOrWhiteSpace(certPath);
    }

    private OutputResult UseSavedCardInternal(Order order)
    {
        PaymentCardToken savedCard = Services.PaymentCard.GetById(order.SavedCardId);
        if (savedCard is null || order.CustomerAccessUserId != savedCard.UserID)
            throw new Exception("Token is incorrect.");

        string certPath = Helper.GetCertificateFilePath(CertificateFile);
        if (string.IsNullOrWhiteSpace(certPath))
            throw new Exception("Certificate for REST API is not found");

        LogEvent(order, "Using saved card({0}) with id: {1}", savedCard.Identifier, savedCard.ID);

        if (order.IsRecurringOrderTemplate)
        {
            SetOrderComplete(order);
            LogEvent(order, "Recurring order template created");
            CheckoutDone(order);

            if (!order.Complete)
                throw new Exception("Some error happened on creating saved card.");

            return PassToCart(order);
        }

        var service = new CyberSourceService(GetHost(), MerchantId, CertificateFile, CertificatePassword);
        PaymentResponse response = service.CreatePayment(order, savedCard);

        string transactionId = response.Id;
        LogEvent(order, "Transaction succeeded with transaction number {0}", transactionId);

        int decimals = string.IsNullOrEmpty(order.Currency.Rounding?.Id)
            ? 2
            : order.Currency.Rounding.Decimals;

        order.TransactionAmount = Math.Round(order.Price.Price, decimals);
        order.TransactionStatus = "Succeeded";
        order.TransactionCardType = savedCard.CardType;
        order.TransactionCardNumber = savedCard.Identifier;
        if (transactionType is TransactionTypes.Sale)
        {
            string msg = "Capture succeeded";
            LogEvent(order, msg);
            order.CaptureInfo = new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, msg);
        }
        else if (transactionType is TransactionTypes.ZeroAuthorization)
        {
            order.TransactionAmount = 0.00;
            order.TransactionType = "Zero authorization";
            string msg = "Zero authorization succeeded";
            LogEvent(order, msg);
            order.CaptureInfo = new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, msg);
        }

        SetOrderComplete(order, transactionId);
        LogEvent(order, "Order completed");
        CheckoutDone(order);
        LogEvent(order, "Recurring successful");

        if (order.RecurringOrderId <= 0)
            return PassToCart(order);

        return NoActionOutputResult.Default;
    }

    /// <summary>
    /// A temporary method to maintain previous behavior. Redirects to cart by Response.Redirect. Please remove it when the needed changes will be done.
    /// </summary>
    private void RedirectToCart(RedirectOutputResult redirectResult) => Context.Current.Response.Redirect(redirectResult.RedirectUrl, redirectResult.IsPermanent);

    #endregion

    #region IRecurring

    public void Recurring(Order order, Order initialOrder)
    {
        if (order is null)
            return;

        try
        {
            UseSavedCardInternal(order);
            LogEvent(order, "Recurring succeeded");
        }
        catch (ThreadAbortException)
        {
        }
        catch (Exception ex)
        {
            LogEvent(order, "Recurring order failed for {0} (based on {1}). The payment failed with the message: {2}",
                DebuggingInfoType.RecurringError, order.Id, initialOrder.Id, ex.Message);
        }
    }

    public bool RecurringSupported(Order order) => true;

    #endregion

    #region ICheckAuthorizationStatus

    public AuthorizationStatus CheckAuthorizationStatus(Order order)
    {
        if (string.Equals(order.TransactionStatus, "Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(order.TransactionType, "Zero authorization", StringComparison.OrdinalIgnoreCase) && order.CaptureInfo.State is OrderCaptureInfo.OrderCaptureState.Success
                ? AuthorizationStatus.AuthorizedZeroAmount
                : AuthorizationStatus.AuthorizedFullAmount;
        }

        return AuthorizationStatus.NotAuthorized;
    }

    #endregion
}
