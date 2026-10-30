using System.Globalization;

namespace RecordKeeper.Core.Models;

/// <summary>
/// Translates between what the user types, the canonical form stored in the database,
/// and the wording shown on screen and read aloud.
///
/// Values are stored culture-independently so the database stays readable and sortable
/// no matter which regional settings the machine uses.
/// </summary>
public static class FieldValueFormat
{
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Validates and normalises what the user typed.
    /// Returns false with a spoken-friendly explanation when the entry cannot be read.
    /// </summary>
    public static bool TryCanonicalize(FieldDataType type, string? input, out string canonical, out string error)
    {
        error = string.Empty;
        var raw = (input ?? string.Empty).Trim();

        if (raw.Length == 0)
        {
            canonical = string.Empty;
            return true;
        }

        switch (type)
        {
            case FieldDataType.Text:
            case FieldDataType.Choice:
                canonical = raw;
                return true;

            case FieldDataType.LongText:
                canonical = (input ?? string.Empty).TrimEnd();
                return true;

            case FieldDataType.Number:
                if (TryParseDouble(raw, out var number))
                {
                    canonical = number.ToString("0.##########", CultureInfo.InvariantCulture);
                    return true;
                }
                canonical = string.Empty;
                error = "That is not a number. Type digits only, for example 12 or 3.5.";
                return false;

            case FieldDataType.Money:
                var cleaned = StripCurrencySymbols(raw);
                if (TryParseDecimal(cleaned, out var amount))
                {
                    canonical = amount.ToString("0.00", CultureInfo.InvariantCulture);
                    return true;
                }
                canonical = string.Empty;
                error = "That is not an amount. Type digits only, for example 1250.00.";
                return false;

            case FieldDataType.Date:
                // Accepting the words people actually say saves a screen-reader user
                // from composing a date out of digits and separators.
                if (TryParseRelativeDay(raw, out var relative))
                {
                    canonical = relative.ToString(DateFormat, CultureInfo.InvariantCulture);
                    return true;
                }

                if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date) ||
                    DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    canonical = date.ToString(DateFormat, CultureInfo.InvariantCulture);
                    return true;
                }
                canonical = string.Empty;
                error = "That is not a date. Type it as day slash month slash year, " +
                        "or type the word today, tomorrow or yesterday.";
                return false;

            case FieldDataType.Boolean:
                var yes = raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                          raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                          raw == "1";
                canonical = yes ? "true" : "false";
                return true;

            default:
                canonical = raw;
                return true;
        }
    }

    /// <summary>The stored value as it should appear on screen.</summary>
    public static string ToDisplay(FieldDataType type, string? canonical)
    {
        var value = canonical ?? string.Empty;
        if (value.Length == 0) return string.Empty;

        switch (type)
        {
            case FieldDataType.Number:
                return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString("0.##########", CultureInfo.CurrentCulture)
                    : value;

            case FieldDataType.Money:
                return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)
                    ? Money.Text(amount)
                    : value;

            case FieldDataType.Date:
                return DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date)
                    ? date.ToString("d", CultureInfo.CurrentCulture)
                    : value;

            case FieldDataType.Boolean:
                return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Yes" : "No";

            default:
                return value;
        }
    }

    /// <summary>
    /// The stored value worded for the screen reader. Dates are spelled out in full
    /// because "03/04/2026" is ambiguous when read as digits.
    /// </summary>
    public static string ToSpoken(FieldDataType type, string? canonical)
    {
        var value = canonical ?? string.Empty;
        if (value.Length == 0) return "empty";

        if (type == FieldDataType.Date &&
            DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return date.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
        }

        // "Rs" is spelled out letter by letter by a screen reader, so an amount is
        // read as "1,250.00 rupees" instead.
        if (type == FieldDataType.Money &&
            decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
        {
            return Money.Spoken(amount);
        }

        return ToDisplay(type, value);
    }

    /// <summary>Numeric mirror of the value, used so the table view sorts numbers properly.</summary>
    public static double? ToSortNumber(FieldDataType type, string? canonical)
    {
        if (string.IsNullOrEmpty(canonical)) return null;

        if (FieldDataTypeInfo.IsNumeric(type) &&
            double.TryParse(canonical, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        if (type == FieldDataType.Boolean)
            return canonical.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        return null;
    }

    /// <summary>Turns a field default into the value a brand-new record starts with.</summary>
    public static string ResolveDefault(FieldDef field)
    {
        var raw = field.DefaultValue ?? string.Empty;

        // "today" in a date field is the common case and worth special-casing.
        if (field.DataType == FieldDataType.Date &&
            raw.Trim().Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            return DateTime.Today.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        return TryCanonicalize(field.DataType, raw, out var canonical, out _) ? canonical : string.Empty;
    }

    /// <summary>Understands "today", "tomorrow" and "yesterday" in a date field.</summary>
    private static bool TryParseRelativeDay(string text, out DateTime value)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "today":     value = DateTime.Today; return true;
            case "tomorrow":  value = DateTime.Today.AddDays(1); return true;
            case "yesterday": value = DateTime.Today.AddDays(-1); return true;
            default:          value = default; return false;
        }
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static string StripCurrencySymbols(string text)
    {
        // Whatever the user typed in front of the number, the number is what matters.
        foreach (var symbol in new[]
                 {
                     Money.Symbol,
                     CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol,
                     "Rs.", "Rs", "rs.", "rs", "PKR", "INR", "AED",
                     "$", "€", "£", "₹", "₨"
                 })
        {
            if (symbol.Length > 0)
                text = text.Replace(symbol, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return text.Trim();
    }
}
