using System.Text.Json;
using System.Text.Json.Serialization;
using RecordKeeper.Core.Data;

namespace RecordKeeper.Core.Models;

public enum AppTheme
{
    /// <summary>Follow whatever Windows is set to, including high contrast.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
    HighContrast = 3
}

public enum RecordViewMode
{
    /// <summary>A table of records. Fast to scan with sight.</summary>
    Table = 0,

    /// <summary>One record at a time with its fields stacked. Far easier with a screen reader.</summary>
    Form = 1
}

/// <summary>
/// User preferences, stored as readable JSON next to the database so they can be
/// inspected or hand-edited if anything ever goes wrong.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Base font size in points. The whole window scales from this.</summary>
    public double FontSize { get; set; } = 15;

    public AppTheme Theme { get; set; } = AppTheme.Light;

    /// <summary>
    /// False until the user saves the Settings window at least once.
    ///
    /// While it is false the app picks the appearance itself — the white-and-blue light
    /// theme, and a focus-outline colour that actually stands out against that theme.
    /// Once the user has made a choice, their choice is used and never second-guessed.
    /// Windows high contrast overrides both cases.
    /// </summary>
    public bool AppearanceChosenByUser { get; set; }

    /// <summary>Which view the record list opens in.</summary>
    public RecordViewMode DefaultViewMode { get; set; } = RecordViewMode.Form;

    /// <summary>Thickness in pixels of the outline drawn around the focused control.</summary>
    public double FocusOutlineThickness { get; set; } = 3;

    /// <summary>Focus outline colour as a hex string, so it can be tuned for low vision.</summary>
    public string FocusOutlineColor { get; set; } = "#FFD400";

    /// <summary>Speak confirmations through the app's own voice, independently of Narrator.</summary>
    public bool SpeakConfirmations { get; set; }

    /// <summary>Play a short distinct sound on save, on error and on delete.</summary>
    public bool PlaySoundCues { get; set; } = true;

    /// <summary>
    /// Show the acvaysoft startup screen. On by default, but it is skipped anyway when
    /// a screen reader is running, and shown without motion when Windows has animations
    /// turned off.
    /// </summary>
    public bool ShowStartupLogo { get; set; } = true;

    /// <summary>Write a backup zip once per day on first launch.</summary>
    public bool AutoBackupDaily { get; set; } = true;

    public DateTime? LastAutoBackupUtc { get; set; }

    /// <summary>How many backup zips to keep in the auto folder before the oldest is removed.</summary>
    public int AutoBackupsToKeep { get; set; } = 14;

    /// <summary>Warn when saving a record whose title matches an existing one.</summary>
    public bool WarnOnDuplicateTitle { get; set; } = true;

    /// <summary>
    /// Whether the sorting and period controls are on show above the record list.
    ///
    /// Off to begin with: they are controls rather than content, and four lists across
    /// the top of every category is a lot to tab past for someone who sets an order
    /// once and leaves it. The button that reveals them remembers what was chosen.
    /// </summary>
    public bool ShowSortControls { get; set; }

    /// <summary>
    /// Whether the totals panel is on show. On to begin with, because a total is an
    /// answer rather than a control, and it is the reason the amounts are kept at all.
    /// </summary>
    public bool ShowTotalsPanel { get; set; } = true;

    /// <summary>
    /// What money is written with, for example "Rs". Rupees unless the user changes it.
    ///
    /// This is not taken from the machine's regional settings: the records are in one
    /// currency whatever Windows is set to, and amounts that changed symbol after a
    /// reinstall would be quietly wrong. <see cref="Money.SystemSymbol"/> opts into
    /// the Windows setting deliberately.
    /// </summary>
    public string CurrencySymbol { get; set; } = Money.DefaultSymbol;

    /// <summary>The word read aloud after an amount, such as "rupees".</summary>
    public string CurrencyName { get; set; } = Money.DefaultSpokenName;

    /// <summary>
    /// Whether the database file is encrypted and needs a password to open.
    ///
    /// Off by default: encryption adds a password prompt to every launch and a
    /// forgotten password means the records are gone for good, so it is something the
    /// user turns on deliberately rather than something they inherit.
    ///
    /// Only the flag is stored. The password itself is never written anywhere — if it
    /// were, it would not be protecting anything.
    /// </summary>
    public bool EncryptionEnabled { get; set; }

    /// <summary>Remembered window geometry, so the app reopens where it was left.</summary>
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 820;
    public bool WindowMaximized { get; set; }

    public long? LastRecordTypeId { get; set; }

    /// <summary>True until the user has been through first-run setup.</summary>
    public bool NeedsFirstRun { get; set; } = true;

    [JsonIgnore]
    public double FontSizeMinimum => 11;

    [JsonIgnore]
    public double FontSizeMaximum => 48;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings is not null) return Sanitize(settings);
            }
        }
        catch (JsonException) { /* a corrupt file should not stop the app starting */ }
        catch (IOException) { }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(Sanitize(settings), Options);

        // Write to a temporary file first so an interrupted save cannot leave
        // the user with a half-written settings file.
        var temporary = AppPaths.SettingsFile + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, AppPaths.SettingsFile, overwrite: true);
    }

    private static AppSettings Sanitize(AppSettings settings)
    {
        settings.FontSize = Math.Clamp(settings.FontSize, settings.FontSizeMinimum, settings.FontSizeMaximum);
        settings.FocusOutlineThickness = Math.Clamp(settings.FocusOutlineThickness, 1, 8);
        settings.AutoBackupsToKeep = Math.Clamp(settings.AutoBackupsToKeep, 1, 365);
        if (settings.WindowWidth < 800) settings.WindowWidth = 800;
        if (settings.WindowHeight < 600) settings.WindowHeight = 600;
        if (string.IsNullOrWhiteSpace(settings.FocusOutlineColor)) settings.FocusOutlineColor = "#FFD400";
        return settings;
    }
}
