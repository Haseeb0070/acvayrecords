namespace RecordKeeper.Core.Models;

/// <summary>
/// A named set of categories that belong together, such as "Online trading" or
/// "Offline trading".
///
/// A group is what expense totals are added up over: a field marked as an expense
/// counts towards its group's grand total, whichever category it sits in.
/// </summary>
public sealed class CategoryGroup
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }

    /// <summary>Categories in this group, filled in by the repository.</summary>
    public List<RecordType> Categories { get; set; } = new();

    /// <summary>Live records across every category in the group.</summary>
    public int RecordCount { get; set; }

    /// <summary>
    /// The sum of every expense field on every live record in the group, or null when
    /// the group has no expense fields at all.
    ///
    /// Null and zero mean different things: null is "nothing here is an expense", zero
    /// is "the expenses add up to nothing". Saying "total nil" when no field has been
    /// marked as an expense would be a lie.
    /// </summary>
    public decimal? ExpenseTotal { get; set; }

    public override string ToString() => Name;
}
