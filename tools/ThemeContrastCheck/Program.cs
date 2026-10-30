using System.Globalization;
using System.Text.RegularExpressions;

// Checks that every colour pair the UI actually puts together is readable, to the
// WCAG 2.1 contrast standard: 4.5 to 1 for normal text, 3 to 1 for a boundary or
// other non-text marking.
//
// This exists because the themes are the one part of an accessibility-led app that
// is easy to break by eye. Someone nudging a blue "just a bit brighter" can drop it
// under the threshold without anything looking obviously wrong on their monitor, and
// the person who suffers for it is the one who cannot see it at all.
//
// The high-contrast theme is not checked: every colour in it comes from the Windows
// system palette, so the contrast is whatever the user's own Windows scheme sets.

const double TextMinimum = 4.5;
const double NonTextMinimum = 3.0;

var themesDirectory = args.Length > 0 ? args[0] : FindThemesDirectory();

if (themesDirectory is null)
{
    Console.Error.WriteLine(
        "Could not find the Themes folder. Pass it as an argument, for example:\n" +
        "  dotnet run --project tools/ThemeContrastCheck -- src/RecordKeeper.App/Themes");
    return 2;
}

Console.WriteLine("Themes folder: " + themesDirectory);

// Pairs are (foreground key, background key, label). A "fill" pair is non-text.
var textPairs = new[]
{
    ("App.Foreground",              "App.Background",              "body text on the page"),
    ("App.Foreground",              "App.ControlBackground",       "typed value in a field"),
    ("App.PanelForeground",         "App.PanelBackground",         "text on a panel"),
    ("App.Foreground",              "App.PanelBackground",         "body text on a panel"),
    ("App.SubtleForeground",        "App.Background",              "help text on the page"),
    ("App.SubtleForeground",        "App.PanelBackground",         "help text on a panel"),
    ("App.InfoForeground",          "App.Background",              "field label on the page"),
    ("App.InfoForeground",          "App.PanelBackground",         "field label on a panel"),
    ("App.HeadingForeground",       "App.Background",              "heading on the page"),
    ("App.HeadingForeground",       "App.PanelBackground",         "group header, column header"),
    ("App.ErrorForeground",         "App.Background",              "error message"),
    ("App.ErrorForeground",         "App.PanelBackground",         "error message on a panel"),
    ("App.RequiredForeground",      "App.Background",              "the word required"),
    ("App.SuccessForeground",       "App.Background",              "success message"),
    ("App.SelectionForeground",     "App.SelectionBackground",     "text on a selection fill"),
    ("App.RowSelectionForeground",  "App.RowSelectionBackground",  "text in a selected list row"),
    ("App.InfoForeground",          "App.RowSelectionBackground",  "secondary text in a selected row"),
    ("App.HeadingForeground",       "App.RowSelectionBackground",  "accent text in a selected row"),
    ("App.AccentForeground",        "App.AccentBackground",        "text on an accent fill"),
    ("App.PrimaryButtonForeground", "App.PrimaryButtonBackground", "text on the main button"),
    ("App.ControlForeground",       "App.ControlBackground",       "text in a control")
};

var nonTextPairs = new[]
{
    ("App.Border",              "App.Background",          "control outline on the page"),
    ("App.Border",              "App.PanelBackground",     "control outline on a panel"),
    ("App.ScrollThumb",         "App.ScrollTrack",         "scroll bar thumb"),
    ("App.RowMarker",           "App.RowSelectionBackground", "selected-row marker bar"),
    ("App.PrimaryButtonBorder", "App.Background",          "main button outline on the page"),
    ("App.PrimaryButtonBorder", "App.PanelBackground",     "main button outline on a panel")
};

// Reported for information, not enforced.
//
// The brand cyan is deliberately allowed to be a light fill that would fail on its
// own. It never has to be told apart from the page by itself: as the main button's
// fill it sits inside App.PrimaryButtonBorder, and as the selected-row marker it sits
// on App.SelectionBackground. Both of those boundaries are enforced above.
var advisoryPairs = new[]
{
    ("App.AccentBackground", "App.Background", "brand fill against the page (fill only, see note)")
};

