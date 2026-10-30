using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using RecordKeeper.Core.Models;

namespace RecordKeeper.Core.Data;

/// <summary>Reads and writes records, their field values, tags and recycle-bin state.</summary>
public sealed class RecordRepository
{
    private readonly Database _database;

    public RecordRepository(Database database) => _database = database;

    public List<RecordItem> Query(RecordQuery query)
    {
        using var connection = _database.Open();

        var sql = new StringBuilder(@"
            SELECT r.id, r.record_type_id, t.name, r.title, r.notes, r.is_favorite,
                   r.is_deleted, r.deleted_utc, r.created_utc, r.modified_utc,
                   (SELECT COUNT(*) FROM attachment a WHERE a.record_id = r.id)
            FROM record r
            JOIN record_type t ON t.id = r.record_type_id");

        using var command = connection.CreateCommand();

        if (query.SortFieldId is > 0)
        {
            // Sorting by one of the user's own fields means reaching into its value.
            // A LEFT JOIN, not an inner one, so a record with nothing in that field
            // still appears in the list rather than quietly disappearing from it.
            sql.Append(" LEFT JOIN field_value sv ON sv.record_id = r.id AND sv.field_def_id = $sortfield");
            command.Parameters.AddWithValue("$sortfield", query.SortFieldId.Value);
        }

        sql.Append(" WHERE r.is_deleted = $deleted");
        command.Parameters.AddWithValue("$deleted", query.ShowDeleted ? 1 : 0);

        AppendFilters(sql, command, query);
        AppendOrder(sql, query);

        command.CommandText = sql.ToString();

        var records = new List<RecordItem>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) records.Add(ReadRecord(reader));
        }

        if (records.Count > 0)
        {
            LoadValues(connection, records);
            LoadTags(connection, records);
        }

