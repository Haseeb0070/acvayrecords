using System.Globalization;

namespace RecordKeeper.Core.Models;

/// <summary>One choice of currency, as it is offered in Settings.</summary>
public sealed record CurrencyPreset(string Label, string Symbol, string SpokenName)
{
    // A record's own ToString prints every member, and the combo box falls back to
    // ToString for the item it is showing as selected. Without this the setting reads
    // as "CurrencyPreset { Label = ..., Symbol = ... }".
    public override string ToString() => Label;
}

/// <summary>
/// How money is written and read aloud.
///
/// The machine's regional settings are deliberately not used for this. The records
/// kept here are kept in rupees whatever Windows happens to be set to, and a total
/// that silently changes currency because the machine was reinstalled would be worse
/// than wrong — it would be wrong and quiet about it. The symbol is a setting, and
/// rupees is what it starts as.
/// </summary>
public static class Money
{
    public const string DefaultSymbol = "Rs";
    public const string DefaultSpokenName = "rupees";

    /// <summary>The symbol that means "use whatever Windows is set to".</summary>
    public const string SystemSymbol = "system";

    public static IReadOnlyList<CurrencyPreset> Presets { get; } = new[]
    {
        new CurrencyPreset("Rupees, written Rs", "Rs", "rupees"),
        new CurrencyPreset("Rupees, written ₨", "₨", "rupees"),
        new CurrencyPreset("Rupees, written ₹", "₹", "rupees"),
        new CurrencyPreset("Dollars, written $", "$", "dollars"),
        new CurrencyPreset("Pounds, written £", "£", "pounds"),
        new CurrencyPreset("Euros, written €", "€", "euros"),
        new CurrencyPreset("Dirhams, written AED", "AED", "dirhams"),
        new CurrencyPreset("Whatever Windows is set to", SystemSymbol, string.Empty)
    };

    private static NumberFormatInfo _format = Build(DefaultSymbol);

    public static string Symbol { get; private set; } = DefaultSymbol;

    /// <summary>The word a screen reader should say, such as "rupees". May be empty.</summary>
    public static string SpokenName { get; private set; } = DefaultSpokenName;

    /// <summary>Switches the currency for the rest of the session.</summary>
    public static void Use(string? symbol, string? spokenName)
    {
        var wanted = string.IsNullOrWhiteSpace(symbol) ? DefaultSymbol : symbol.Trim();

        if (wanted == SystemSymbol)
        {
            Symbol = CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol;
            SpokenName = string.Empty;
            _format = (NumberFormatInfo)CultureInfo.CurrentCulture.NumberFormat.Clone();
            return;
        }

        Symbol = wanted;
        SpokenName = (spokenName ?? string.Empty).Trim();
        _format = Build(wanted);
    }

    /// <summary>An amount as it should appear on screen, for example "Rs 1,250.00".</summary>
    public static string Text(decimal amount) => amount.ToString("C2", _format);

    /// <summary>
    /// An amount as it should be read aloud, for example "1,250.00 rupees".
    ///
    /// Narrator spells "Rs" out letter by letter, so the word goes after the number
    /// instead wherever the app is speaking rather than printing.
    /// </summary>
    public static string Spoken(decimal amount) =>
        SpokenName.Length == 0
            ? Text(amount)
            : amount.ToString("N2", _format) + " " + SpokenName;

    /// <summary>The preset matching a stored symbol, falling back to rupees.</summary>
    public static CurrencyPreset PresetFor(string? symbol) =>
        Presets.FirstOrDefault(p => p.Symbol == (symbol ?? string.Empty)) ?? Presets[0];

    private static NumberFormatInfo Build(string symbol)
    {
        // Invariant rather than the machine's culture: the digits, the decimal point
        // and the grouping stay the same wherever the app is run.
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();

        format.CurrencySymbol = symbol;
        format.CurrencyDecimalDigits = 2;

        // "Rs 1,250.00" and "-Rs 1,250.00": the symbol leads, with a space after it,
        // which is how an amount is written by hand.
        format.CurrencyPositivePattern = 2;
        format.CurrencyNegativePattern = 9;

        return format;
    }
}
