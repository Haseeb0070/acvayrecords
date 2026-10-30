namespace RecordKeeper.Core.Data;

/// <summary>
/// Resolves every location the application writes to. Everything lives under one
/// folder so that backing the app up, or moving it to another machine, is a single copy.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// The folder the application's own data lives in.
    ///
    /// Deliberately left as it was when the application was called Record Keeper.
    /// The one file at this fixed location is what points at wherever the records
    /// really are, so renaming it would strand every existing installation: the
    /// application would look in a new, empty folder and quietly start again.
    /// </summary>
    public const string AppFolderName = "RecordKeeper";

    private static string? _root;

    /// <summary>The folder used unless the user has redirected the data elsewhere.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    /// <summary>
    /// A one-line text file in the default location holding the path of the real data folder.
    /// This indirection lets the user keep records in Documents, or on a USB drive,
    /// without the app needing a registry entry.
    /// </summary>
    private static string RedirectFile => Path.Combine(DefaultRoot, "datapath.txt");

    public static string Root
    {
        get
        {
            if (_root is not null) return _root;

            var root = DefaultRoot;
            try
            {
                if (File.Exists(RedirectFile))
                {
                    var target = File.ReadAllText(RedirectFile).Trim();
                    if (target.Length > 0 && Path.IsPathFullyQualified(target))
                        root = target;
                }
            }
            catch (IOException) { /* fall back to the default folder */ }
            catch (UnauthorizedAccessException) { /* fall back to the default folder */ }

            _root = root;
            return _root;
        }
    }

    public static string DatabaseFile => Path.Combine(Root, "records.db");
    public static string AttachmentsDir => Path.Combine(Root, "attachments");
    public static string BackupsDir => Path.Combine(Root, "backups");
    public static string ExportsDir => Path.Combine(Root, "exports");
    public static string LogsDir => Path.Combine(Root, "logs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>Creates the folder layout. Safe to call on every start.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AttachmentsDir);
        Directory.CreateDirectory(Path.Combine(BackupsDir, "auto"));
        Directory.CreateDirectory(Path.Combine(BackupsDir, "manual"));
        Directory.CreateDirectory(ExportsDir);
        Directory.CreateDirectory(LogsDir);
    }

    /// <summary>
    /// Points this process at a throwaway data folder, for the test harness only.
    ///
    /// It exists so the checks can run against a scratch database instead of the
    /// user's own records. Nothing in the application calls it, and it deliberately
    /// does not write the redirect file, so it cannot affect the next real launch.
    /// </summary>
    public static void UseRootForTesting(string root)
    {
        _root = root;
        EnsureCreated();
    }

    /// <summary>Points future launches at a different data folder.</summary>
    public static void SetRedirect(string newRoot)
    {
        Directory.CreateDirectory(DefaultRoot);
        if (string.Equals(Path.TrimEndingDirectorySeparator(newRoot),
                          Path.TrimEndingDirectorySeparator(DefaultRoot),
                          StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(RedirectFile)) File.Delete(RedirectFile);
        }
        else
        {
            File.WriteAllText(RedirectFile, newRoot);
        }
    }

    public static string Absolute(string relativePath) => Path.Combine(Root, relativePath);
}
