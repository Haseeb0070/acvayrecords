using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace RecordKeeper.Core.Data;

/// <summary>
/// Owns the SQLite file and its schema. SQLite gives atomic, crash-safe writes
/// with no server and no configuration, which is what an offline desktop record
/// store needs.
/// </summary>
public sealed class Database
{
    /// <summary>
    /// Bump this and add a step to <see cref="Migrate"/> whenever the schema changes.
    ///   2  the formula column that makes a field calculated
    ///   3  category groups, and the flag that marks a field as an expense
    /// </summary>
    private const int SchemaVersion = 4;

    public string ConnectionString { get; }

    /// <summary>
    /// The file this database lives in. Named FilePath rather than File so it does not
    /// shadow System.IO.File for the rest of this class.
    /// </summary>
    public string FilePath { get; }

    public Database(string? databaseFile = null, string? password = null)
    {
        AppPaths.EnsureCreated();

        FilePath = databaseFile ?? AppPaths.DatabaseFile;
        ConnectionString = BuildConnectionString(FilePath, password);
    }

    /// <summary>
    /// Builds a connection string, including the key when the database is encrypted.
    /// An empty or null password means an ordinary, unencrypted file.
    /// </summary>
    private static string BuildConnectionString(string file, string? password)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        if (!string.IsNullOrEmpty(password)) builder.Password = password;

