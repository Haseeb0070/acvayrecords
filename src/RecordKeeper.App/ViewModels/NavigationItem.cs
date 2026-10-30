using System.Globalization;
using System.Windows;
using RecordKeeper.App.Mvvm;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.ViewModels;

public enum NavigationKind
{
    AllRecords,
    Group,
    RecordType,
    Favorites,
    RecycleBin
}

/// <summary>One entry in the left-hand navigation list.</summary>
public sealed class NavigationItem : ObservableObject
{
    private int _count;

    public required NavigationKind Kind { get; init; }
    public required string Name { get; init; }

    /// <summary>Null for the built-in entries such as Favourites and the recycle bin.</summary>
    public RecordType? RecordType { get; init; }

    /// <summary>Set on a group row, and on a category that sits inside one.</summary>
    public CategoryGroup? Group { get; init; }

    public long? RecordTypeId => RecordType?.Id;

    public long? GroupId => Kind == NavigationKind.Group ? Group?.Id : null;

    /// <summary>Categories sit one step in from the group heading above them.</summary>
    public int Indent { get; init; }

    public Thickness IndentMargin => new(Indent * 18, 0, 0, 0);

    public bool IsGroup => Kind == NavigationKind.Group;

    /// <summary>A number key that jumps straight here, shown as "Alt + 3".</summary>
    public int AltNumber { get; set; }

    /// <summary>
    /// The group's expense grand total, or null when nothing in it is an expense.
    /// Null and zero are not the same: null means no field has been marked.
    /// </summary>
    public decimal? ExpenseTotal { get; set; }

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(AccessibleName));
                OnPropertyChanged(nameof(CountLabel));
            }
        }
    }

    public string CountLabel => Count.ToString();

    /// <summary>The expense total under a group's name, or empty.</summary>
    public string ExpenseLabel => ExpenseTotal is null
        ? string.Empty
        : "Expenses " + Money.Text(ExpenseTotal.Value);

    public bool HasExpenseTotal => ExpenseTotal is not null;

    /// <summary>
    /// The line a screen reader reads. Saying the count here means the user knows
    /// whether a category is worth opening before they open it, and a group says its
    /// running expense total without anything having to be opened at all.
    /// </summary>
    public string AccessibleName
    {
        get
        {
            var noun = Count == 1 ? "record" : "records";
            var shortcut = AltNumber > 0 ? $" Shortcut Alt plus {AltNumber}." : string.Empty;

            return Kind switch
            {
                NavigationKind.RecycleBin => $"Recycle bin, {Count} deleted {noun}.{shortcut}",
                NavigationKind.Favorites => $"Favourites, {Count} {noun}.{shortcut}",
                NavigationKind.AllRecords => $"All records, {Count} {noun}.{shortcut}",

                NavigationKind.Group => ExpenseTotal is null
                    ? $"Group {Name}, {Count} {noun}. No expense total, because no field in it is marked as an expense.{shortcut}"
                    : $"Group {Name}, {Count} {noun}, expenses totalling " +
                      $"{Money.Spoken(ExpenseTotal.Value)}.{shortcut}",

                _ => Group is null
                    ? $"{Name}, {Count} {noun}.{shortcut}"
                    : $"{Name}, in {Group.Name}, {Count} {noun}.{shortcut}"
            };
        }
    }

    public override string ToString() => Name;
}