var failures = 0;
var checks = 0;

foreach (var file in new[] { "Light.xaml", "Dark.xaml" })
{
    var path = Path.Combine(themesDirectory, file);
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"FAIL  {file} is missing");
        failures++;
        continue;
    }

    var colors = ReadBrushes(path);
    Console.WriteLine($"\n=== {file} ({colors.Count} colours) ===");

    foreach (var (foreground, background, label) in textPairs)
        Check(file, colors, foreground, background, label, TextMinimum, ref failures, ref checks);

    foreach (var (foreground, background, label) in nonTextPairs)
        Check(file, colors, foreground, background, label, NonTextMinimum, ref failures, ref checks);

    foreach (var (foreground, background, label) in advisoryPairs)
    {
        if (!colors.TryGetValue(foreground, out var a) || !colors.TryGetValue(background, out var b)) continue;
        Console.WriteLine($"note  {ContrastRatio(a, b),5:0.00}:1  (not enforced)        {label}");
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? $"ALL {checks} CONTRAST CHECKS PASSED"
    : $"{failures} CONTRAST CHECK(S) FAILED out of {checks}");

return failures == 0 ? 0 : 1;


static void Check(string file, Dictionary<string, (byte R, byte G, byte B)> colors,
                  string foregroundKey, string backgroundKey, string label,
                  double minimum, ref int failures, ref int checks)
{
    if (!colors.TryGetValue(foregroundKey, out var foreground))
    {
        Console.Error.WriteLine($"FAIL  {file}: {foregroundKey} is not defined");
        failures++;
        return;
    }

    if (!colors.TryGetValue(backgroundKey, out var background))
    {
        Console.Error.WriteLine($"FAIL  {file}: {backgroundKey} is not defined");
        failures++;
        return;
    }

    checks++;

    var ratio = ContrastRatio(foreground, background);
    var ok = ratio >= minimum;
    if (!ok) failures++;

    var grade = ratio >= 7 ? "AAA" : ratio >= 4.5 ? "AA " : ratio >= 3 ? "AA-large" : "fail";

    Console.WriteLine(
        $"{(ok ? "PASS" : "FAIL")}  {ratio,5:0.00}:1  (needs {minimum:0.0})  {grade,-8}  {label}" +
        $"  [{foregroundKey} on {backgroundKey}]");
}

/// Reads every SolidColorBrush with an x:Key and a literal hex Color out of a theme file.
static Dictionary<string, (byte R, byte G, byte B)> ReadBrushes(string path)
{
    var result = new Dictionary<string, (byte, byte, byte)>(StringComparer.Ordinal);

    var pattern = new Regex(
        """<SolidColorBrush\s+x:Key="(?<key>[^"]+)"\s+Color="(?<color>#[0-9A-Fa-f]{6})"\s*/>""",
        RegexOptions.Compiled);

    foreach (Match match in pattern.Matches(File.ReadAllText(path)))
    {
        var hex = match.Groups["color"].Value[1..];
        result[match.Groups["key"].Value] = (
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    return result;
}

static double ContrastRatio((byte R, byte G, byte B) first, (byte R, byte G, byte B) second)
{
    var a = RelativeLuminance(first);
    var b = RelativeLuminance(second);

    var lighter = Math.Max(a, b);
    var darker = Math.Min(a, b);

    return (lighter + 0.05) / (darker + 0.05);
}

/// WCAG 2.1 relative luminance.
static double RelativeLuminance((byte R, byte G, byte B) color) =>
    0.2126 * Linearize(color.R) +
    0.7152 * Linearize(color.G) +
    0.0722 * Linearize(color.B);

static double Linearize(byte channel)
{
    var value = channel / 255.0;
    return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
}

/// Walks up from the working directory looking for the themes folder, so the tool can
/// be run from anywhere in the repository without being told where anything is.
static string? FindThemesDirectory()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, "src", "RecordKeeper.App", "Themes");
        if (Directory.Exists(candidate)) return candidate;
        directory = directory.Parent;
    }

    return null;
}
