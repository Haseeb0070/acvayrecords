using Microsoft.Data.Sqlite;
using RecordKeeper.Core.Models;

namespace RecordKeeper.Core.Data;

/// <summary>Reads and writes record types and their custom field definitions.</summary>
public sealed class RecordTypeRepository
{
    private readonly Database _database;

    public RecordTypeRepository(Database database) => _database = database;

    public List<RecordType> GetAll(bool includeFieldsAndCounts = true)
    {
        using var connection = _database.Open();
        var types = new List<RecordType>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT t.id, t.name, t.description, t.sort_order, t.created_utc, t.modified_utc,
                       (SELECT COUNT(*) FROM record r WHERE r.record_type_id = t.id AND r.is_deleted = 0),
                       t.title_label, t.group_id, COALESCE(g.name, '')
                FROM record_type t
                LEFT JOIN category_group g ON g.id = t.group_id
                ORDER BY t.sort_order, t.name COLLATE NOCASE";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                types.Add(new RecordType
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Description = reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                    CreatedUtc = ParseUtc(reader.GetString(4)),
                    ModifiedUtc = ParseUtc(reader.GetString(5)),
                    RecordCount = reader.GetInt32(6),
                    TitleLabel = reader.GetString(7),
                    GroupId = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    GroupName = reader.GetString(9)
                });
            }
        }

        if (!includeFieldsAndCounts) return types;

        var fieldsByType = LoadAllFields(connection);
        foreach (var type in types)
        {
            if (fieldsByType.TryGetValue(type.Id, out var fields))
                type.Fields = fields;
        }

        return types;
    }

    public RecordType? GetById(long id)
    {
        using var connection = _database.Open();

        RecordType? type = null;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT t.id, t.name, t.description, t.sort_order, t.created_utc, t.modified_utc,
                       t.title_label, t.group_id, COALESCE(g.name, '')
                FROM record_type t
                LEFT JOIN category_group g ON g.id = t.group_id
                WHERE t.id = $id";
            command.Parameters.AddWithValue("$id", id);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                type = new RecordType
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Description = reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                    CreatedUtc = ParseUtc(reader.GetString(4)),
                    ModifiedUtc = ParseUtc(reader.GetString(5)),
                    TitleLabel = reader.GetString(6),
                    GroupId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    GroupName = reader.GetString(8)
                };
            }
        }

        if (type is null) return null;
        type.Fields = LoadFields(connection, type.Id);
        return type;
    }

    /// <summary>Saves a record type together with its full field list, in one transaction.</summary>
    public long Save(RecordType type)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var now = DateTime.UtcNow.ToString("O");

        if (type.Id == 0)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"
                INSERT INTO record_type(name, description, title_label, group_id, sort_order, created_utc, modified_utc)
                VALUES($name, $description, $titleLabel, $group, $sort, $now, $now);
                SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$name", type.Name);
            insert.Parameters.AddWithValue("$description", type.Description ?? string.Empty);
            insert.Parameters.AddWithValue("$titleLabel", type.TitleLabel ?? string.Empty);
            insert.Parameters.AddWithValue("$group", type.GroupId ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$sort", type.SortOrder);
            insert.Parameters.AddWithValue("$now", now);
            type.Id = Convert.ToInt64(insert.ExecuteScalar());
        }
        else
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = @"
                UPDATE record_type
                SET name = $name, description = $description, title_label = $titleLabel,
                    group_id = $group, sort_order = $sort, modified_utc = $now
                WHERE id = $id";
            update.Parameters.AddWithValue("$name", type.Name);
            update.Parameters.AddWithValue("$description", type.Description ?? string.Empty);
            update.Parameters.AddWithValue("$titleLabel", type.TitleLabel ?? string.Empty);
            update.Parameters.AddWithValue("$group", type.GroupId ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("$sort", type.SortOrder);
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$id", type.Id);
            update.ExecuteNonQuery();
        }

        SaveFields(connection, transaction, type);

        transaction.Commit();
        return type.Id;
    }

    private static void SaveFields(SqliteConnection connection, SqliteTransaction transaction, RecordType type)
    {
        // Remove fields the user took out of the type. Their stored values go with
        // them through the ON DELETE CASCADE on field_value.
        var keptIds = type.Fields.Where(f => f.Id != 0).Select(f => f.Id).ToList();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = keptIds.Count == 0
                ? "DELETE FROM field_def WHERE record_type_id = $type"
                : $"DELETE FROM field_def WHERE record_type_id = $type AND id NOT IN ({string.Join(",", keptIds)})";
            delete.Parameters.AddWithValue("$type", type.Id);
            delete.ExecuteNonQuery();
        }

        for (var i = 0; i < type.Fields.Count; i++)
        {
            var field = type.Fields[i];
            field.RecordTypeId = type.Id;
            field.SortOrder = i;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            if (field.Id == 0)
            {
                command.CommandText = @"
                    INSERT INTO field_def(record_type_id, name, data_type, is_required, choices,
                                          default_value, help_text, formula, is_expense, total_in_list, sort_order, show_in_list)
                    VALUES($type, $name, $dataType, $required, $choices,
                           $defaultValue, $help, $formula, $isExpense, $totalInList, $sort, $showInList);
                    SELECT last_insert_rowid();";
            }
            else
            {
                command.CommandText = @"
                    UPDATE field_def
                    SET name = $name, data_type = $dataType, is_required = $required,
                        choices = $choices, default_value = $defaultValue, help_text = $help,
                        formula = $formula, is_expense = $isExpense, total_in_list = $totalInList,
                        sort_order = $sort, show_in_list = $showInList
                    WHERE id = $id";
                command.Parameters.AddWithValue("$id", field.Id);
            }

            command.Parameters.AddWithValue("$type", type.Id);
            command.Parameters.AddWithValue("$name", field.Name);
            command.Parameters.AddWithValue("$dataType", (int)field.DataType);
            command.Parameters.AddWithValue("$required", field.IsRequired ? 1 : 0);
            command.Parameters.AddWithValue("$choices", field.Choices ?? string.Empty);
            command.Parameters.AddWithValue("$defaultValue", field.DefaultValue ?? string.Empty);
            command.Parameters.AddWithValue("$help", field.HelpText ?? string.Empty);
            command.Parameters.AddWithValue("$formula", field.Formula ?? string.Empty);
            command.Parameters.AddWithValue("$isExpense", field.IsExpense ? 1 : 0);
            command.Parameters.AddWithValue("$totalInList", field.TotalInList ? 1 : 0);
            command.Parameters.AddWithValue("$sort", field.SortOrder);
            command.Parameters.AddWithValue("$showInList", field.ShowInList ? 1 : 0);

            if (field.Id == 0)
                field.Id = Convert.ToInt64(command.ExecuteScalar());
            else
                command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Deletes a record type. Refuses if any record still uses it, including records
    /// sitting in the recycle bin, so nothing is silently destroyed.
    /// </summary>
    public bool TryDelete(long id, out string reason)
    {
        using var connection = _database.Open();

        using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM record WHERE record_type_id = $id";
            count.Parameters.AddWithValue("$id", id);
            var used = Convert.ToInt32(count.ExecuteScalar());
            if (used > 0)
            {
                reason = used == 1
                    ? "1 record still uses this type. Delete or move that record first, and empty the recycle bin."
                    : $"{used} records still use this type. Delete or move those records first, and empty the recycle bin.";
                return false;
            }
        }

        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM record_type WHERE id = $id";
        delete.Parameters.AddWithValue("$id", id);
        delete.ExecuteNonQuery();

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// A free name based on <paramref name="stem"/>, adding " 2", " 3" and so on until
    /// one is unused. Lets "copy of" and "new category" always succeed rather than
    /// bouncing the user back with a name clash.
    /// </summary>
    public string SuggestName(string stem)
    {
        var trimmed = string.IsNullOrWhiteSpace(stem) ? "New category" : stem.Trim();

        if (!NameExists(trimmed)) return trimmed;

        for (var counter = 2; counter < 1000; counter++)
        {
            var candidate = $"{trimmed} {counter}";
            if (!NameExists(candidate)) return candidate;
        }

        return $"{trimmed} {Guid.NewGuid().ToString("N")[..6]}";
    }

    public bool NameExists(string name, long exceptId = 0)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM record_type WHERE name = $name COLLATE NOCASE AND id <> $id";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$id", exceptId);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static List<FieldDef> LoadFields(SqliteConnection connection, long recordTypeId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, record_type_id, name, data_type, is_required, choices,
                   default_value, help_text, sort_order, show_in_list, formula, is_expense, total_in_list
            FROM field_def WHERE record_type_id = $type ORDER BY sort_order, id";
        command.Parameters.AddWithValue("$type", recordTypeId);

        var fields = new List<FieldDef>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) fields.Add(ReadField(reader));
        return fields;
    }

    private static Dictionary<long, List<FieldDef>> LoadAllFields(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT id, record_type_id, name, data_type, is_required, choices,
                   default_value, help_text, sort_order, show_in_list, formula, is_expense, total_in_list
            FROM field_def ORDER BY record_type_id, sort_order, id";

        var map = new Dictionary<long, List<FieldDef>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var field = ReadField(reader);
            if (!map.TryGetValue(field.RecordTypeId, out var list))
                map[field.RecordTypeId] = list = new List<FieldDef>();
            list.Add(field);
        }
        return map;
    }

    private static FieldDef ReadField(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        RecordTypeId = reader.GetInt64(1),
        Name = reader.GetString(2),
        DataType = (FieldDataType)reader.GetInt32(3),
        IsRequired = reader.GetInt32(4) != 0,
        Choices = reader.GetString(5),
        DefaultValue = reader.GetString(6),
        HelpText = reader.GetString(7),
        SortOrder = reader.GetInt32(8),
        ShowInList = reader.GetInt32(9) != 0,
        Formula = reader.GetString(10),
        IsExpense = reader.GetInt32(11) != 0,
        TotalInList = reader.GetInt32(12) != 0
    };

    internal static DateTime ParseUtc(string value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.UtcNow;
}
