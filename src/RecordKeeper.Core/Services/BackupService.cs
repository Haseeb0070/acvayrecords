using RecordKeeper.Core.Data;
using RecordKeeper.Core.Models;

namespace RecordKeeper.Core.Services;

/// <summary>Creates backup zips and keeps the automatic folder from growing without limit.</summary>
public sealed class BackupService
{
    private readonly Database _database;

    public BackupService(Database database) => _database = database;

    /// <summary>Makes a backup the user asked for, under backups\manual.</summary>
    public string CreateManualBackup() => _database.CreateBackup(automatic: false);

    /// <summary>
    /// Makes the once-a-day automatic backup if it is due. Returns the path written,
    /// or null when today's backup already exists or the feature is switched off.
    /// </summary>
    public string? RunDailyBackupIfDue(AppSettings settings)
    {
        if (!settings.AutoBackupDaily) return null;

        var last = settings.LastAutoBackupUtc;
        if (last is not null && (DateTime.UtcNow - last.Value) < TimeSpan.FromHours(20)) return null;

        var path = _database.CreateBackup(automatic: true);
        settings.LastAutoBackupUtc = DateTime.UtcNow;
        Prune(settings.AutoBackupsToKeep);
        return path;
    }

    /// <summary>Deletes the oldest automatic backups beyond the number to keep.</summary>
    private static void Prune(int keep)
    {
        var folder = Path.Combine(AppPaths.BackupsDir, "auto");
        if (!Directory.Exists(folder)) return;

        var files = new DirectoryInfo(folder)
            .GetFiles("records-*.zip")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(Math.Max(1, keep))
            .ToList();

        foreach (var file in files)
        {
            try { file.Delete(); }
            catch (IOException) { /* leave it for the next run */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    public List<FileInfo> ListBackups()
    {
        var all = new List<FileInfo>();
        foreach (var sub in new[] { "manual", "auto" })
        {
            var folder = Path.Combine(AppPaths.BackupsDir, sub);
            if (Directory.Exists(folder))
                all.AddRange(new DirectoryInfo(folder).GetFiles("records-*.zip"));
        }
        return all.OrderByDescending(f => f.CreationTimeUtc).ToList();
    }
}
