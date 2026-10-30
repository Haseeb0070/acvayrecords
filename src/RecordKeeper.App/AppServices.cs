using RecordKeeper.App.Accessibility;
using RecordKeeper.Core.Data;
using RecordKeeper.Core.Models;
using RecordKeeper.Core.Services;

namespace RecordKeeper.App;

/// <summary>
/// Everything the application needs, built once at startup. The app is small enough
/// that a hand-wired container is clearer than a dependency-injection framework.
/// </summary>
public sealed class AppServices
{
    public static AppServices Current { get; private set; } = null!;

    public Database Database { get; }
    public RecordTypeRepository RecordTypes { get; }
    public CategoryGroupRepository Groups { get; }
    public RecordRepository Records { get; }
    public AttachmentRepository Attachments { get; }
    public ExportService Export { get; }
    public PdfExportService Pdf { get; }
    public BackupService Backup { get; }
    public Announcer Announcer { get; }

    public AppSettings Settings { get; private set; }

    /// <summary>What the starter-type setup did on this launch, for announcing once.</summary>
    public SeedOutcome SeedOutcome { get; private set; } = new();

    /// <summary>
    /// The password the database was opened with, kept for the life of the session so
    /// Settings can convert the file without asking for it again. It is never written
    /// to disk.
    /// </summary>
    public string? DatabasePassword { get; private set; }

    private AppServices(string? password)
    {
        AppPaths.EnsureCreated();

        Settings = SettingsStore.Load();
        Money.Use(Settings.CurrencySymbol, Settings.CurrencyName);

        DatabasePassword = password;
        Database = new Database(null, password);
        Database.Initialize();

        RecordTypes = new RecordTypeRepository(Database);
        Groups = new CategoryGroupRepository(Database);
        Records = new RecordRepository(Database);
        Attachments = new AttachmentRepository(Database);
        Export = new ExportService(Attachments);
        // The logo is rendered from the app's own vector resource, on demand.
        Pdf = new PdfExportService(Attachments, () => Branding.LogoImage.GetPng());
        Backup = new BackupService(Database);
        Announcer = new Announcer(Settings);

        SeedOutcome = SeedData.EnsureStarterTypes(Database, RecordTypes);
    }

    public static AppServices Initialize(string? password = null) =>
        Current = new AppServices(password);

    /// <summary>Persists the settings and pushes the change through to the live UI.</summary>
    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        Money.Use(settings.CurrencySymbol, settings.CurrencyName);
        SettingsStore.Save(settings);
        Announcer.UpdateSettings(settings);
        ThemeManager.Apply(settings);
    }

    public void SaveSettings() => SaveSettings(Settings);

    /// <summary>
    /// Writes the settings file without reapplying the theme or the speech settings.
    ///
    /// For the small things the window remembers, such as whether a panel is on show.
    /// Rebuilding the theme on a button press is work nobody asked for, and anything
    /// it disturbs — focus, a live announcement — is disturbed for no reason.
    /// </summary>
    public void PersistSettings() => SettingsStore.Save(Settings);
}