        return builder.ToString();
    }

    /// <summary>
    /// Whether this file can be opened with this password. Used to check a password
    /// before anything is done with it, and to tell "wrong password" apart from
    /// "corrupt file".
    /// </summary>
    public static bool CanOpen(string file, string? password)
    {
        if (!System.IO.File.Exists(file)) return false;

        try
        {
            using var connection = new SqliteConnection(BuildConnectionString(file, password));
            connection.Open();

            // Reading the schema is what actually exercises the key: an open alone
            // succeeds even with the wrong password.
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
            command.ExecuteScalar();

            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// Converts the database between encrypted and unencrypted, or changes its password.
    ///
    /// Pass null or empty as <paramref name="newPassword"/> to decrypt. The work is done
    /// into a new file and only swapped in once it has been written in full, and the
    /// previous file is kept alongside, so an interruption cannot leave the user with
    /// half a database.
    /// </summary>
    /// <returns>The path of the kept copy of the previous file.</returns>
    public static string Convert(string file, string? currentPassword, string? newPassword)
    {
        if (!System.IO.File.Exists(file))
            throw new FileNotFoundException("There is no database to convert.", file);

        if (!CanOpen(file, currentPassword))
            throw new InvalidOperationException("The current password does not open this database.");

        var converted = file + ".converting";
        var kept = file + ".previous";

        foreach (var leftover in new[] { converted, kept })
            if (System.IO.File.Exists(leftover)) System.IO.File.Delete(leftover);

        using (var connection = new SqliteConnection(BuildConnectionString(file, currentPassword)))
        {
            connection.Open();

            using (var checkpoint = connection.CreateCommand())
            {
                // Fold the write-ahead log in first, or the copy misses recent changes.
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
            }

            using var export = connection.CreateCommand();
            export.CommandText =
                "ATTACH DATABASE $target AS converted KEY $key; " +
                "SELECT sqlcipher_export('converted'); " +
                "DETACH DATABASE converted;";
            export.Parameters.AddWithValue("$target", converted);
            export.Parameters.AddWithValue("$key", newPassword ?? string.Empty);
            export.ExecuteNonQuery();
        }

        // Connection pooling would otherwise keep a handle on the old file.
        SqliteConnection.ClearAllPools();

        var readable = CanOpen(converted, newPassword);

        // CanOpen opened its own connection, and that one is pooled too. Without this
        // second clear the swap below fails with "the file is in use by another
        // process", having just verified the file is good.
        SqliteConnection.ClearAllPools();

        if (!readable)
        {
            System.IO.File.Delete(converted);
            throw new InvalidOperationException(
                "The converted database could not be opened, so nothing was changed.");
        }

        ReplaceWithRetry(converted, file, kept);

        // The old journal files belong to the file that has just been replaced.
        foreach (var journal in new[] { file + "-wal", file + "-shm" })
        {
            try
            {
                if (System.IO.File.Exists(journal)) System.IO.File.Delete(journal);
            }
            catch (IOException)
            {
                // A stale journal next to a good database is harmless; SQLite will
                // tidy it up on the next clean close.
            }
        }

        return kept;
    }

    /// <summary>
    /// Swaps the converted file in, retrying briefly if the old one is still held.
    ///
    /// Closing a SQLite connection returns it to the pool rather than releasing the
    /// operating-system handle at once, and a virus scanner or the search indexer can
    /// hold a database file for a moment after it is written. Both clear in well under
    /// a second, so a short retry is the difference between this working and failing
    /// for no reason the user could act on.
    /// </summary>
    private static void ReplaceWithRetry(string source, string destination, string backup)
    {
        const int attempts = 12;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                System.IO.File.Replace(source, destination, backup);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(150);
            }
        }
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        // WAL keeps a reader from blocking a save, and survives an abrupt shutdown.
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    /// <summary>Creates the schema on a new file, or upgrades an older one.</summary>
    public void Initialize()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        Execute(connection, transaction,
            "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);");

        var current = ReadSchemaVersion(connection, transaction);

        if (current == 0)
        {
            CreateSchema(connection, transaction);
            WriteSchemaVersion(connection, transaction, SchemaVersion);
        }
        else if (current < SchemaVersion)
        {
            Migrate(connection, transaction, current);
            WriteSchemaVersion(connection, transaction, SchemaVersion);
        }

        transaction.Commit();
    }

    /// <summary>Upgrades an older database one version at a time, in place.</summary>
    private static void Migrate(SqliteConnection connection, SqliteTransaction transaction, int from)
    {
        if (from < 2)
        {
            Execute(connection, transaction,
                "ALTER TABLE field_def ADD COLUMN formula TEXT NOT NULL DEFAULT '';");
            Execute(connection, transaction,
                "ALTER TABLE record_type ADD COLUMN title_label TEXT NOT NULL DEFAULT '';");
        }

        if (from < 3)
        {
            Execute(connection, transaction, @"
                CREATE TABLE category_group (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    name        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                    sort_order  INTEGER NOT NULL DEFAULT 0,
                    created_utc TEXT    NOT NULL
                );");

            // Deleting a group leaves its categories in place, ungrouped. Losing a
            // group must never take records with it.
            Execute(connection, transaction,
                "ALTER TABLE record_type ADD COLUMN group_id INTEGER NULL REFERENCES category_group(id) ON DELETE SET NULL;");

            Execute(connection, transaction,
                "ALTER TABLE field_def ADD COLUMN is_expense INTEGER NOT NULL DEFAULT 0;");
        }

        if (from < 4)
        {
            Execute(connection, transaction,
                "ALTER TABLE field_def ADD COLUMN total_in_list INTEGER NOT NULL DEFAULT 0;");

            // Which numeric fields are worth adding up is a judgement, so the upgrade
            // makes the one it can defend and leaves the rest to the user.
            //
            // A field that feeds a formula is usually a rate or an input — a price per
            // piece, a quantity — and a sum of those says very little. A field nothing
            // is worked out from, and a field that is itself worked out, are the ones
            // a column of money is normally added up for.
            //
            // Getting this wrong in the cautious direction costs one tick in Edit
            // field. Getting it wrong the other way puts a meaningless number in front
            // of someone who cannot see at a glance that it is meaningless.
            Execute(connection, transaction, @"
                UPDATE field_def
                SET total_in_list = 1
                WHERE data_type IN (2, 3)   -- Number and Money
                  AND NOT EXISTS (
                      SELECT 1 FROM field_def other
                      WHERE other.record_type_id = field_def.record_type_id
                        AND other.formula <> ''
                        AND instr(lower(other.formula), '[' || lower(field_def.name) || ']') > 0
                  );");
        }
    }

    /// <summary>Reads a small named value from the meta table, or null when it is not set.</summary>
    public string? GetMeta(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO meta(key, value) VALUES($key, $value) " +
            "ON CONFLICT(key) DO UPDATE SET value = $value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void CreateSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, @"
            CREATE TABLE category_group (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                name        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                sort_order  INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT    NOT NULL
            );

            CREATE TABLE record_type (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                name          TEXT    NOT NULL UNIQUE,
                description   TEXT    NOT NULL DEFAULT '',
                title_label   TEXT    NOT NULL DEFAULT '',
                group_id      INTEGER NULL REFERENCES category_group(id) ON DELETE SET NULL,
                sort_order    INTEGER NOT NULL DEFAULT 0,
                created_utc   TEXT    NOT NULL,
                modified_utc  TEXT    NOT NULL
            );

            CREATE TABLE field_def (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                record_type_id INTEGER NOT NULL REFERENCES record_type(id) ON DELETE CASCADE,
                name           TEXT    NOT NULL,
                data_type      INTEGER NOT NULL,
                is_required    INTEGER NOT NULL DEFAULT 0,
                choices        TEXT    NOT NULL DEFAULT '',
                default_value  TEXT    NOT NULL DEFAULT '',
                help_text      TEXT    NOT NULL DEFAULT '',
                formula        TEXT    NOT NULL DEFAULT '',
                is_expense     INTEGER NOT NULL DEFAULT 0,
                total_in_list  INTEGER NOT NULL DEFAULT 0,
                sort_order     INTEGER NOT NULL DEFAULT 0,
                show_in_list   INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX ix_field_def_type ON field_def(record_type_id, sort_order);

            CREATE TABLE record (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                record_type_id INTEGER NOT NULL REFERENCES record_type(id) ON DELETE RESTRICT,
                title          TEXT    NOT NULL,
                notes          TEXT    NOT NULL DEFAULT '',
                is_favorite    INTEGER NOT NULL DEFAULT 0,
                is_deleted     INTEGER NOT NULL DEFAULT 0,
                deleted_utc    TEXT    NULL,
                created_utc    TEXT    NOT NULL,
                modified_utc   TEXT    NOT NULL
            );
            CREATE INDEX ix_record_type    ON record(record_type_id, is_deleted);
            CREATE INDEX ix_record_title   ON record(title);
            CREATE INDEX ix_record_deleted ON record(is_deleted);

            CREATE TABLE field_value (
                record_id    INTEGER NOT NULL REFERENCES record(id)    ON DELETE CASCADE,
                field_def_id INTEGER NOT NULL REFERENCES field_def(id) ON DELETE CASCADE,
                value_text   TEXT    NOT NULL DEFAULT '',
                value_number REAL    NULL,
                PRIMARY KEY (record_id, field_def_id)
            );
            CREATE INDEX ix_field_value_field ON field_value(field_def_id);

            CREATE TABLE tag (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE
            );

            CREATE TABLE record_tag (
                record_id INTEGER NOT NULL REFERENCES record(id) ON DELETE CASCADE,
                tag_id    INTEGER NOT NULL REFERENCES tag(id)    ON DELETE CASCADE,
                PRIMARY KEY (record_id, tag_id)
            );
            CREATE INDEX ix_record_tag_tag ON record_tag(tag_id);

            CREATE TABLE attachment (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                record_id     INTEGER NOT NULL REFERENCES record(id) ON DELETE CASCADE,
                file_name     TEXT    NOT NULL,
                relative_path TEXT    NOT NULL,
                size_bytes    INTEGER NOT NULL DEFAULT 0,
                description   TEXT    NOT NULL DEFAULT '',
                added_utc     TEXT    NOT NULL
            );
            CREATE INDEX ix_attachment_record ON attachment(record_id);
        ");
    }

    private static int ReadSchemaVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        var value = command.ExecuteScalar() as string;
        return int.TryParse(value, out var version) ? version : 0;
    }

    private static void WriteSchemaVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO meta(key, value) VALUES('schema_version', $v) " +
            "ON CONFLICT(key) DO UPDATE SET value = $v";
        command.Parameters.AddWithValue("$v", version.ToString());
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Copies the database and attachments into a single timestamped zip.</summary>
    public string CreateBackup(bool automatic)
    {
        using (var connection = Open())
        {
            using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }

        var folder = Path.Combine(AppPaths.BackupsDir, automatic ? "auto" : "manual");
        Directory.CreateDirectory(folder);

        var zipPath = Path.Combine(folder, $"records-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip");

        var staging = Path.Combine(Path.GetTempPath(), "RecordKeeperBackup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            File.Copy(AppPaths.DatabaseFile, Path.Combine(staging, "records.db"), overwrite: true);

            if (Directory.Exists(AppPaths.AttachmentsDir))
                CopyDirectory(AppPaths.AttachmentsDir, Path.Combine(staging, "attachments"));

            if (File.Exists(AppPaths.SettingsFile))
                File.Copy(AppPaths.SettingsFile, Path.Combine(staging, "settings.json"), overwrite: true);

            ZipFile.CreateFromDirectory(staging, zipPath);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
        }

        return zipPath;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
