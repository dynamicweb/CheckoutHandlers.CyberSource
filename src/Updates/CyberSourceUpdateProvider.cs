using Dynamicweb.Updates;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Updates;

public sealed class CyberSourceUpdateProvider : UpdateProvider
{
    private static Stream GetResourceStream(string name)
    {
        string resourceName = $"Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Updates.{name}";

        return Assembly.GetAssembly(typeof(CyberSourceUpdateProvider)).GetManifestResourceStream(resourceName);
    }

    public override IEnumerable<Update> GetUpdates()
    {
        return new List<Update>()
        {
            new FileUpdate("7c7b5833-f67f-4eb7-88bb-a7460e538f4f", this, "/Files/Templates/eCom7/CheckoutHandler/CyberSource/Cancel/checkouthandler_cancel.html", () => GetResourceStream("checkouthandler_cancel.html")),
            new FileUpdate("1d7c25a8-cf3d-4f9a-a85e-c82e3219cba5", this, "/Files/Templates/eCom7/CheckoutHandler/CyberSource/Error/checkouthandler_error.html", () => GetResourceStream("checkouthandler_error.html")),
            new FileUpdate("541895f6-0e7f-4ad4-9bd7-333aa86dfa07", this, "/Files/Templates/eCom7/CheckoutHandler/CyberSource/Payment/Payment.html", () => GetResourceStream("Payment.html"))
        };
    }

    /*
     * IMPORTANT!
     * Use a generated GUID string as id for an update
     * - Execute command in C# interactive window: Guid.NewGuid().ToString()
     */
}