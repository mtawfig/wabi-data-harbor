using System.Text.RegularExpressions;
using WabiInsight.Core.Entities.Metadata;

namespace WabiInsight.Core.Monitoring;

/// <summary>
/// Suggests a semantic role for a catalogued column from its name, type and key status (Phase 14, specification
/// §35.1). A hint for the Organization Administrator and for rule templates; never a permission and never applied
/// without a human choosing it. Patterns are generic (accounts, devices, people, time, money, places, contacts).
/// </summary>
public static partial class ColumnRoleSuggester
{
    public static ColumnSemanticRole Suggest(string columnName, string dataType, bool isPrimaryKey)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        var name = Normalize(columnName);
        var type = (dataType ?? string.Empty).ToLowerInvariant();
        var isTemporalType = type.Contains("date", StringComparison.Ordinal) || type.Contains("time", StringComparison.Ordinal);
        var isNumericType = type is "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" or "number";
        var isTextType = type.Contains("char", StringComparison.Ordinal) || type.Contains("text", StringComparison.Ordinal);

        // A date/time column is a timestamp whatever its name says ("LoginAt", "AccountOpenedOn").
        if (isTemporalType)
        {
            return ColumnSemanticRole.Timestamp;
        }

        // Devices before contacts: "ip address" and "mac address" are devices, not postal addresses.
        if (DevicePattern().IsMatch(name))
        {
            return ColumnSemanticRole.DeviceIdentifier;
        }

        if (ContactPattern().IsMatch(name))
        {
            return ColumnSemanticRole.Contact;
        }

        if (AccountPattern().IsMatch(name))
        {
            return ColumnSemanticRole.AccountIdentifier;
        }

        if (PersonPattern().IsMatch(name))
        {
            return ColumnSemanticRole.PersonIdentifier;
        }

        if (TimestampPattern().IsMatch(name))
        {
            return ColumnSemanticRole.Timestamp;
        }

        if (LocationPattern().IsMatch(name))
        {
            return ColumnSemanticRole.Location;
        }

        if (StatusPattern().IsMatch(name) && !isNumericType)
        {
            return ColumnSemanticRole.Status;
        }

        if (isNumericType && AmountPattern().IsMatch(name))
        {
            return ColumnSemanticRole.Amount;
        }

        if (isNumericType && QuantityPattern().IsMatch(name))
        {
            return ColumnSemanticRole.Quantity;
        }

        if (isPrimaryKey || IdentifierPattern().IsMatch(name))
        {
            return ColumnSemanticRole.Identifier;
        }

        if (isTextType && StatusPattern().IsMatch(name))
        {
            return ColumnSemanticRole.Status;
        }

        return ColumnSemanticRole.Other;
    }

    /// <summary>Lower-case with word separators between camel-case parts and digits removed, so "AccountNo", "account_no" and "ACCOUNTNO" compare alike.</summary>
    public static string Normalize(string columnName)
    {
        var spaced = CamelBoundary().Replace(columnName, "$1 $2");
        return NonLetters().Replace(spaced, " ").Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"([a-z])([A-Z])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex(@"[^A-Za-z]+")]
    private static partial Regex NonLetters();

    [GeneratedRegex(@"\b(e ?mail|phone|mobile|tel|telephone|fax|address|street|zip|postal)\b")]
    private static partial Regex ContactPattern();

    [GeneratedRegex(@"\b(device|imei|imsi|mac|user ?agent|terminal|pos|atm|workstation|hostname|host ?name|ip|ip ?address|browser|fingerprint)\b")]
    private static partial Regex DevicePattern();

    [GeneratedRegex(@"\b(account|acct|iban|card|wallet|subscription|contract|policy|membership|login|username|user ?name)\b")]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"\b(employee|customer|client|patient|person|citizen|passenger|national ?id|emirates ?id|passport|ssn|user ?id|member ?id|tenant ?id|supplier|vendor|owner|holder|beneficiary|driver|student)\b")]
    private static partial Regex PersonPattern();

    [GeneratedRegex(@"\b(date|time|timestamp|at|on|when|created|updated|modified|login|logged|expires|expiry|start|end|period|month|year|day)\b")]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"\b(city|country|region|location|branch|site|airport|station|terminal ?code|lat|latitude|lng|longitude|geo|zone|area|office|warehouse|gate)\b")]
    private static partial Regex LocationPattern();

    [GeneratedRegex(@"\b(status|state|stage|result|outcome|flag|is|has|active|enabled|type|category|kind|priority|severity|direction)\b")]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"\b(amount|amt|salary|wage|pay|price|cost|total|net|gross|fee|balance|tax|vat|discount|revenue|sales|value|budget|allowance|deduction|rate|charge|payment|income|expense)\b")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"\b(qty|quantity|count|number of|seats|passengers|units|pieces|minutes|hours|days|percent|pct|factor|score|rating|kg|km|weight|distance|stock|level)\b")]
    private static partial Regex QuantityPattern();

    [GeneratedRegex(@"\b(id|ids|no|num|number|code|key|ref|reference|uuid|guid|sequence|seq)\b")]
    private static partial Regex IdentifierPattern();
}