        return records;
    }

    /// <summary>
    /// What the records matching a query add up to, field by field.
    ///
    /// This runs the same filters as <see cref="Query"/> — the same method builds
    /// them — so the total shown above a list always describes that list and not some
    /// other set of records. A total that quietly disagrees with what is on screen is
    /// worse than no total at all.
    /// </summary>
    public RecordTotals Totals(RecordQuery query)
    {
        using var connection = _database.Open();

        // Only the fields the user asked to have added up. Not every column of numbers
        // is worth a sum, and only they know which of theirs is which.
        var sql = new StringBuilder(@"
            SELECT v.field_def_id, SUM(v.value_number), COUNT(v.value_number)
            FROM field_value v
            JOIN field_def f   ON f.id = v.field_def_id AND f.total_in_list = 1
            JOIN record r      ON r.id = v.record_id
            JOIN record_type t ON t.id = r.record_type_id
            WHERE r.is_deleted = $deleted AND v.value_number IS NOT NULL");

        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$deleted", query.ShowDeleted ? 1 : 0);

        AppendFilters(sql, command, query);
        sql.Append(" GROUP BY v.field_def_id");

        command.CommandText = sql.ToString();

        var totals = new RecordTotals();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var fieldId = reader.GetInt64(0);
            totals.Sums[fieldId] = (decimal)reader.GetDouble(1);
            totals.Counts[fieldId] = reader.GetInt32(2);
        }

        return totals;
    }

    /// <summary>
    /// Re-applies a category's formulas to the records already saved.
    ///
    /// Changing a formula used to leave every existing record showing the total worked
    /// out under the old one, until each was opened and saved again. Only records whose
    /// result actually changes are written, so running this twice does nothing the
    /// second time, and a record that was already right keeps its modified date.
    /// </summary>
    public RecalculationResult Recalculate(RecordType type)
    {
        var result = new RecalculationResult();

        var calculated = type.Fields.Where(f => f.IsCalculated).ToList();
        if (calculated.Count == 0) return result;

        // Records in the recycle bin are included: one restored later should not come
        // back carrying a total from a formula that no longer exists.
        var records = Query(new RecordQuery { RecordTypeId = type.Id });
        records.AddRange(Query(new RecordQuery { RecordTypeId = type.Id, ShowDeleted = true }));

        result.RecordsChecked = records.Count;
        if (records.Count == 0) return result;

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var now = DateTime.UtcNow.ToString("O");

        foreach (var record in records)
        {
            var before = new Dictionary<long, string>(record.Values);
            RecordCalculator.ApplyFormulas(type.Fields, record.Values);

            var touched = calculated
                .Where(field => Value(before, field.Id) != record.GetValue(field.Id))
                .ToList();

            if (touched.Count == 0) continue;

            foreach (var field in touched)
            {
                var value = record.GetValue(field.Id);

                using var write = connection.CreateCommand();
                write.Transaction = transaction;

                if (value.Length == 0)
                {
                    // The formula can no longer be worked out. Removing the row says
                    // "not known"; storing a zero would say the total really is zero.
                    write.CommandText =
                        "DELETE FROM field_value WHERE record_id = $record AND field_def_id = $field";
                }
                else
                {
                    write.CommandText = @"
                        INSERT INTO field_value(record_id, field_def_id, value_text, value_number)
                        VALUES($record, $field, $text, $number)
                        ON CONFLICT(record_id, field_def_id)
                        DO UPDATE SET value_text = $text, value_number = $number";
                    write.Parameters.AddWithValue("$text", value);
                    write.Parameters.AddWithValue("$number",
                        FieldValueFormat.ToSortNumber(field.DataType, value) ?? (object)DBNull.Value);
                }

                write.Parameters.AddWithValue("$record", record.Id);
                write.Parameters.AddWithValue("$field", field.Id);
                write.ExecuteNonQuery();

                result.ValuesChanged++;
            }

            using var stamp = connection.CreateCommand();
            stamp.Transaction = transaction;
            stamp.CommandText = "UPDATE record SET modified_utc = $now WHERE id = $id";
            stamp.Parameters.AddWithValue("$now", now);
            stamp.Parameters.AddWithValue("$id", record.Id);
            stamp.ExecuteNonQuery();

            result.RecordsChanged++;
        }

        transaction.Commit();
        return result;
    }

    private static string Value(Dictionary<long, string> values, long fieldId) =>
        values.TryGetValue(fieldId, out var value) ? value : string.Empty;

    /// <summary>
    /// Everything after "WHERE r.is_deleted = ?", shared by the list and the totals.
    /// </summary>
    private static void AppendFilters(StringBuilder sql, SqliteCommand command, RecordQuery query)
    {
        if (query.RecordTypeId is > 0)
        {
            sql.Append(" AND r.record_type_id = $type");
            command.Parameters.AddWithValue("$type", query.RecordTypeId.Value);
        }

        if (query.GroupId is > 0)
        {
            sql.Append(" AND t.group_id = $group");
            command.Parameters.AddWithValue("$group", query.GroupId.Value);
        }

        if (query.OnlyFavorites)
            sql.Append(" AND r.is_favorite = 1");

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            sql.Append(@" AND EXISTS (
                SELECT 1 FROM record_tag rt JOIN tag g ON g.id = rt.tag_id
                WHERE rt.record_id = r.id AND g.name = $tag COLLATE NOCASE)");
            command.Parameters.AddWithValue("$tag", query.Tag);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            // Search covers the title, the notes and every custom field value,
            // so the user does not have to remember which field holds the text.
            sql.Append(@" AND (
                r.title LIKE $search ESCAPE '\'
                OR r.notes LIKE $search ESCAPE '\'
                OR EXISTS (SELECT 1 FROM field_value v2
                           WHERE v2.record_id = r.id AND v2.value_text LIKE $search ESCAPE '\')
                OR EXISTS (SELECT 1 FROM record_tag rt JOIN tag g ON g.id = rt.tag_id
                           WHERE rt.record_id = r.id AND g.name LIKE $search ESCAPE '\'))");
            command.Parameters.AddWithValue("$search", "%" + EscapeLike(query.SearchText.Trim()) + "%");
        }

        AppendPeriod(sql, command, query);
    }

    /// <summary>
    /// The date range, applied either to one of the user's own date fields or, when
    /// no field is named, to the date the record was first saved.
    /// </summary>
    private static void AppendPeriod(StringBuilder sql, SqliteCommand command, RecordQuery query)
    {
        if (query.From is null && query.To is null) return;

        if (query.DateFieldId is > 0)
        {
            // Dates are stored as yyyy-MM-dd, which compares correctly as text.
            sql.Append(@" AND EXISTS (SELECT 1 FROM field_value dv
                            WHERE dv.record_id = r.id AND dv.field_def_id = $datefield");
            command.Parameters.AddWithValue("$datefield", query.DateFieldId.Value);

            if (query.From is { } from)
            {
                sql.Append(" AND dv.value_text >= $fromday");
                command.Parameters.AddWithValue("$fromday", Day(from));
            }

            if (query.To is { } to)
            {
                sql.Append(" AND dv.value_text <= $today");
                command.Parameters.AddWithValue("$today", Day(to));
            }

            sql.Append(')');
            return;
        }

        // created_utc is stored in round-trip format, which also compares as text.
        if (query.From is { } start)
        {
            sql.Append(" AND r.created_utc >= $fromutc");
            command.Parameters.AddWithValue("$fromutc", Utc(start.Date));
        }

        if (query.To is { } end)
        {
            // The whole of the closing day counts, so the bound is the next midnight.
            sql.Append(" AND r.created_utc < $toutc");
            command.Parameters.AddWithValue("$toutc", Utc(end.Date.AddDays(1)));
        }
    }

    private static string Day(DateTime date) =>
        date.ToString(FieldValueFormat.DateFormat, CultureInfo.InvariantCulture);

    private static string Utc(DateTime localMidnight) =>
        DateTime.SpecifyKind(localMidnight, DateTimeKind.Local).ToUniversalTime().ToString("O");

    private static void AppendOrder(StringBuilder sql, RecordQuery query)
    {
        if (query.SortFieldId is > 0)
        {
            // Records with nothing in the field go last either way round. "Not filled
            // in" is not a small amount, and it is not an early date either.
            sql.Append(" ORDER BY (sv.record_id IS NULL) ASC, ");
            sql.Append(query.SortFieldIsNumeric ? "sv.value_number" : "sv.value_text COLLATE NOCASE");
            sql.Append(query.SortDescending ? " DESC" : " ASC");
            sql.Append(", r.title COLLATE NOCASE ASC");
            return;
        }

        sql.Append(query.SortColumn switch
        {
            "Created"  => " ORDER BY r.created_utc",
            "Modified" => " ORDER BY r.modified_utc",
            "Type"     => " ORDER BY t.name COLLATE NOCASE, r.title COLLATE NOCASE",
            "Deleted"  => " ORDER BY r.deleted_utc",
            _          => " ORDER BY r.title COLLATE NOCASE"
        });

        sql.Append(query.SortDescending ? " DESC" : " ASC");
    }

    public RecordItem? GetById(long id)
    {
        using var connection = _database.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT r.id, r.record_type_id, t.name, r.title, r.notes, r.is_favorite,
                   r.is_deleted, r.deleted_utc, r.created_utc, r.modified_utc,
                   (SELECT COUNT(*) FROM attachment a WHERE a.record_id = r.id)
            FROM record r
            JOIN record_type t ON t.id = r.record_type_id
            WHERE r.id = $id";
        command.Parameters.AddWithValue("$id", id);

        RecordItem? record = null;
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read()) record = ReadRecord(reader);
        }

        if (record is null) return null;

        var list = new List<RecordItem> { record };
        LoadValues(connection, list);
        LoadTags(connection, list);
        return record;
    }

    /// <summary>Inserts or updates a record and replaces its field values and tags.</summary>
    public long Save(RecordItem record)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var now = DateTime.UtcNow.ToString("O");

        if (record.Id == 0)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"
                INSERT INTO record(record_type_id, title, notes, is_favorite, is_deleted, created_utc, modified_utc)
                VALUES($type, $title, $notes, $favorite, 0, $now, $now);
                SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$type", record.RecordTypeId);
            insert.Parameters.AddWithValue("$title", record.Title);
            insert.Parameters.AddWithValue("$notes", record.Notes ?? string.Empty);
            insert.Parameters.AddWithValue("$favorite", record.IsFavorite ? 1 : 0);
            insert.Parameters.AddWithValue("$now", now);
            record.Id = Convert.ToInt64(insert.ExecuteScalar());
            record.CreatedUtc = DateTime.UtcNow;
        }
        else
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = @"
                UPDATE record
                SET title = $title, notes = $notes, is_favorite = $favorite, modified_utc = $now
                WHERE id = $id";
            update.Parameters.AddWithValue("$title", record.Title);
            update.Parameters.AddWithValue("$notes", record.Notes ?? string.Empty);
            update.Parameters.AddWithValue("$favorite", record.IsFavorite ? 1 : 0);
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$id", record.Id);
            update.ExecuteNonQuery();
        }

        record.ModifiedUtc = DateTime.UtcNow;

        SaveValues(connection, transaction, record);
        SaveTags(connection, transaction, record);

        transaction.Commit();
        return record.Id;
    }

    private static void SaveValues(SqliteConnection connection, SqliteTransaction transaction, RecordItem record)
    {
        // The field definitions tell us how to produce the numeric sort mirror, and
        // which fields are worked out from a formula rather than entered.
        var fields = new List<FieldDef>();
        using (var types = connection.CreateCommand())
        {
            types.Transaction = transaction;
            types.CommandText =
                "SELECT id, name, data_type, formula FROM field_def WHERE record_type_id = $type ORDER BY sort_order, id";
            types.Parameters.AddWithValue("$type", record.RecordTypeId);

            using var reader = types.ExecuteReader();
            while (reader.Read())
            {
                fields.Add(new FieldDef
                {
                    Id = reader.GetInt64(0),
                    RecordTypeId = record.RecordTypeId,
                    Name = reader.GetString(1),
                    DataType = (FieldDataType)reader.GetInt32(2),
                    Formula = reader.GetString(3)
                });
            }
        }

        // Make the stored totals match the inputs, whichever code path is saving.
        RecordCalculator.ApplyFormulas(fields, record.Values);

        var fieldTypes = fields.ToDictionary(f => f.Id, f => f.DataType);

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM field_value WHERE record_id = $id";
            clear.Parameters.AddWithValue("$id", record.Id);
            clear.ExecuteNonQuery();
        }

        foreach (var (fieldId, value) in record.Values)
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (!fieldTypes.TryGetValue(fieldId, out var dataType)) continue;

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"
                INSERT INTO field_value(record_id, field_def_id, value_text, value_number)
                VALUES($record, $field, $text, $number)";
            insert.Parameters.AddWithValue("$record", record.Id);
            insert.Parameters.AddWithValue("$field", fieldId);
            insert.Parameters.AddWithValue("$text", value);

            var sortNumber = FieldValueFormat.ToSortNumber(dataType, value);
            insert.Parameters.AddWithValue("$number", sortNumber ?? (object)DBNull.Value);
            insert.ExecuteNonQuery();
        }
    }

    private static void SaveTags(SqliteConnection connection, SqliteTransaction transaction, RecordItem record)
    {
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM record_tag WHERE record_id = $id";
            clear.Parameters.AddWithValue("$id", record.Id);
            clear.ExecuteNonQuery();
        }

        foreach (var name in record.Tags
                     .Select(t => t.Trim())
                     .Where(t => t.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            long tagId;
            using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = "SELECT id FROM tag WHERE name = $name COLLATE NOCASE";
                find.Parameters.AddWithValue("$name", name);
                var existing = find.ExecuteScalar();

                if (existing is not null && existing != DBNull.Value)
                {
                    tagId = Convert.ToInt64(existing);
                }
                else
                {
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO tag(name) VALUES($name); SELECT last_insert_rowid();";
                    insert.Parameters.AddWithValue("$name", name);
                    tagId = Convert.ToInt64(insert.ExecuteScalar());
                }
            }

            using var link = connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText = "INSERT OR IGNORE INTO record_tag(record_id, tag_id) VALUES($record, $tag)";
            link.Parameters.AddWithValue("$record", record.Id);
            link.Parameters.AddWithValue("$tag", tagId);
            link.ExecuteNonQuery();
        }
    }

    /// <summary>Moves records to the recycle bin. Nothing is destroyed.</summary>
    public int MoveToRecycleBin(IEnumerable<long> ids)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");
        var affected = 0;

        foreach (var id in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE record SET is_deleted = 1, deleted_utc = $now WHERE id = $id AND is_deleted = 0";
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$id", id);
            affected += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return affected;
    }

    public int Restore(IEnumerable<long> ids)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        var affected = 0;

        foreach (var id in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE record SET is_deleted = 0, deleted_utc = NULL WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            affected += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return affected;
    }

    /// <summary>
    /// Permanently removes records. Returns the vault paths of attachments that the
    /// caller should now delete from disk.
    /// </summary>
    public List<string> PurgePermanently(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        var orphanedFiles = new List<string>();
        if (idList.Count == 0) return orphanedFiles;

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var id in idList)
        {
            using (var files = connection.CreateCommand())
            {
                files.Transaction = transaction;
                files.CommandText = "SELECT relative_path FROM attachment WHERE record_id = $id";
                files.Parameters.AddWithValue("$id", id);
                using var reader = files.ExecuteReader();
                while (reader.Read()) orphanedFiles.Add(reader.GetString(0));
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM record WHERE id = $id";
            delete.Parameters.AddWithValue("$id", id);
            delete.ExecuteNonQuery();
        }

        transaction.Commit();
        return orphanedFiles;
    }

    public void SetFavorite(long id, bool isFavorite)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE record SET is_favorite = $favorite, modified_utc = $now WHERE id = $id";
        command.Parameters.AddWithValue("$favorite", isFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public int CountInRecycleBin()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM record WHERE is_deleted = 1";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int CountFavorites()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM record WHERE is_favorite = 1 AND is_deleted = 0";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>All tag names in use, with how many live records carry each.</summary>
    public List<(string Name, int Count)> GetTagsInUse()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT g.name, COUNT(*)
            FROM tag g
            JOIN record_tag rt ON rt.tag_id = g.id
            JOIN record r ON r.id = rt.record_id AND r.is_deleted = 0
            GROUP BY g.id, g.name
            ORDER BY g.name COLLATE NOCASE";

        var tags = new List<(string, int)>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) tags.Add((reader.GetString(0), reader.GetInt32(1)));
        return tags;
    }

    /// <summary>
    /// Finds live records of the same type carrying the same title, so the user is
    /// warned before creating a near-duplicate.
    /// </summary>
    public List<string> FindPossibleDuplicates(long recordTypeId, string title, long exceptId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT title FROM record
            WHERE record_type_id = $type AND is_deleted = 0 AND id <> $except
              AND title = $title COLLATE NOCASE
            LIMIT 5";
        command.Parameters.AddWithValue("$type", recordTypeId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$except", exceptId);

        var matches = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) matches.Add(reader.GetString(0));
        return matches;
    }

    private static RecordItem ReadRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        RecordTypeId = reader.GetInt64(1),
        RecordTypeName = reader.GetString(2),
        Title = reader.GetString(3),
        Notes = reader.GetString(4),
        IsFavorite = reader.GetInt32(5) != 0,
        IsDeleted = reader.GetInt32(6) != 0,
        DeletedUtc = reader.IsDBNull(7) ? null : RecordTypeRepository.ParseUtc(reader.GetString(7)),
        CreatedUtc = RecordTypeRepository.ParseUtc(reader.GetString(8)),
        ModifiedUtc = RecordTypeRepository.ParseUtc(reader.GetString(9)),
        AttachmentCount = reader.GetInt32(10)
    };

    private static void LoadValues(SqliteConnection connection, List<RecordItem> records)
    {
        var byId = records.ToDictionary(r => r.Id);
        var ids = string.Join(",", byId.Keys);

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT record_id, field_def_id, value_text FROM field_value WHERE record_id IN ({ids})";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (byId.TryGetValue(reader.GetInt64(0), out var record))
                record.Values[reader.GetInt64(1)] = reader.GetString(2);
        }
    }

    private static void LoadTags(SqliteConnection connection, List<RecordItem> records)
    {
        var byId = records.ToDictionary(r => r.Id);
        var ids = string.Join(",", byId.Keys);

        using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT rt.record_id, g.name
            FROM record_tag rt JOIN tag g ON g.id = rt.tag_id
            WHERE rt.record_id IN ({ids})
            ORDER BY g.name COLLATE NOCASE";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (byId.TryGetValue(reader.GetInt64(0), out var record))
                record.Tags.Add(reader.GetString(1));
        }
    }

    private static string EscapeLike(string text) => text
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
