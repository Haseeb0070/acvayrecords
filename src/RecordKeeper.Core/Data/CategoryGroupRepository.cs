using System.Globalization;
using RecordKeeper.Core.Models;

namespace RecordKeeper.Core.Data;

/// <summary>
/// Reads and writes category groups, and adds up their expense totals.
/// </summary>
public sealed class CategoryGroupRepository
{
    private readonly Database _database;

    public CategoryGroupRepository(Database database) => _database = database;

    /// <summary>
    /// Every group, with its record count and expense total filled in.
    /// </summary>
    public List<CategoryGroup> GetAll()
    {
        using var connection = _database.Open();

        var groups = new List<CategoryGroup>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT g.id, g.name, g.sort_order, g.created_utc,
                       (SELECT COUNT(*)
                        FROM record r
                        JOIN record_type t ON t.id = r.record_type_id
                        WHERE t.group_id = g.id AND r.is_deleted = 0)
                FROM category_group g
                ORDER BY g.sort_order, g.name COLLATE NOCASE";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                groups.Add(new CategoryGroup
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    SortOrder = reader.GetInt32(2),
                    CreatedUtc = RecordTypeRepository.ParseUtc(reader.GetString(3)),
                    RecordCount = reader.GetInt32(4)
                });
            }
        }

        foreach (var group in groups)
            group.ExpenseTotal = GetExpenseTotal(connection, group.Id);

        return groups;
    }

    public CategoryGroup? GetById(long id) => GetAll().FirstOrDefault(g => g.Id == id);

    public long Save(CategoryGroup group)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();

        if (group.Id == 0)
        {
            command.CommandText = @"
                INSERT INTO category_group(name, sort_order, created_utc)
                VALUES($name, $sort, $now);
                SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        }
        else
        {
            command.CommandText = "UPDATE category_group SET name = $name, sort_order = $sort WHERE id = $id";
            command.Parameters.AddWithValue("$id", group.Id);
        }

        command.Parameters.AddWithValue("$name", group.Name);
        command.Parameters.AddWithValue("$sort", group.SortOrder);

        if (group.Id == 0) group.Id = Convert.ToInt64(command.ExecuteScalar());
        else command.ExecuteNonQuery();

        return group.Id;
    }

    /// <summary>
    /// Deletes a group. Its categories are not deleted: they simply become ungrouped,
    /// which the schema does through ON DELETE SET NULL. Losing a group must never
    /// take records with it.
    /// </summary>
    public void Delete(long id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM category_group WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public bool NameExists(string name, long exceptId = 0)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM category_group WHERE name = $name COLLATE NOCASE AND id <> $id";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$id", exceptId);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public string SuggestName(string stem)
    {
        var trimmed = string.IsNullOrWhiteSpace(stem) ? "New group" : stem.Trim();
        if (!NameExists(trimmed)) return trimmed;

        for (var counter = 2; counter < 1000; counter++)
        {
            var candidate = $"{trimmed} {counter}";
            if (!NameExists(candidate)) return candidate;
        }

        return $"{trimmed} {Guid.NewGuid().ToString("N")[..6]}";
    }

    /// <summary>Puts a category into a group, or takes it out when groupId is null.</summary>
    public void AssignCategory(long recordTypeId, long? groupId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE record_type SET group_id = $group WHERE id = $id";
        command.Parameters.AddWithValue("$group", groupId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$id", recordTypeId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// The grand total of every expense field on every live record in a group.
    ///
    /// Returns null when the group has no expense fields at all, which is different
    /// from a total of zero: null means "nothing here is an expense", zero means "the
    /// expenses add up to nothing".
    /// </summary>
    public decimal? GetExpenseTotal(long groupId)
    {
        using var connection = _database.Open();
        return GetExpenseTotal(connection, groupId);
    }

    private static decimal? GetExpenseTotal(Microsoft.Data.Sqlite.SqliteConnection connection, long groupId)
    {
        // Is anything in this group even marked as an expense?
        using (var marked = connection.CreateCommand())
        {
            marked.CommandText = @"
                SELECT COUNT(*)
                FROM field_def f
                JOIN record_type t ON t.id = f.record_type_id
                WHERE t.group_id = $group AND f.is_expense = 1";
            marked.Parameters.AddWithValue("$group", groupId);

            if (Convert.ToInt32(marked.ExecuteScalar()) == 0) return null;
        }

        using var command = connection.CreateCommand();

        // value_number is the numeric mirror the repository writes alongside the text,
        // so the sum happens in SQLite rather than by parsing every row in memory.
        command.CommandText = @"
            SELECT COALESCE(SUM(v.value_number), 0)
            FROM field_value v
            JOIN field_def f  ON f.id = v.field_def_id AND f.is_expense = 1
            JOIN record r     ON r.id = v.record_id    AND r.is_deleted = 0
            JOIN record_type t ON t.id = r.record_type_id
            WHERE t.group_id = $group AND v.value_number IS NOT NULL";
        command.Parameters.AddWithValue("$group", groupId);

        var result = command.ExecuteScalar();
        if (result is null || result == DBNull.Value) return 0m;

        return Convert.ToDecimal(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The expense fields in a group, named by category, so the total can be explained
    /// rather than just stated.
    /// </summary>
    public List<(string Category, string Field)> GetExpenseFields(long groupId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT t.name, f.name
            FROM field_def f
            JOIN record_type t ON t.id = f.record_type_id
            WHERE t.group_id = $group AND f.is_expense = 1
            ORDER BY t.sort_order, t.name COLLATE NOCASE, f.sort_order";
        command.Parameters.AddWithValue("$group", groupId);

        var fields = new List<(string, string)>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) fields.Add((reader.GetString(0), reader.GetString(1)));
        return fields;
    }
}
