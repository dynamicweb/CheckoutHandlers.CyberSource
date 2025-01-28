using Dynamicweb.Core.Helpers;
using Dynamicweb.Ecommerce.Orders;
using System;
using System.IO;

namespace Dynamicweb.Ecommerce.CheckoutHandlers.CyberSource.Helpers;

internal static class Helper
{
    public static string GetCertificateFilePath(string certificateFile)
    {
        if (string.IsNullOrWhiteSpace(certificateFile))
            return string.Empty;

        string path = FilePathHelper.GetAbsolutePath(certificateFile);
        if (File.Exists(path))
            return path;

        return string.Empty;
    }

    public static string GetCustomerLastName(Order order, string customerName)
    {
        string lastName = order.CustomerSurname;
        int delimeterPosition = customerName.IndexOf(' ');
        if (string.IsNullOrWhiteSpace(lastName))
            lastName = delimeterPosition > -1 ? customerName.Substring(delimeterPosition + 1) : customerName;

        return lastName;
    }

    public static string GetCustomerFirstName(Order order, string customerName)
    {
        string firstName = order.CustomerFirstName;
        int delimeterPosition = customerName.IndexOf(' ');
        if (string.IsNullOrWhiteSpace(firstName))
            firstName = delimeterPosition > -1 ? customerName.Substring(0, delimeterPosition) : customerName;

        return firstName;
    }

    public static string GetTransactionAmount(Order order)
    {
        int decimals = order.Currency.Rounding is null || string.IsNullOrEmpty(order.Currency.Rounding.Id)
            ? 2
            : order.Currency.Rounding.Decimals;

        string amount = Math.Round(order.Price.Price, decimals).ToString("0.00", System.Globalization.CultureInfo.CreateSpecificCulture("en-US"));

        return amount;
    }
}
