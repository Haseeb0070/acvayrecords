using RecordKeeper.Core.Models;

namespace RecordKeeper.Core.Data;

/// <summary>
/// Manages the attachment vault. Files chosen by the user are copied into the data
/// folder rather than linked, so moving or deleting the original never breaks a record.
/// </summary>
public sealed class AttachmentRepository
{
    private readonly Database _database;

    public AttachmentRepository(Database database) => _database = database;

    public List<Attachment> GetForRecord(long recordId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, record_id, file_name, relative_path, size_bytes, description, added_utc
            FROM attachment WHERE record_id = $id ORDER BY added_utc, id";
        command.Parameters.AddWithValue("$id", recordId);

        var attachments = new List<Attachment>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            attachments.Add(new Attachment
            {
                Id = reader.GetInt64(0),
                RecordId = reader.GetInt64(1),
                FileName = reader.GetString(2),
                RelativePath = reader.GetString(3),
                SizeBytes = reader.GetInt64(4),
                Description = reader.GetString(5),
                AddedUtc = RecordTypeRepository.ParseUtc(reader.GetString(6))
            });
        }
        return attachments;
    }

    /// <summary>
    /// Copies a file into the vault and links it to a record.
    /// The vault is laid out as attachments\year\month\recordId\filename so the folder
    /// stays browsable in File Explorer and never grows one flat directory of thousands.
    /// </summary>
    public Attachment Add(long recordId, string sourceFilePath, string description = "")
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("The file to attach no longer exists.", sourceFilePath);

        var now = DateTime.Now;
        var relativeFolder = Path.Combine("attachments", now.ToString("yyyy"), now.ToString("MM"), recordId.ToString());
        var absoluteFolder = AppPaths.Absolute(relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var fileName = Path.GetFileName(sourceFilePath);
        var targetName = MakeUniqueName(absoluteFolder, fileName);
        var targetPath = Path.Combine(absoluteFolder, targetName);

        File.Copy(sourceFilePath, targetPath, overwrite: false);

        var attachment = new Attachment
        {
            RecordId = recordId,
            FileName = fileName,
            RelativePath = Path.Combine(relativeFolder, targetName),
            SizeBytes = new FileInfo(targetPath).Length,
            Description = description ?? string.Empty,
            AddedUtc = DateTime.UtcNow
        };

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO attachment(record_id, file_name, relative_path, size_bytes, description, added_utc)
            VALUES($record, $name, $path, $size, $description, $added);
            SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$record", attachment.RecordId);
        command.Parameters.AddWithValue("$name", attachment.FileName);
        command.Parameters.AddWithValue("$path", attachment.RelativePath);
        command.Parameters.AddWithValue("$size", attachment.SizeBytes);
        command.Parameters.AddWithValue("$description", attachment.Description);
        command.Parameters.AddWithValue("$added", attachment.AddedUtc.ToString("O"));
        attachment.Id = Convert.ToInt64(command.ExecuteScalar());

        return attachment;
    }

    /// <summary>Unlinks an attachment and removes its copy from the vault.</summary>
    public void Remove(long attachmentId)
    {
        string? relativePath = null;

        using (var connection = _database.Open())
        {
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT relative_path FROM attachment WHERE id = $id";
                read.Parameters.AddWithValue("$id", attachmentId);
                relativePath = read.ExecuteScalar() as string;
            }

            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM attachment WHERE id = $id";
            delete.Parameters.AddWithValue("$id", attachmentId);
            delete.ExecuteNonQuery();
        }

        if (relativePath is not null) DeleteVaultFile(relativePath);
    }

    public void UpdateDescription(long attachmentId, string description)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE attachment SET description = $description WHERE id = $id";
        command.Parameters.AddWithValue("$description", description ?? string.Empty);
        command.Parameters.AddWithValue("$id", attachmentId);
        command.ExecuteNonQuery();
    }

    public static string AbsolutePathOf(Attachment attachment) => AppPaths.Absolute(attachment.RelativePath);

    /// <summary>Removes vault files left behind after records were purged.</summary>
    public static void DeleteVaultFiles(IEnumerable<string> relativePaths)
    {
        foreach (var path in relativePaths) DeleteVaultFile(path);
    }

    private static void DeleteVaultFile(string relativePath)
    {
        try
        {
            var absolute = AppPaths.Absolute(relativePath);
            if (File.Exists(absolute)) File.Delete(absolute);

            // Tidy up the per-record folder once its last file is gone.
            var folder = Path.GetDirectoryName(absolute);
            if (folder is not null && Directory.Exists(folder) &&
                Directory.GetFileSystemEntries(folder).Length == 0)
            {
                Directory.Delete(folder);
            }
        }
        catch (IOException) { /* a locked file is left for the next tidy-up */ }
        catch (UnauthorizedAccessException) { /* likewise */ }
    }

    private static string MakeUniqueName(string folder, string fileName)
    {
        if (!File.Exists(Path.Combine(folder, fileName))) return fileName;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var counter = 2; counter < 10_000; counter++)
        {
            var candidate = $"{stem} ({counter}){extension}";
            if (!File.Exists(Path.Combine(folder, candidate))) return candidate;
        }

        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }
}
