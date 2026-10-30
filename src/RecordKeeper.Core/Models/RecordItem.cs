using System.Globalization;

namespace RecordKeeper.Core.Models;

/// <summary>A single stored record.</summary>
public sealed class RecordItem
{
    public long Id { get; set; }
    public long RecordTypeId { get; set; }
    public string RecordTypeName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    /// <summary>Canonical field values keyed by <see cref="FieldDef.Id"/>.</summary>
    public Dictionary<long, string> Values { get; set; } = new();

    public List<string> Tags { get; set; } = new();
    public int AttachmentCount { get; set; }

    public string GetValue(long fieldId) =>
        Values.TryGetValue(fieldId, out var v) ? v : string.Empty;

    public override string ToString() => Title;
}

/// <summary>A file copied into the local attachment vault and linked to a record.</summary>
public sealed class Attachment
{
    public long Id { get; set; }
    public long RecordId { get; set; }

    /// <summary>The file name as the user originally knew it.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Location inside the vault, relative to the data root.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime AddedUtc { get; set; }

    /// <summary>Size in words, for the screen reader; "1.4 megabytes" reads better than "1468006 bytes".</summary>
    public string SizeSpoken
    {
        get
        {
            double b = SizeBytes;
            if (b < 1024) return $"{SizeBytes} bytes";
            if (b < 1024 * 1024) return string.Format(CultureInfo.CurrentCulture, "{0:0.#} kilobytes", b / 1024);
            if (b < 1024L * 1024 * 1024) return string.Format(CultureInfo.CurrentCulture, "{0:0.#} megabytes", b / (1024 * 1024));
            return string.Format(CultureInfo.CurrentCulture, "{0:0.##} gigabytes", b / (1024L * 1024 * 1024));
        }
    }

    public override string ToString() => FileName;
}

/// <summary>Criteria for listing records.</summary>
public sealed class RecordQuery
{
    public long? RecordTypeId { get; set; }

    /// <summary>Limits the query to every category inside one group.</summary>
    public long? GroupId { get; set; }
    public string SearchText { get; set; } = string.Empty;
    public string? Tag { get; set; }
    public bool OnlyFavorites { get; set; }

    /// <summary>When true the query returns records in the recycle bin instead of live ones.</summary>
    public bool ShowDeleted { get; set; }

    public string SortColumn { get; set; } = "Title";
    public bool SortDescending { get; set; }

    /// <summary>
    /// One of the user's own fields to sort by, instead of <see cref="SortColumn"/>.
    /// Records with nothing in that field always come last, whichever way round the
    /// sort runs: "not filled in" is not a small number and not an early date.
    /// </summary>
    public long? SortFieldId { get; set; }

    /// <summary>True when <see cref="SortFieldId"/> holds a number or an amount.</summary>
    public bool SortFieldIsNumeric { get; set; }

    /// <summary>
    /// The date field a period filter applies to. Null means the date the record was
    /// first saved, which is the only date a mixed list of categories has in common.
    /// </summary>
    public long? DateFieldId { get; set; }

    /// <summary>Start of the period, inclusive. Local dates, not times.</summary>
    public DateTime? From { get; set; }

    /// <summary>End of the period, inclusive of the whole of that day.</summary>
    public DateTime? To { get; set; }
}

/// <summary>What a set of records adds up to, keyed by field.</summary>
public sealed class RecordTotals
{
    /// <summary>The sum of each numeric field, for the fields that have one.</summary>
    public Dictionary<long, decimal> Sums { get; } = new();

    /// <summary>How many records contributed to each sum.</summary>
    public Dictionary<long, int> Counts { get; } = new();

    public bool IsEmpty => Sums.Count == 0;
}

/// <summary>What re-applying the formulas changed.</summary>
public sealed class RecalculationResult
{
    public int RecordsChecked { get; set; }
    public int RecordsChanged { get; set; }
    public int ValuesChanged { get; set; }
}
