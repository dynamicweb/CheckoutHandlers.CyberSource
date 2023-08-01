using Dynamicweb.Core;
using Dynamicweb.Ecommerce.Cart;
using Dynamicweb.Ecommerce.Orders;
using Dynamicweb.Ecommerce.Orders.Gateways;
using Dynamicweb.Extensibility.AddIns;
using Dynamicweb.Extensibility.Editors;
using Dynamicweb.Rendering;
using Dynamicweb.Security.UserManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Web;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource
{
    /// <summary>
    /// CyberSource Checkout Handler
    /// </summary>
    [AddInName("CyberSource")]
    [AddInDescription("Payment system, http://www.cybersource.com")]
    public class CyberSource : CheckoutHandlerWithStatusPage, IDropDownOptions, IRemoteCapture, ISavedCard, IRecurring, ICheckAuthorizationStatus
    {
        internal enum WorkModes { Test, Production }
        internal enum WindowModes { Redirect, Embedded }
        internal enum TransactionTypes { ZeroAuthorization, Authorization, Sale }

        private static string[] supportedCountryCodes;
        private static string[] supportedCurrencyCodes;

        internal WorkModes workMode = WorkModes.Test;
        internal WindowModes windowMode = WindowModes.Redirect;
        private TransactionTypes transactionType = TransactionTypes.Sale;
        private string decline_AVS_Flag;
        private const string FormTemplateFolder = "eCom7/CheckoutHandler/CyberSource/Payment";
        private const string CancelTemplateFolder = "eCom7/CheckoutHandler/CyberSource/Cancel";
        private const string ErrorTemplateFolder = "eCom7/CheckoutHandler/CyberSource/Error";

        private static Dictionary<string, string> CardTypes = new Dictionary<string, string>
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
        private string paymentTemplate;
        private string cancelTemplate;
        private string errorTemplate;

        static CyberSource()
        {
            List<RegionInfo> cultures = new List<RegionInfo>();
            foreach (var r in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                try
                {
                    cultures.Add(new RegionInfo(r.Name));
                }
                catch
                {

                }
            }
            supportedCountryCodes = cultures.Select(x => x.TwoLetterISORegionName.ToUpper()).Distinct().ToArray();
            supportedCurrencyCodes = cultures.Select(x => x.ISOCurrencySymbol.ToUpper()).Distinct().ToArray();
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

        [AddInParameter("Merchant id"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true")]
        public string MerchantId { get; set; }

        [AddInParameter("Profile id"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true")]
        public string ProfileId { get; set; }

        [AddInParameter("Access key"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true")]
        public string AccessKey { get; set; }

        [AddInParameter("Secret key"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true;TextArea=true;")]
        public string SecretKey { get; set; }

        [AddInParameter("Certificate"), AddInParameterEditor(typeof(FileManagerEditor), "NewGUI=true;allowBrowse=true;folder=System;showfullpath=true;")]
        public string CertificateFile { get; set; }

        [AddInParameter("Transaction type")]
        [AddInParameterEditor(typeof(RadioParameterEditor), "")]
        public string TransactionType
        {
            get { return transactionType.ToString(); }
            set { Enum.TryParse<TransactionTypes>(value, out transactionType); }
        }

        [AddInParameter("Forced tokenization(always store token for users: as saved card or on order)"), AddInParameterEditor(typeof(YesNoParameterEditor), "")]
        public bool ForceTokenization { get; set; }

        [AddInParameter("Payment template"), AddInParameterEditor(typeof(TemplateParameterEditor), $"folder=templates/{FormTemplateFolder}")]
        public string PaymentTemplate
        {
            get
            {
                return TemplateHelper.GetTemplateName(paymentTemplate);
            }
            set => paymentTemplate = value;
        }

        [AddInParameter("Cancel template"), AddInParameterEditor(typeof(TemplateParameterEditor), $"folder=templates/{CancelTemplateFolder}")]
        public string CancelTemplate
        {
            get
            {
                return TemplateHelper.GetTemplateName(cancelTemplate);
            }
            set => cancelTemplate = value;
        }

        [AddInParameter("Error template"), AddInParameterEditor(typeof(TemplateParameterEditor), $"folder=templates/{ErrorTemplateFolder}")]
        public string ErrorTemplate
        {
            get
            {
                return TemplateHelper.GetTemplateName(errorTemplate);
            }
            set => errorTemplate = value;
        }

        [AddInParameter("Work Mode"), AddInParameterEditor(typeof(RadioParameterEditor), "")]
        public string WorkMode
        {
            get { return workMode.ToString(); }
            set { Enum.TryParse<WorkModes>(value, out workMode); }
        }

        [AddInParameter("Window Mode"), AddInParameterEditor(typeof(RadioParameterEditor), "")]
        public string WindowMode
        {
            get { return windowMode.ToString(); }
            set { Enum.TryParse<WindowModes>(value, out windowMode); }
        }


        [AddInParameter("Review AVS Codes"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true")]
        public string Decline_AVS_Flag
        {
            get { return string.IsNullOrEmpty(decline_AVS_Flag) ? "N" : decline_AVS_Flag; }
            set { decline_AVS_Flag = value; }
        }

        [AddInParameter("Ignore AVS Result"), AddInParameterEditor(typeof(YesNoParameterEditor), "")]
        public bool Ignore_AVS_Result { get; set; } = false;

        [AddInParameter("Approve AVS Code"), AddInParameterEditor(typeof(TextParameterEditor), "NewGUI=true")]
        public string Result_AVS_Flag { get; set; }
        #endregion

        /// <summary>
        /// Gets options according to behavior mode
        /// </summary>
        /// <param name="behaviorMode"></param>
        /// <returns>Key-value pairs of settings</returns>
        Hashtable IDropDownOptions.GetOptions(string behaviorMode)
        {
            try
            {
                switch (behaviorMode)
                {
                    case "Work Mode":
                        return new Hashtable {
                            { WorkModes.Test.ToString(), "Test" },
                            { WorkModes.Production.ToString(), "Production" }
                                   };
                    case "Window Mode":
                        return new Hashtable
                                   {
                                       {WindowModes.Redirect.ToString(), "Redirect"},
                                       {WindowModes.Embedded.ToString(), "Embedded"}
                                   };
                    case "Transaction type":
                        return new Hashtable {
                            { TransactionTypes.ZeroAuthorization.ToString(), "Authorization (zero amount)"},
                            { TransactionTypes.Authorization.ToString(), "Authorization (order amount)"},
                            { TransactionTypes.Sale.ToString(), "Sale" }
                        };
                    default:
                        throw new ArgumentException(string.Format("Unknown dropdown name: '{0}'", behaviorMode));
                }
            }
            catch (System.Threading.ThreadAbortException)
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
                var errorMessage = string.Empty;
                if (order == null || string.IsNullOrEmpty(order.Id))
                {
                    errorMessage = "No valid Order object set";
                }
                else if (string.IsNullOrWhiteSpace(order.TransactionNumber))
                {
                    errorMessage = "No transaction number set on the order";
                }

                var certPath = GetCertificateFilePath();
                if (string.IsNullOrWhiteSpace(certPath))
                {
                    errorMessage = "No certificate not found";
                }

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    LogEvent(order, errorMessage);
                    return new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Failed, errorMessage);
                }

                var request = new
                {
                    clientReferenceInformation = new
                    {
                        code = order.Id
                    },
                    orderInformation = new
                    {
                        amountDetails = new
                        {
                            totalAmount = GetTransactionAmount(order),
                            currency = order.Price.Currency.Code
                        }
                    },
                };

                var requestJson = Converter.Serialize(request);
                var url = $"https://{(workMode == WorkModes.Production ? "api" : "apitest")}.cybersource.com/pts/v2/payments/{order.TransactionNumber}/captures";
                var response = CallCyberSourceAPI(requestJson, url, order);
                if (response.Result.StatusCode == HttpStatusCode.Created)
                {
                    LogEvent(order, "Capture successful", DebuggingInfoType.CaptureResult);
                    return new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, "Capture successful");
                }
                else
                {
                    string responseJson = Converter.Serialize(response);
                    string message = $"Remote Capture failed. Response: {responseJson}";
                    LogEvent(order, message, DebuggingInfoType.CaptureResult);
                    return new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Failed, message);
                }
            }
            catch (System.Threading.ThreadAbortException)
            {
                return new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Failed, "System.Threading.ThreadAbortException");
            }
            catch (Exception ex)
            {
                var message = string.Format("Remote capture failed with the message: {0}", ex.Message);
                LogError(order, ex, message);

                return new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Failed, message);
            }
        }

        /// <summary>
        /// Starts order checkout procedure
        /// </summary>
        /// <param name="order">Order to be checked out</param>
        /// <returns>String representation of template output</returns>
        public override string StartCheckout(Order order)
        {
            try
            {
                string errorMessage;
                if (!ValidateOrderFields(order, out errorMessage))
                {
                    return OnError(order, errorMessage);
                }

                bool isIFrameMode = windowMode == WindowModes.Embedded;

                Dictionary<string, string> form;
                string gatewayUrl;

                if (order.IsRecurringOrderTemplate || !String.IsNullOrWhiteSpace(GetSavedCardName(order)))
                {
                    gatewayUrl = transactionType == TransactionTypes.ZeroAuthorization ? GetCreateCardGatewayUrl(isIFrameMode) : GetGatewayUrl(isIFrameMode);
                    form = PrepareCreateCardRequest(order);
                }
                else
                {
                    gatewayUrl = GetGatewayUrl(isIFrameMode);
                    if (transactionType == TransactionTypes.Sale)
                    {
                        form = PrepareSaleRequest(order);
                    }
                    else
                    {
                        form = PrepareAuthorizationRequest(order);
                    }
                }

                if (isIFrameMode)
                {
                    return RenderPaymentFrame(order, gatewayUrl, form);
                }
                else
                {
                    SubmitForm(gatewayUrl, form);
                }
            }
            catch (System.Threading.ThreadAbortException)
            {
                return string.Empty;
            }
            catch (Exception ex)
            {
                LogError(order, ex, "Unhandled exception with message: {0}", ex.Message);
                return OnError(order, ex.Message);
            }

            return string.Empty;
        }


        private string RenderPaymentFrame(Orders.Order order, string gatewayUrl, Dictionary<string, string> form)
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

            var loopTemplate = formTemplate.GetLoop("CyberSourceFields");
            foreach (var field in form)
            {
                loopTemplate.SetTag("CyberSource.FieldName", field.Key);
                loopTemplate.SetTag("CyberSource.FieldValue", field.Value);
                loopTemplate.CommitLoop();
            }

            // Render and return
            return this.Render(order, formTemplate);
        }

        public override string Redirect(Order order)
        {
            LogEvent(order, "Redirected to CyberSource CheckoutHandler");
            string result;

            switch (Dynamicweb.Context.Current.Request["cmd"])
            {
                case "Accept":
                    result = ValidateAVSCode(order);
                    if (result != null)
                    {
                        return result;
                    }
                    return StateOk(order);
                case "CardSaved":
                    result = ValidateAVSCode(order);
                    if (result != null)
                    {
                        return result;
                    }
                    return StateCardSaved(order);
                case "Cancel":
                    return StateCancel(order);
                case "IFrameError":
                    return StateIFrameError(order);
                default:
                    Context.Current.Response.End();
                    return null;
            }
        }

        private string ValidateAVSCode(Order order)
        {
            string transact = Context.Current.Request["transaction_id"];
            string avsResult = Context.Current.Request["auth_avs_code"];
            string avsResultRaw = Context.Current.Request["auth_avs_code_raw"];
            var resultCodesAllowed = new List<string>();
            if (!string.IsNullOrWhiteSpace(Result_AVS_Flag))
            {
                resultCodesAllowed.AddRange(Result_AVS_Flag.Replace(' ', ',').Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            }
            LogEvent(order, "CyberSource response: avs_code: '{0}', avs_code_raw: '{1}'.", avsResult, avsResultRaw);

            if (!string.IsNullOrEmpty(avsResult) && resultCodesAllowed.Any() && !resultCodesAllowed.Contains(avsResult))
            {
                LogEvent(order, "Transaction {0} not approved.", transact);
                return OnError(order, string.Format("Transaction {0} not approved. {1} (code={2})"
                        , transact, Context.Current.Request["message"], avsResult), windowMode == WindowModes.Embedded);
            }

            return null;
        }

        private bool ValidateOrderFields(Order order, out string errorMessage)
        {
            if (!supportedCurrencyCodes.Any(x => x == order.CurrencyCode))
            {
                errorMessage = $"Only {string.Join(",", supportedCurrencyCodes)} currency codes is allowed. Order currency: {order.CurrencyCode}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(order.CustomerCountryCode))
            {
                errorMessage = "Required customer country code";
                return false;
            }

            if (!supportedCountryCodes.Any(x => x == order.CustomerCountryCode))
            {
                errorMessage = $"Only {string.Join(",", supportedCountryCodes)} country codes is supported. Order country code: {order.CustomerCountryCode}";
                return false;
            }

            errorMessage = string.Empty;

            return true;
        }

        private string StateCancel(Order order)
        {
            LogEvent(order, "State cancel");
            string calculatedSignature;
            if (windowMode != WindowModes.Embedded && !Security.ValidateResponseSignation(AccessKey, SecretKey, out calculatedSignature))
            {
                LogError(order, "The signature returned from callback does not match: {0}, calculated: {1}", Dynamicweb.Context.Current.Request["signature"], calculatedSignature);
                return OnError(order, "Wrong signature");
            }

            order.TransactionStatus = "Cancelled";
            Services.Orders.Save(order);
            CheckoutDone(order);

            var cancelTemplate = new Template(TemplateHelper.GetTemplatePath(CancelTemplate, CancelTemplateFolder));
            var orderRenderer = new Dynamicweb.Ecommerce.Frontend.Renderer();
            orderRenderer.RenderOrderDetails(cancelTemplate, order, true);

            return cancelTemplate.Output();
        }

        private string StateIFrameError(Order order)
        {
            return OnError(order, Dynamicweb.Context.Current.Request["ErrorMessage"]);
        }

        private string StateOk(Order order)
        {
            LogEvent(order, "State ok");

            if (!order.Complete)
            {
                return ProcessOrder(order);
            }

            RedirectToCart(order);

            return null;
        }


        private string StateCardSaved(Order order)
        {
            LogEvent(order, "CyberSource Card Authorized successfully");

            var cardName = HttpUtility.UrlDecode(Context.Current.Request["CardTokenName"]);
            if (string.IsNullOrEmpty(cardName))
            {
                cardName = order.Id;
            }

            if (Context.Current.Request["reason_code"] == "100")
            {
                var requestCardType = Context.Current.Request["req_card_type"];
                var subscribtionId = Context.Current.Request["payment_token"];
                var cardType = CardTypes.Keys.Any(key => key == requestCardType) ? CardTypes[requestCardType] : String.Format("Unrecognized card type - {0}", requestCardType);
                var cardNubmer = order.TransactionCardNumber = Context.Current.Request["req_card_number"].ToUpper();

                order.TransactionCardType = cardType;
                string transactionId = Dynamicweb.Context.Current.Request["transaction_id"];

                var user = User.GetUserByID(order.CustomerAccessUserId);
                if (user != null)
                {
                    var savedCard = Services.PaymentCard.CreatePaymentCard(user.ID, order.PaymentMethodId, cardName, cardType, cardNubmer, subscribtionId);
                    order.SavedCardId = savedCard.ID;
                }
                else
                {
                    order.TransactionToken = subscribtionId;
                }

                int decimals = order.Currency.Rounding == null || order.Currency.Rounding.Id == string.Empty ? 2 : order.Currency.Rounding.Decimals;
                if (!order.IsRecurringOrderTemplate && transactionType == TransactionTypes.ZeroAuthorization)
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
                else if (transactionType == TransactionTypes.Sale)
                {
                    order.TransactionAmount = Math.Round(order.Price.Price, decimals);
                    order.TransactionStatus = "Succeeded";
                    string msg = "Capture succeeded";
                    LogEvent(order, msg);
                    order.CaptureInfo = new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, msg);
                }
                else if (transactionType == TransactionTypes.Authorization)
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
            {
                LogError(order, string.Format("Create card failed. Decision: '{0}' messageId: '{1}', messageText: '{2}'", Context.Current.Request["decision"], Context.Current.Request["reason_code"], Context.Current.Request["message"]));
            }
            CheckoutDone(order);

            if (!order.Complete)
            {
                return OnError(order, "Some error happened on creating saved card.", windowMode == WindowModes.Embedded);
            }

            if (windowMode != WindowModes.Embedded)
            {
                RedirectToCart(order);
            }
            else
            {
                Context.Current.Response.Write(string.Format("<script>parent.location.href = '{0}&cmd=Accept';</script>", GetBaseUrl(order)));
                Context.Current.Response.End();
            }
            return null;
        }

        private string ProcessOrder(Order order)
        {
            bool orderWasCompleted = order.Complete;

            try
            {
                bool errorOccured = false;
                string calculatedSignature;

                if (!Security.ValidateResponseSignation(AccessKey, SecretKey, out calculatedSignature))
                {
                    errorOccured = true;
                    LogError(order, "The signature returned from callback does not match: {0}, calculated: {1}", Dynamicweb.Context.Current.Request["signature"], calculatedSignature);
                }

                string transactionId = Dynamicweb.Context.Current.Request["transaction_id"];
                if (string.IsNullOrEmpty(transactionId))
                {
                    errorOccured = true;
                    LogEvent(order, "No transaction number sent to callback");
                }

                if (Dynamicweb.Context.Current.Request["reason_code"] != "100")
                {
                    errorOccured = true;
                    LogEvent(order, "Transaction {0} not approved. CyberSource response: messageId: '{1}', messageText: '{2}'.", transactionId, Dynamicweb.Context.Current.Request["auth_response"], Dynamicweb.Context.Current.Request["message"]);

                    return OnError(order, string.Format("Transaction {0} not approved. {1}", transactionId, Dynamicweb.Context.Current.Request["message"]), windowMode == WindowModes.Embedded);
                }

                string amount = Dynamicweb.Context.Current.Request["req_amount"];
                int decimals = order.Currency.Rounding == null || order.Currency.Rounding.Id == string.Empty ? 2 : order.Currency.Rounding.Decimals;

                if (errorOccured)
                {
                    LogError(order, "At least one validation error exists - exiting callback routine.");
                    order.TransactionStatus = "Failed";
                    Services.Orders.Save(order);
                }
                else
                {
                    LogEvent(order, "Payment succeeded with transaction number {0}", transactionId);
                    var requestCardType = Dynamicweb.Context.Current.Request["req_card_type"];
                    var cardType = CardTypes.Keys.Any(key => key == requestCardType) ? CardTypes[requestCardType] : String.Format("Unrecognized card type - {0}", requestCardType);

                    order.TransactionAmount = Math.Round(order.Price.Price, decimals);
                    order.TransactionStatus = "Succeeded";
                    order.TransactionCardType = cardType;
                    order.TransactionCardNumber = HideCardNumber(Dynamicweb.Context.Current.Request["req_card_number"]);

                    if (transactionType == TransactionTypes.Sale)
                    {
                        string msg = "Capture succeeded";
                        LogEvent(order, msg);
                        order.CaptureInfo = new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, msg);
                    }
                    else if (transactionType == TransactionTypes.ZeroAuthorization)
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
            finally
            {
                if (!orderWasCompleted && order.Complete)
                {
                    CheckoutDone(order);

                    if (windowMode != WindowModes.Embedded)
                    {
                        RedirectToCart(order);
                    }
                    else
                    {
                        Context.Current.Response.Write(string.Format("<script>parent.location.href = '{0}&cmd=Accept';</script>", GetBaseUrl(order)));
                        Context.Current.Response.End();
                    }
                }
            }
            return null;
        }

        private string OnError(Orders.Order order, string message, bool isIFrameError = false)
        {
            if (windowMode == WindowModes.Embedded && isIFrameError)
            {
                Context.Current.Response.Write(string.Format("<script>parent.location.href = '{0}&cmd=IFrameError&ErrorMessage={1}';</script>", GetBaseUrl(order), HttpUtility.UrlEncode(message)));
                Context.Current.Response.End();
            }

            order.TransactionAmount = 0;
            order.TransactionStatus = "Failed";
            order.Errors.Add(message);
            Services.Orders.Save(order);

            Services.Orders.DowngradeToCart(order);
            order.CartV2StepIndex = 0;
            order.TransactionStatus = string.Empty;
            Common.Context.SetCart(order);

            if (string.IsNullOrWhiteSpace(ErrorTemplate))
            {
                RedirectToCart(order);
            }

            var errorTemplate = new Template(TemplateHelper.GetTemplatePath(ErrorTemplate, ErrorTemplateFolder));
            errorTemplate.SetTag("CheckoutHandler:ErrorMessage", message);

            return Render(order, errorTemplate);
        }

        private Dictionary<string, string> PrepareAuthorizationRequest(Order order, string token = "")
        {
            return PrepareRequest(order, "authorization", token);
        }

        private Dictionary<string, string> PrepareSaleRequest(Order order, string token = "")
        {
            return PrepareRequest(order, "sale", token);
        }

        private Dictionary<string, string> PrepareCreateCardRequest(Order order)
        {
            if (transactionType == TransactionTypes.Sale)
            {
                return PrepareRequest(order, "sale,create_payment_token", "");
            }
            else if (transactionType == TransactionTypes.Authorization)
            {
                return PrepareRequest(order, "authorization,create_payment_token", "");
            }
            else
            {
                return PrepareRequest(order, "create_payment_token", "");
            }
        }

        #region Request building

        private Dictionary<string, string> PrepareRequest(Order order, string requestTransactionType, string token)
        {
            var customerName = Converter.ToString(order.CustomerName).Trim();

            var firstName = GetCustomerFirstName(order, customerName);
            var lastName = GetCustomerLastName(order, customerName);

            string amount = transactionType == TransactionTypes.ZeroAuthorization ? "0.00" : GetTransactionAmount(order);
            var requestParameters = new Dictionary<string, string>
            {
                {"profile_id", ProfileId},
                {"access_key", AccessKey},
                {"transaction_uuid", Guid.NewGuid().ToString()},
                {"signed_date_time", DateTime.Now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")},
                {"unsigned_field_names", ""},
                {"locale", GetLanguageCode()},

                {"transaction_type", requestTransactionType},
                {"payment_method", "card"},
                {"reference_number", !order.IsRecurringOrderTemplate ? order.Id : order.CustomerAccessUserId.ToString()},
                {"amount", amount},
                {"currency", order.Price.Currency.Code},
                {"override_custom_cancel_page", GetCancelUrl(order)},
                {"override_custom_receipt_page", GetAcceptUrl(order)},
                {"businessRules_declineAVSFlags", Decline_AVS_Flag },
                {"businessRules_ignoreAVSResult", Ignore_AVS_Result.ToString().ToLower() }
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                requestParameters.Add("payment_token", token);
            }
            else
            {
                requestParameters = requestParameters.Union(
                    new Dictionary<string, string>
                    {
                        {"bill_to_forename", firstName},
                        {"bill_to_surname", lastName},
                        {"bill_to_email", Converter.ToString(order.CustomerEmail)},
                        {"bill_to_phone", Converter.ToString(order.CustomerPhone)},
                        {"bill_to_company_name", Converter.ToString(order.CustomerCompany)},
                        {"bill_to_address_line1", Converter.ToString(order.CustomerAddress)},
                        {"bill_to_address_line2", Converter.ToString(order.CustomerAddress2)},
                        {"bill_to_address_city", Converter.ToString(order.CustomerCity)},
                        {"bill_to_address_state", Converter.ToString(order.CustomerRegion)},
                        {"bill_to_address_postal_code", Converter.ToString(order.CustomerZip)},
                        {"bill_to_address_country", Converter.ToString(order.CustomerCountryCode)},

                    }).ToDictionary(x => x.Key, x => x.Value);
            }
            bool useBillInfoForDelivery = string.IsNullOrEmpty(order.DeliveryAddress);
            if (useBillInfoForDelivery)
            {
                requestParameters.Add("ship_to_forename", firstName);
                requestParameters.Add("ship_to_surname", lastName);
                requestParameters.Add("ship_to_email", Converter.ToString(order.CustomerEmail));
                requestParameters.Add("ship_to_phone", Converter.ToString(order.CustomerPhone));
                requestParameters.Add("ship_to_company_name", Converter.ToString(order.CustomerCompany));
                requestParameters.Add("ship_to_address_line1", Converter.ToString(order.CustomerAddress));
                requestParameters.Add("ship_to_address_line2", Converter.ToString(order.CustomerAddress2));
                requestParameters.Add("ship_to_address_city", Converter.ToString(order.CustomerCity));
                requestParameters.Add("ship_to_address_state", Converter.ToString(order.CustomerRegion));
                requestParameters.Add("ship_to_address_postal_code", Converter.ToString(order.CustomerZip));
                requestParameters.Add("ship_to_address_country", Converter.ToString(order.CustomerCountryCode));
            }
            else
            {
                requestParameters.Add("ship_to_forename", string.IsNullOrWhiteSpace(order.DeliveryFirstName) ? firstName : order.DeliveryFirstName);
                requestParameters.Add("ship_to_surname", string.IsNullOrWhiteSpace(order.DeliverySurname) ? lastName : order.DeliverySurname);
                requestParameters.Add("ship_to_email", Converter.ToString(order.DeliveryEmail));
                requestParameters.Add("ship_to_phone", Converter.ToString(order.DeliveryPhone));
                requestParameters.Add("ship_to_company_name", Converter.ToString(order.DeliveryCompany));
                requestParameters.Add("ship_to_address_line1", Converter.ToString(order.DeliveryAddress));
                requestParameters.Add("ship_to_address_line2", Converter.ToString(order.DeliveryAddress2));
                requestParameters.Add("ship_to_address_city", Converter.ToString(order.DeliveryCity));
                requestParameters.Add("ship_to_address_state", Converter.ToString(order.DeliveryRegion));
                requestParameters.Add("ship_to_address_postal_code", Converter.ToString(order.DeliveryZip));
                requestParameters.Add("ship_to_address_country", supportedCountryCodes.Any(x => x == order.DeliveryCountryCode) ? order.DeliveryCountryCode : string.Empty);
            }

            requestParameters = requestParameters.Where(x => !string.IsNullOrEmpty(x.Value)).ToDictionary(x => x.Key, x => x.Value);
            var signedFieldNames = string.Join(",", requestParameters.Keys) + ",signed_field_names";
            requestParameters.Add("signed_field_names", signedFieldNames);
            requestParameters.Add("signature", Security.Sign(requestParameters, SecretKey));

            return requestParameters;
        }

        private static string GetCustomerLastName(Order order, string customerName)
        {
            string lastName = order.CustomerSurname;
            var delimeterPosition = customerName.IndexOf(' ');
            if (string.IsNullOrWhiteSpace(lastName))
            {
                lastName = delimeterPosition > -1 ? customerName.Substring(delimeterPosition + 1) : customerName;
            }

            return lastName;
        }

        private static string GetCustomerFirstName(Order order, string customerName)
        {
            var firstName = order.CustomerFirstName;
            var delimeterPosition = customerName.IndexOf(' ');
            if (string.IsNullOrWhiteSpace(firstName))
            {
                firstName = delimeterPosition > -1 ? customerName.Substring(0, delimeterPosition) : customerName;
            }
            return firstName;
        }

        #endregion

        /// <summary>
        /// This demonstrates what a generic API request helper method would look like.
        /// </summary>
        /// <param name="request">Request to send to API endpoint<</param>
        /// <returns>Task</returns>
        public async Task<HttpResponseMessage> CallCyberSourceAPI(string request, string resource, Order order)
        {
            var client = new HttpClient();

            var jwtToken = GenerateJWT(request, "POST", order);

            LogEvent(order, "JWT token created", jwtToken);

            StringContent content = new StringContent(request);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

            var response = await client.PostAsync(resource, content);
            return response;
        }

        /// <summary>
        /// This method demonstrates the creation of the JWT Authentication credential
        /// Takes Request Paylaod and Http method(GET/POST) as input.
        /// </summary>
        /// <param name="request">Value from which to generate JWT</param>
        /// <param name="method">The HTTP Verb that is needed for generating the credential</param>
        /// <returns>String containing the JWT Authentication credential</returns>
        public string GenerateJWT(string request, string method, Order order)
        {
            string digest;
            string token = "TOKEN_PLACEHOLDER";

            try
            {
                // Generate the hash for the payload
                using (SHA256 sha256Hash = SHA256.Create())
                {
                    byte[] payloadBytes = sha256Hash.ComputeHash(Encoding.ASCII.GetBytes(request));
                    digest = Convert.ToBase64String(payloadBytes);
                }

                // Create the JWT payload (aka claimset / JWTBody)
                string jwtBody = "0";

                if (method.Equals("POST"))
                {
                    jwtBody = "{\n\"digest\":\"" + digest + "\", \"digestAlgorithm\":\"SHA-256\", \"iat\":\"" + DateTime.Now.ToUniversalTime().ToString("r") + "\"}";
                }
                else if (method.Equals("GET"))
                {
                    jwtBody = "{\"iat\":\"" + DateTime.Now.ToUniversalTime().ToString("r") + "\"}";
                }


                // P12 certificate public key is sent in the header and the private key is used to sign the token
                X509Certificate2 x5Cert = new X509Certificate2(GetCertificateFilePath(), MerchantId, X509KeyStorageFlags.MachineKeySet);

                // Extracting Public Key from .p12 file
                string x5cPublicKey = Convert.ToBase64String(x5Cert.RawData);

                // Extracting Private Key from .p12 file
                var privateKey = x5Cert.GetRSAPrivateKey();

                // Extracting serialNumber
                string serialNumber = null;
                string serialNumberPrefix = "SERIALNUMBER=";

                string principal = x5Cert.Subject;

                int beg = principal.IndexOf(serialNumberPrefix);
                if (beg >= 0)
                {
                    int x5cBase64List = principal.IndexOf(",", beg);
                    if (x5cBase64List == -1)
                    {
                        x5cBase64List = principal.Length;
                    }

                    serialNumber = principal.Substring(serialNumberPrefix.Length, x5cBase64List - serialNumberPrefix.Length);
                }

                // Create the JWT Header custom fields
                var x5cList = new List<string>()
                {
                    x5cPublicKey
                };
                var cybsHeaders = new Dictionary<string, object>()
                {
                    { "v-c-merchant-id", MerchantId },
                    { "x5c", x5cList }
                };

                // JWT token is Header plus the Body plus the Signature of the Header & Body
                // Here the Jose-JWT helper library (https://github.com/dvsekhvalnov/jose-jwt) is used create the JWT
                token = Jose.JWT.Encode(jwtBody, privateKey, Jose.JwsAlgorithm.RS256, cybsHeaders);
            }
            catch (Exception ex)
            {
                LogError(order, ex, "JWT token create failed");
            }

            return token;
        }

        private string GetSavedCardName(Order order)
        {
            return !string.IsNullOrWhiteSpace(order.SavedCardDraftName) ? order.SavedCardDraftName : (order.DoSaveCardToken || order.IsRecurringOrderTemplate || ForceTokenization ? order.Id : "");
        }

        private string GetAcceptUrl(Order order)
        {
            var cardName = GetSavedCardName(order);
            return string.Format("{0}&cmd={1}{2}", GetBaseUrl(order), (order.IsRecurringOrderTemplate || !string.IsNullOrWhiteSpace(cardName)) ? "CardSaved" : "Accept",
                !string.IsNullOrWhiteSpace(cardName) ? string.Format("&CardTokenName={0}", HttpUtility.UrlEncode(cardName)) : "");
        }

        private string GetCancelUrl(Order order)
        {
            return string.Format("{0}&cmd=Cancel", GetBaseUrl(order));
        }

        private string GetLanguageCode()
        {
            var currentLanguageCode = Dynamicweb.Environment.ExecutingContext.GetCulture(true).TwoLetterISOLanguageName;
            return currentLanguageCode;
        }

        #region Gateway URLs

        private string GetGatewayUrl(bool isIFrameMode)
        {
            if (isIFrameMode)
            {
                if (workMode == WorkModes.Production)
                {
                    return "https://secureacceptance.cybersource.com/embedded/pay";
                }
                return "https://testsecureacceptance.cybersource.com/embedded/pay";
            }

            if (workMode == WorkModes.Production)
            {
                return "https://secureacceptance.cybersource.com/pay";
            }
            return "https://testsecureacceptance.cybersource.com/pay";
        }

        private string GetCreateCardGatewayUrl(bool isIFrameMode)
        {
            if (isIFrameMode)
            {
                if (workMode == WorkModes.Production)
                {
                    return "https://secureacceptance.cybersource.com/embedded/token/create";
                }
                return "https://testsecureacceptance.cybersource.com/embedded/token/create";
            }

            if (workMode == WorkModes.Production)
            {
                return "https://secureacceptance.cybersource.com/token/create";
            }
            return "https://testsecureacceptance.cybersource.com/token/create";
        }

        #endregion

        private string GetCertificateFilePath()
        {
            if (string.IsNullOrWhiteSpace(CertificateFile))
            {
                return string.Empty;
            }
            var path = Context.Current.Server.MapPath(string.Format("/Files/{0}", CertificateFile));
            if (File.Exists(path))
            {
                return path;
            }
            return string.Empty;
        }

        private string GetTransactionAmount(Order order)
        {
            int decimals = order.Currency.Rounding == null || order.Currency.Rounding.Id == "" ? 2 : order.Currency.Rounding.Decimals;
            string amount = Math.Round(order.Price.Price, decimals).ToString("0.00", System.Globalization.CultureInfo.CreateSpecificCulture("en-US"));
            return amount;
        }

        #region ISavedCard interface


        public void DeleteSavedCard(int savedCardID)
        {
            //not supported due new cybersouyrce api
        }

        public string UseSavedCard(Orders.Order order)
        {
            try
            {
                UseSavedCardInternal(order);
                if (!order.Complete)
                {
                    LogEvent(order, "Order not complete on using saved card", DebuggingInfoType.UseSavedCard);
                    return OnError(order, "Some error happened on creating payment using saved card");
                }
                return string.Empty;
            }
            catch (System.Threading.ThreadAbortException)
            {
                return string.Empty;
            }
            catch (Exception ex)
            {
                LogEvent(order, ex.Message, DebuggingInfoType.UseSavedCard);
                return OnError(order, ex.Message);
            }
        }

        public bool SavedCardSupported(Orders.Order order)
        {
            return !string.IsNullOrWhiteSpace(GetCertificateFilePath());
        }

        private async void UseSavedCardInternal(Orders.Order order)
        {
            var savedCard = Services.PaymentCard.GetById(order.SavedCardId);
            if (savedCard == null || order.CustomerAccessUserId != savedCard.UserID)
            {
                throw new Exception("Token is incorrect.");
            }

            var certPath = GetCertificateFilePath();
            if (string.IsNullOrWhiteSpace(certPath))
            {
                LogError(order, "No certificate not found");
                return;
            }

            LogEvent(order, "Using saved card({0}) with id: {1}", savedCard.Identifier, savedCard.ID);

            if (order.IsRecurringOrderTemplate)
            {
                SetOrderComplete(order);
                LogEvent(order, "Recurring order template created");
                CheckoutDone(order);

                if (!order.Complete)
                {
                    LogError(order, "Some error happened on creating saved card.");
                }
                RedirectToCart(order);
            }
            else
            {
                string request = PreparePaymentRequest(order, savedCard);

                var url = $"https://{(workMode == WorkModes.Production ? "api" : "apitest")}.cybersource.com/pts/v2/payments";
                var response = CallCyberSourceAPI(request, url, order);
                var responseContent = await response.Result.Content.ReadAsStringAsync();
                var responseJson = Converter.Deserialize<Dictionary<string, object>>(responseContent);

                if (response.Result.StatusCode == HttpStatusCode.Created)
                {
                    var transactionId = Converter.ToString(responseJson["id"]);

                    LogEvent(order, "Transaction succeeded with transaction number {0}", transactionId);

                    int decimals = order.Currency.Rounding == null || order.Currency.Rounding.Id == "" ? 2 : order.Currency.Rounding.Decimals;
                    order.TransactionAmount = Math.Round(order.Price.Price, decimals);
                    order.TransactionStatus = "Succeeded";
                    order.TransactionCardType = savedCard.CardType;
                    order.TransactionCardNumber = savedCard.Identifier;
                    if (transactionType == TransactionTypes.Sale)
                    {
                        string msg = "Capture succeeded";
                        LogEvent(order, msg);
                        order.CaptureInfo = new OrderCaptureInfo(OrderCaptureInfo.OrderCaptureState.Success, msg);
                    }
                    else if (transactionType == TransactionTypes.ZeroAuthorization)
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
                }
                else
                {
                    var errorMessage = $"Payment using saved card information failed. Response: {responseContent}";
                    LogError(order, errorMessage);
                    return;
                }
            }

            if (order.RecurringOrderId <= 0)
            {
                RedirectToCart(order);
            }
        }

        private string PreparePaymentRequest(Order order, PaymentCardToken savedCard)
        {
            var customerName = Converter.ToString(order.CustomerName).Trim();
            var firstName = GetCustomerFirstName(order, customerName);
            var lastName = GetCustomerLastName(order, customerName);
            var request = new
            {
                clientReferenceInformation = new
                {
                    code = order.Id
                },
                paymentInformation = new
                {
                    legacyToken = new
                    {
                        id = savedCard.Token
                    }
                },
                orderInformation = new
                {
                    amountDetails = new
                    {
                        totalAmount = GetTransactionAmount(order),
                        currency = order.Price.Currency.Code
                    }
                },
                billTo = new
                {
                    firstName,
                    lastName,
                    address1 = Converter.ToString(order.CustomerAddress),
                    locality = Converter.ToString(order.CustomerCity),
                    administrativeArea = Converter.ToString(order.CustomerRegion),
                    postalCode = Converter.ToString(order.CustomerZip),
                    country = Converter.ToString(order.CustomerCountryCode),
                    email = Converter.ToString(order.CustomerEmail),
                    phoneNumber = Converter.ToString(order.CustomerPhone)
                }
            };

            var requestJson = Converter.Serialize(request);
            return requestJson;
        }
        #endregion


        #region IRecurring

        public void Recurring(Order order, Order initialOrder)
        {
            if (order != null)
            {
                try
                {
                    UseSavedCardInternal(order);
                    LogEvent(order, "Recurring succeeded");
                }
                catch (System.Threading.ThreadAbortException)
                {
                }
                catch (Exception ex)
                {
                    LogEvent(order, "Recurring order failed for {0} (based on {1}). The payment failed with the message: {2}",
                        DebuggingInfoType.RecurringError, order.Id, initialOrder.Id, ex.Message);
                }
            }
        }

        public bool RecurringSupported(Order order)
        {
            return true;
        }

        #endregion

        #region ICheckAuthorizationStatus

        public AuthorizationStatus CheckAuthorizationStatus(Order order)
        {
            if (order.TransactionStatus == "Succeeded")
            {
                return order.TransactionType == "Zero authorization" && order.CaptureInfo.State == OrderCaptureInfo.OrderCaptureState.Success ?
                    AuthorizationStatus.AuthorizedZeroAmount : AuthorizationStatus.AuthorizedFullAmount;
            }
            else
            {
                return AuthorizationStatus.NotAuthorized;
            }
        }

        #endregion

    }
}
