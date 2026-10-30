using RecordKeeper.Core.Models;

namespace RecordKeeper.App.ViewModels;

/// <summary>One entry in the "Sort by" list.</summary>
public sealed class SortOption
{
    public required string Name { get; init; }

    /// <summary>A built-in column such as Title, Created or Modified.</summary>
    public string Column { get; init; } = "Title";

    /// <summary>One of the user's own fields, when the sort is on a field value.</summary>
    public long? FieldId { get; init; }

    public bool IsNumeric { get; init; }
    public bool IsDate { get; init; }

    /// <summary>
    /// What the two directions should be called for this kind of value. "A to Z" means
    /// nothing for an amount, and "Smallest first" means nothing for a name.
    /// </summary>
    public string AscendingName => IsNumeric ? "Smallest first" : IsDate ? "Oldest first" : "A to Z";

    public string DescendingName => IsNumeric ? "Largest first" : IsDate ? "Newest first" : "Z to A";

    public override string ToString() => Name;
}

/// <summary>How a period is chosen in the "Show" list.</summary>
public enum PeriodKind
{
    All,
    Today,
    ThisMonth,
    LastMonth,
    ThisYear,
    Custom
}

/// <summary>One entry in the "Show" list, which narrows the records to a date range.</summary>
public sealed class PeriodOption
{
    public required PeriodKind Kind { get; init; }
    public required string Name { get; init; }

    public bool IsCustom => Kind == PeriodKind.Custom;

    /// <summary>
    /// The range this period covers today, or nulls for "any date". Worked out when it
    /// is asked for rather than stored, so a window left open overnight does not go on
    /// showing yesterday's idea of "this month".
    /// </summary>
    public (DateTime? From, DateTime? To) Range()
    {
        var today = DateTime.Today;

        return Kind switch
        {
            PeriodKind.Today => (today, today),
            PeriodKind.ThisMonth => (First(today), Last(today)),
            PeriodKind.LastMonth => (First(today.AddMonths(-1)), Last(today.AddMonths(-1))),
            PeriodKind.ThisYear => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31)),
            _ => (null, null)
        };
    }

    private static DateTime First(DateTime inMonth) => new(inMonth.Year, inMonth.Month, 1);

    private static DateTime Last(DateTime inMonth) =>
        First(inMonth).AddMonths(1).AddDays(-1);

    public override string ToString() => Name;
}

/// <summary>Which date a period filter should be measured against.</summary>
public sealed class DateFieldOption
{
    public required string Name { get; init; }

    /// <summary>Null means the date the record was first saved.</summary>
    public long? FieldId { get; init; }

    public override string ToString() => Name;
}

/// <summary>One line of the totals panel: a field and what it adds up to.</summary>
public sealed class TotalLine
{
    public required string FieldName { get; init; }
    public required string Amount { get; init; }
    public required string Spoken { get; init; }
    public required int Count { get; init; }
    public required int OutOf { get; init; }

    /// <summary>"from 97 of 100 records", or empty when every record contributed.</summary>
    public string Coverage => Count == OutOf
        ? string.Empty
        : $"from {Count} of {OutOf} {(OutOf == 1 ? "record" : "records")}";

    public string AccessibleName => Coverage.Length == 0
        ? $"{FieldName}, {Spoken}"
        : $"{FieldName}, {Spoken}, {Coverage}";
}
