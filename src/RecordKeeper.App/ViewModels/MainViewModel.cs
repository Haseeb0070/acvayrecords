using System.Collections.ObjectModel;
using System.Globalization;
using RecordKeeper.App.Mvvm;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.ViewModels;

/// <summary>
/// Holds the application's state: what is selected on the left, which records are
/// listed, and whether the main area is showing the list or the record editor.
///
/// This class deliberately opens no dialogs and moves no focus. Those belong to the
/// window, which can do them accessibly; keeping them out of here means the state
/// transitions stay simple enough to reason about.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    private NavigationItem? _selectedNavigation;
    private RecordRowViewModel? _selectedRecord;
    private string _searchText = string.Empty;
    private RecordViewMode _viewMode;
    private RecordEditorViewModel? _editor;
    private bool _isLoading;

    public MainViewModel(AppServices services)
    {
        _services = services;
        _viewMode = services.Settings.DefaultViewMode;
        _period = PeriodOptions[0];

        ReloadNavigation();
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    public ObservableCollection<RecordRowViewModel> Records { get; } = new();

    public List<RecordType> RecordTypes { get; private set; } = new();

    public List<CategoryGroup> Groups { get; private set; } = new();

    /// <summary>The columns the table view should show for whatever is selected.</summary>
    public List<FieldDef> TableColumns { get; private set; } = new();

    /// <summary>Raised when the table view needs its columns rebuilt.</summary>
    public event EventHandler? TableColumnsChanged;

    /// <summary>Raised when the record list has been reloaded, so the view can restore focus.</summary>
    public event EventHandler? RecordsReloaded;

    /// <summary>
    /// Asked before the selection moves away from an open record editor.
    ///
    /// Returning false leaves the selection where it was. The window supplies this,
    /// because the question is a dialog and this class deliberately opens none; what
    /// belongs here is only the rule that the question has to be asked at all.
    /// </summary>
    public Func<NavigationItem?, bool>? ConfirmLeavingEditor { get; set; }

    /// <summary>True while the application itself is moving the selection.</summary>
    private bool _movingSelectionInternally;

    public NavigationItem? SelectedNavigation
    {
        get => _selectedNavigation;
        set
        {
            if (ReferenceEquals(_selectedNavigation, value)) return;

            // Clicking another category with a half-written record open must not throw
            // it away without asking. Only the user's own moves are questioned: a
            // reload moving the selection back to where it already was is not leaving.
            if (!_movingSelectionInternally && IsEditing &&
                ConfirmLeavingEditor is { } ask && !ask(value))
            {
                // The list has already moved its own highlight, and it follows this
                // property, so saying it did not change puts the highlight back.
                OnPropertyChanged(nameof(SelectedNavigation));
                return;
            }

            if (!SetProperty(ref _selectedNavigation, value)) return;

            if (value?.RecordTypeId is { } typeId)
                _services.Settings.LastRecordTypeId = typeId;

            RebuildListOptions();
            RebuildTableColumns();
            LoadRecords();

            OnPropertyChanged(nameof(ListHeading));
            OnPropertyChanged(nameof(IsRecycleBin));
            OnPropertyChanged(nameof(CanAddRecord));
            OnPropertyChanged(nameof(CurrentRecordType));
            OnPropertyChanged(nameof(NewRecordChoices));
            OnPropertyChanged(nameof(NewRecordNeedsChoice));
            OnPropertyChanged(nameof(NewRecordHelpText));
            OnPropertyChanged(nameof(CannotAddRecordReason));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            LoadRecords();
            OnPropertyChanged(nameof(IsSearching));
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    public RecordRowViewModel? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (!SetProperty(ref _selectedRecord, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionPositionText));
        }
    }

    public bool HasSelection => SelectedRecord is not null;

    /// <summary>"Record 4 of 27", kept available so the user can ask where they are.</summary>
    public string SelectionPositionText
    {
        get
        {
            if (SelectedRecord is null) return Records.Count == 0 ? "No records" : "Nothing selected";
            var index = Records.IndexOf(SelectedRecord) + 1;
            return $"Record {index} of {Records.Count}";
        }
    }

    public RecordViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (!SetProperty(ref _viewMode, value)) return;
            OnPropertyChanged(nameof(IsTableView));
            OnPropertyChanged(nameof(IsFormView));
            OnPropertyChanged(nameof(ShowListView));
            OnPropertyChanged(nameof(ShowTableView));
            OnPropertyChanged(nameof(ViewModeName));
            OnPropertyChanged(nameof(ViewToggleLabel));
            OnPropertyChanged(nameof(ViewToggleHelp));
        }
    }

    public bool IsTableView => ViewMode == RecordViewMode.Table;
    public bool IsFormView => ViewMode == RecordViewMode.Form;

    // The list, the table and the empty-state panel are mutually exclusive, so each
    // gets its own flag rather than the view combining conditions in a binding.
    public bool ShowListView => IsFormView && HasRecords;
    public bool ShowTableView => IsTableView && HasRecords;

    public string ViewModeName => IsTableView ? "Table view" : "List view";

    /// <summary>
    /// What the toggle button offers, which is always the view you are not in.
    /// This was a menu item with a shortcut and nothing on screen, so the table view
    /// was effectively undiscoverable.
    /// </summary>
    public string ViewToggleLabel => IsTableView ? "List view" : "Table view";

    public string ViewToggleHelp => IsTableView
        ? "Switch to list view, where each record is one item read out as a sentence. Shortcut Control plus Shift plus V."
        : "Switch to table view, where each field is a column. Shortcut Control plus Shift plus V.";

    // ------------------------------------------- showing and hiding the two panels

    /// <summary>
    /// Whether the sorting and period controls are on show.
    ///
    /// Kept in the settings rather than reset on every launch: someone who hides them
    /// should not have to hide them again tomorrow.
    /// </summary>
    public bool ShowSortControls
    {
        get => _services.Settings.ShowSortControls;
        set
        {
            if (_services.Settings.ShowSortControls == value) return;

            _services.Settings.ShowSortControls = value;
            _services.PersistSettings();

            OnPropertyChanged(nameof(ShowSortControls));
            OnPropertyChanged(nameof(SortToggleLabel));
            OnPropertyChanged(nameof(SortToggleHelp));
        }
    }

    public bool ShowTotalsPanel
    {
        get => _services.Settings.ShowTotalsPanel;
        set
        {
            if (_services.Settings.ShowTotalsPanel == value) return;

            _services.Settings.ShowTotalsPanel = value;
            _services.PersistSettings();

            OnPropertyChanged(nameof(ShowTotalsPanel));
            OnPropertyChanged(nameof(TotalsPanelVisible));
            OnPropertyChanged(nameof(ShowTotalsHint));
            OnPropertyChanged(nameof(TotalsToggleLabel));
            OnPropertyChanged(nameof(TotalsToggleHelp));
        }
    }

    /// <summary>The panel is on screen only when it is turned on and has something in it.</summary>
    public bool TotalsPanelVisible => ShowTotalsPanel && Totals.Count > 0;

    // Each button says what pressing it will do, like the view button beside them.
    public string SortToggleLabel => ShowSortControls ? "Hide sort and period" : "Sort and period";

    public string SortToggleHelp => ShowSortControls
        ? "Hides the sorting and period controls. The order and the period you chose stay as they are."
        : "Shows the controls for the order of the list and the period it covers.";

    public string TotalsToggleLabel => ShowTotalsPanel ? "Hide totals" : "Show totals";

    public string TotalsToggleHelp => ShowTotalsPanel
        ? "Hides the totals panel. The totals are still read out with the record count."
        : "Shows what the listed records add up to, above the list.";

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public RecordEditorViewModel? Editor
    {
        get => _editor;
        private set
        {
            if (!SetProperty(ref _editor, value)) return;
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(IsListVisible));
        }
    }

    /// <summary>
    /// The main area shows either the list or the editor, never both.
    ///
    /// Side-by-side panes are fine at normal font sizes but fall apart at 300% zoom,
    /// and they force a screen-reader user to work out which pane they are in. One
    /// thing at a time is both simpler to magnify and simpler to hear.
    /// </summary>
    public bool IsEditing => Editor is not null;

    public bool IsListVisible => Editor is null;

    public bool IsRecycleBin => SelectedNavigation?.Kind == NavigationKind.RecycleBin;

    /// <summary>
    /// The categories a new record could go into, given what is selected on the left.
    ///
    /// A group holds categories and All records covers every one, so a new record is
    /// perfectly possible on those views — the user is simply asked which category it
    /// belongs to. Favourites and the recycle bin offer none, because a brand-new
    /// record is neither starred nor deleted, so it could not belong to either.
    /// </summary>
    public List<RecordType> NewRecordChoices
    {
        get
        {
            var nav = SelectedNavigation;
            if (nav is null) return new List<RecordType>();

            return nav.Kind switch
            {
                NavigationKind.RecordType => nav.RecordType is null
                    ? new List<RecordType>()
                    : new List<RecordType> { nav.RecordType },
                NavigationKind.Group => RecordTypes.Where(t => t.GroupId == nav.GroupId).ToList(),
                NavigationKind.AllRecords => RecordTypes.ToList(),
                _ => new List<RecordType>()
            };
        }
    }

    /// <summary>
    /// True when a new record can really be started from here.
    ///
    /// The New record button is hidden when this is false. A button that answers a
    /// click by explaining why it cannot work is still a button that did nothing, and
    /// for a keyboard user it is worse than that: it sits in the tab order promising
    /// something it never delivers.
    /// </summary>
    public bool CanAddRecord => NewRecordChoices.Count > 0;

    /// <summary>True when the category still has to be chosen, so the user is asked.</summary>
    public bool NewRecordNeedsChoice => NewRecordChoices.Count > 1;

    /// <summary>What the New record button promises, so that it never surprises anyone.</summary>
    public string NewRecordHelpText
    {
        get
        {
            var choices = NewRecordChoices;

            if (choices.Count == 1)
                return $"Adds a record to {choices[0].Name}. Shortcut Control plus N.";

            if (choices.Count > 1)
                return $"Asks which of {ChoiceCountPhrase(choices.Count)} the new record " +
                       "belongs to, then adds it. Shortcut Control plus N.";

            return "Adds a record to the selected category. Shortcut Control plus N.";
        }
    }

    /// <summary>
    /// "the 3 categories in Online trading" on a group, and plain "the 3 categories"
    /// elsewhere, because "the 3 categories in All records" names nothing useful.
    /// </summary>
    private string ChoiceCountPhrase(int count) =>
        SelectedNavigation?.Kind == NavigationKind.Group
            ? $"the {count} categories in {ListHeading}"
            : $"the {count} categories";

    /// <summary>Why a new record cannot be started from here, and what to do instead.</summary>
    public string CannotAddRecordReason => SelectedNavigation?.Kind switch
    {
        NavigationKind.RecycleBin =>
            "The recycle bin only holds records you have deleted, so a new one cannot be " +
            "added here. Choose a category on the left first. Press F6 to reach the list.",

        NavigationKind.Favorites =>
            "Favourites only lists records you have starred, so a new one cannot be added " +
            "here. Choose a category on the left, add the record there, then press Control " +
            "plus D to star it. Press F6 to reach the list.",

        NavigationKind.Group =>
            $"{ListHeading} has no categories in it yet, so a new record would have no " +
            "fields to fill in. Put a category into this group from Categories and their " +
            "fields, which is Control plus T.",

        _ =>
            "There are no categories yet, so a new record would have no fields to fill in. " +
            "Make a category first with Control plus Shift plus N."
    };

    /// <summary>The selected category, or null on a group, All records or the bin.</summary>
    public RecordType? CurrentRecordType => SelectedNavigation?.RecordType;

    public string ListHeading => SelectedNavigation?.Name ?? "Records";

    // ----------------------------------------------------------------- group totals

    private string _groupExpenseHeadline = string.Empty;
    private string _groupExpenseDetail = string.Empty;

    /// <summary>True while a group is selected, so the view can show its total.</summary>
    public bool ShowGroupSummary => SelectedNavigation?.Kind == NavigationKind.Group;

    /// <summary>The group's expense grand total, worded for the panel and for speech.</summary>
    public string GroupExpenseHeadline => _groupExpenseHeadline;

    /// <summary>Which fields the total is made of, so the number can be checked.</summary>
    public string GroupExpenseDetail => _groupExpenseDetail;

    /// <summary>
    /// Works out the wording for the selected group's expense total.
    ///
    /// A total with no explanation is hard to trust, so the fields feeding it are named.
    /// </summary>
    private void RefreshGroupSummary()
    {
        var group = SelectedNavigation?.Group;

        if (SelectedNavigation?.Kind != NavigationKind.Group || group is null)
        {
            _groupExpenseHeadline = string.Empty;
            _groupExpenseDetail = string.Empty;
            return;
        }

        if (group.ExpenseTotal is null)
        {
            _groupExpenseHeadline = "No expenses are being totalled for this group";
            _groupExpenseDetail =
                "No field in any of its categories is marked as an expense. " +
                "Open Categories and their fields, edit a number or money field, and tick " +
                "\"Count this towards the group expense total\".";
            return;
        }

        var fields = _services.Groups.GetExpenseFields(group.Id);

        _groupExpenseHeadline =
            $"Total expenses for {group.Name}: " +
            Money.Text(group.ExpenseTotal.Value);

        _groupExpenseDetail = fields.Count switch
        {
            0 => string.Empty,
            1 => $"Added up from {fields[0].Field} on {fields[0].Category}, across " +
                 $"{group.RecordCount} {(group.RecordCount == 1 ? "record" : "records")}.",
            _ => "Added up from " +
                 string.Join(", ", fields.Select(f => $"{f.Field} on {f.Category}")) +
                 $", across {group.RecordCount} {(group.RecordCount == 1 ? "record" : "records")}."
        };
    }

    public bool HasRecords => Records.Count > 0;

    /// <summary>True when the list has nothing in it, so the view can show a welcome panel.</summary>
    public bool IsEmptyList => Records.Count == 0;

    /// <summary>
    /// Heading for the panel shown in place of an empty list. An empty list with no
    /// explanation is the least helpful thing a new user can be shown.
    /// </summary>
    public string EmptyStateHeading
    {
        get
        {
            if (IsSearching) return "Nothing matches that search";
            if (IsRecycleBin) return "The recycle bin is empty";
            if (SelectedNavigation?.Kind == NavigationKind.Favorites) return "Nothing is starred yet";
            return $"No {ListHeading.ToLowerInvariant()} records yet";
        }
    }

    public string EmptyStateHint
    {
        get
        {
            if (IsSearching)
                return $"Nothing has \"{SearchText}\" in its title, its fields, its notes or its tags. " +
                       "Press Control plus Shift plus C to clear the search and see everything again.";

            if (IsRecycleBin)
                return "Records you delete land here, and can be put back from here. " +
                       "Nothing is destroyed until you empty the bin.";

            var choices = NewRecordChoices;

            // Saying "press Control plus N" on a view where Control plus N cannot work
            // is how a user learns not to trust what the app tells them.
            if (choices.Count == 0) return CannotAddRecordReason;

            if (choices.Count > 1)
                return "Press Control plus N, or use the New record button, to add one. " +
                       $"It will ask which of {ChoiceCountPhrase(choices.Count)} it belongs to.";

            return "Press Control plus N, or use the New record button, to add your first one. " +
                   $"It will ask you for the {choices[0].EffectiveTitleLabel.ToLowerInvariant()} first.";
        }
    }

    /// <summary>The sentence announced after the list reloads.</summary>
    /// <summary>
    /// The short version shown under the heading: what is listed and how many.
    ///
    /// The spoken summary carries the period and the totals as well, because someone
    /// listening cannot glance at the panel below. On screen that panel is already
    /// there, and repeating it in a paragraph above only pushes the records down.
    /// </summary>
    public string ListCountLine
    {
        get
        {
            var noun = Records.Count == 1 ? "record" : "records";

            if (IsSearching)
            {
                return Records.Count == 0
                    ? $"No records match {SearchText}."
                    : $"{Records.Count} {noun} match {SearchText}.";
            }

            if (Records.Count == 0) return ListSummary;

            return $"{ListHeading}, {Records.Count} {noun}.";
        }
    }

    public string ListSummary
    {
        get
        {
            var noun = Records.Count == 1 ? "record" : "records";

            // What the records add up to is said with the count, every time the count
            // is said. Having to go and look for the number defeats the point of it.
            var sums = TotalsSentence.Length > 0 ? " " + TotalsSentence : string.Empty;

            if (IsSearching)
            {
                return Records.Count == 0
                    ? $"No records match {SearchText}."
                    : $"{Records.Count} {noun} match {SearchText}.{sums}";
            }

            if (Records.Count == 0)
            {
                if (IsRecycleBin) return "The recycle bin is empty.";

                if (CurrentRange() is { From: not null } or { To: not null })
                    return $"{ListHeading} has no records in that period.";

                return CanAddRecord
                    ? $"{ListHeading} has no records yet. Press Control plus N to add one."
                    : $"{ListHeading} has no records yet.";
            }

            // A group announces its running expense total along with its count, so the
            // number is heard without having to go looking for it.
            var total = ShowGroupSummary && GroupExpenseHeadline.Length > 0
                ? " " + GroupExpenseHeadline + "."
                : string.Empty;

            var period = CurrentRange() is { From: not null } or { To: not null }
                ? " " + PeriodDescription
                : string.Empty;

            return $"{ListHeading}, {Records.Count} {noun}.{period}{total}{sums}";
        }
    }

    // ----------------------------------------------------------------- navigation

    public void ReloadNavigation()
    {
        var previousKind = SelectedNavigation?.Kind;
        var previousTypeId = SelectedNavigation?.RecordTypeId;
        var previousGroupId = SelectedNavigation?.GroupId;

        RecordTypes = _services.RecordTypes.GetAll();
        Groups = _services.Groups.GetAll();

        NavigationItems.Clear();

        var all = new NavigationItem
        {
            Kind = NavigationKind.AllRecords,
            Name = "All records",
            Count = RecordTypes.Sum(t => t.RecordCount)
        };
        NavigationItems.Add(all);

        // Each group as a heading row carrying its own expense total, with its
        // categories stepped in underneath it.
        foreach (var group in Groups)
        {
            NavigationItems.Add(new NavigationItem
            {
                Kind = NavigationKind.Group,
                Name = group.Name,
                Group = group,
                Count = group.RecordCount,
                ExpenseTotal = group.ExpenseTotal
            });

            foreach (var type in RecordTypes.Where(t => t.GroupId == group.Id))
            {
                NavigationItems.Add(new NavigationItem
                {
                    Kind = NavigationKind.RecordType,
                    Name = type.Name,
                    RecordType = type,
                    Group = group,
                    Indent = 1,
                    Count = type.RecordCount
                });
            }
        }

        // Categories that belong to no group follow, at the outer level. They are not
        // given an "Ungrouped" heading, because that heading would be selectable and
        // would look like a group that can hold a total, which it cannot.
        foreach (var type in RecordTypes.Where(t => t.GroupId is null))
        {
            NavigationItems.Add(new NavigationItem
            {
                Kind = NavigationKind.RecordType,
                Name = type.Name,
                RecordType = type,
                Count = type.RecordCount
            });
        }

        NavigationItems.Add(new NavigationItem
        {
            Kind = NavigationKind.Favorites,
            Name = "Favourites",
            Count = _services.Records.CountFavorites()
        });

        NavigationItems.Add(new NavigationItem
        {
            Kind = NavigationKind.RecycleBin,
            Name = "Recycle bin",
            Count = _services.Records.CountInRecycleBin()
        });

        // Alt plus a digit jumps to the first nine entries.
        for (var i = 0; i < NavigationItems.Count && i < 9; i++)
            NavigationItems[i].AltNumber = i + 1;

        RestoreSelection(previousKind, previousTypeId, previousGroupId);
    }

    private void RestoreSelection(NavigationKind? previousKind, long? previousTypeId, long? previousGroupId)
    {
        NavigationItem? target = null;

        if (previousKind == NavigationKind.RecordType && previousTypeId is not null)
            target = NavigationItems.FirstOrDefault(n => n.RecordTypeId == previousTypeId);
        else if (previousKind == NavigationKind.Group && previousGroupId is not null)
            target = NavigationItems.FirstOrDefault(n => n.GroupId == previousGroupId);
        else if (previousKind is not null)
            target = NavigationItems.FirstOrDefault(n => n.Kind == previousKind);
        else if (_services.Settings.LastRecordTypeId is { } remembered)
            target = NavigationItems.FirstOrDefault(n => n.RecordTypeId == remembered);

        // Rebuilding the list makes new items for the same categories, so putting the
        // selection back is the application's own move, not the user leaving an open
        // record. Saving a record reloads, and must not ask whether to save it.
        _movingSelectionInternally = true;
        try
        {
            SelectedNavigation = target ?? NavigationItems.FirstOrDefault();
        }
        finally
        {
            _movingSelectionInternally = false;
        }
    }

    public void SelectNavigationByNumber(int altNumber)
    {
        var item = NavigationItems.FirstOrDefault(n => n.AltNumber == altNumber);
        if (item is not null) SelectedNavigation = item;
    }

    /// <summary>
    /// Selects whichever entry now stands for the one given, after the list has been
    /// rebuilt and the original object no longer exists.
    /// </summary>
    public void SelectNavigationLike(NavigationItem? wanted)
    {
        if (wanted is null) return;

        var match = NavigationItems.FirstOrDefault(n =>
            n.Kind == wanted.Kind &&
            n.RecordTypeId == wanted.RecordTypeId &&
            n.GroupId == wanted.GroupId);

        if (match is not null) SelectedNavigation = match;
    }

    // ----------------------------------------------------------------- records

    // ------------------------------------------------ sorting, period and totals

    private SortOption? _sort;
    private bool _sortDescending;
    private PeriodOption _period;
    private DateFieldOption? _dateField;
    private DateTime? _customFrom;
    private DateTime? _customTo;
    private string _totalsHeading = string.Empty;
    private string _totalsHint = string.Empty;

    public ObservableCollection<SortOption> SortOptions { get; } = new();
    public ObservableCollection<DateFieldOption> DateFieldOptions { get; } = new();

    /// <summary>What the listed records add up to, one line per numeric field.</summary>
    public ObservableCollection<TotalLine> Totals { get; } = new();

    public IReadOnlyList<PeriodOption> PeriodOptions { get; } = new[]
    {
        new PeriodOption { Kind = PeriodKind.All,       Name = "All dates" },
        new PeriodOption { Kind = PeriodKind.Today,     Name = "Today" },
        new PeriodOption { Kind = PeriodKind.ThisMonth, Name = "This month" },
        new PeriodOption { Kind = PeriodKind.LastMonth, Name = "Last month" },
        new PeriodOption { Kind = PeriodKind.ThisYear,  Name = "This year" },
        new PeriodOption { Kind = PeriodKind.Custom,    Name = "Between two dates" }
    };

    public SortOption? SelectedSort
    {
        get => _sort;
        set
        {
            if (!SetProperty(ref _sort, value)) return;

            OnPropertyChanged(nameof(SortOrderLabel));
            OnPropertyChanged(nameof(SortOrderHelp));
            LoadRecords();
        }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (!SetProperty(ref _sortDescending, value)) return;

            OnPropertyChanged(nameof(SortOrderLabel));
            OnPropertyChanged(nameof(SortOrderHelp));
            LoadRecords();
        }
    }

    /// <summary>
    /// The button shows the order the list is in now, rather than the order pressing
    /// it would give. A control that reads as "Largest first" while the list is in the
    /// opposite order is a control that cannot be trusted without looking.
    /// </summary>
    public string SortOrderLabel => _sort is null
        ? "A to Z"
        : _sortDescending ? _sort.DescendingName : _sort.AscendingName;

    public string SortOrderHelp
    {
        get
        {
            if (_sort is null) return "Changes the order of the list.";

            var other = _sortDescending ? _sort.AscendingName : _sort.DescendingName;
            return $"The list is in order of {_sort.Name}, {SortOrderLabel.ToLowerInvariant()}. " +
                   $"Press to put it in order of {other.ToLowerInvariant()} instead.";
        }
    }

    public PeriodOption SelectedPeriod
    {
        get => _period;
        set
        {
            if (!SetProperty(ref _period, value)) return;

            OnPropertyChanged(nameof(ShowCustomDates));
            LoadRecords();
        }
    }

    /// <summary>Which date the period is measured against; shown only when there is a choice.</summary>
    public DateFieldOption? SelectedDateField
    {
        get => _dateField;
        set
        {
            if (!SetProperty(ref _dateField, value)) return;
            LoadRecords();
        }
    }

    public bool ShowDateFieldChoice => DateFieldOptions.Count > 1;

    public bool ShowCustomDates => SelectedPeriod.IsCustom;

    public DateTime? CustomFrom
    {
        get => _customFrom;
        set
        {
            if (!SetProperty(ref _customFrom, value)) return;
            if (SelectedPeriod.IsCustom) LoadRecords();
        }
    }

    public DateTime? CustomTo
    {
        get => _customTo;
        set
        {
            if (!SetProperty(ref _customTo, value)) return;
            if (SelectedPeriod.IsCustom) LoadRecords();
        }
    }

    /// <summary>True while anything is narrowing the list beyond the category itself.</summary>
    public bool IsFiltered => IsSearching || HasPeriod;

    private bool HasPeriod => CurrentRange() is { From: not null } or { To: not null };

    /// <summary>The period only needs a line of its own when it is narrowing anything.</summary>
    public bool ShowPeriodDescription => HasPeriod;

    /// <summary>
    /// The period in words, including which date it is measured against. A filter the
    /// user cannot see is a filter that makes every number on the screen a mystery.
    /// </summary>
    public string PeriodDescription
    {
        get
        {
            var (from, to) = CurrentRange();
            if (from is null && to is null) return "Showing records of any date.";

            var against = SelectedDateField?.FieldId is null
                ? "the date each record was added"
                : SelectedDateField.Name;

            var range = (from, to) switch
            {
                ({ } f, { } t) when f.Date == t.Date => $"on {Spell(f)}",
                ({ } f, { } t) => $"from {Spell(f)} to {Spell(t)}",
                ({ } f, null) => $"from {Spell(f)} onwards",
                (null, { } t) => $"up to {Spell(t)}",
                _ => string.Empty
            };

            return $"Showing records {range}, by {against}.";
        }
    }

    private static string Spell(DateTime date) =>
        date.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);

    public bool ShowTotals => Totals.Count > 0;

    public string TotalsHeading => _totalsHeading;

    /// <summary>Shown when a category has numbers in it but none is set to be added up.</summary>
    public string TotalsHint => _totalsHint;

    // Someone who has put the totals away does not need telling how to turn one on.
    public bool ShowTotalsHint => _totalsHint.Length > 0 && ShowTotalsPanel;

    /// <summary>The totals alone, said wherever the record count is said.</summary>
    public string TotalsSentence => Totals.Count == 0
        ? string.Empty
        : string.Join(". ", Totals.Select(t => t.AccessibleName)) + ".";

    /// <summary>The totals panel as a screen reader should read it, heading and all.</summary>
    public string TotalsSpoken => Totals.Count == 0
        ? string.Empty
        : _totalsHeading + ". " + TotalsSentence;

    /// <summary>
    /// Rebuilds the sort and period choices for whatever is selected. Called when the
    /// selection changes, and after the categories are edited, because a field that
    /// has been renamed or deleted must not linger in a list of things to sort by.
    /// </summary>
    private void RebuildListOptions()
    {
        var previousSort = _sort?.Name;
        var previousDate = _dateField?.Name;
        var type = SelectedNavigation?.RecordType;

        SortOptions.Clear();

        SortOptions.Add(type is null
            ? new SortOption { Name = "Title", Column = "Title" }
            : new SortOption { Name = type.EffectiveTitleLabel, Column = "Title" });

        if (type is null && SelectedNavigation?.Kind is NavigationKind.AllRecords or NavigationKind.Group)
            SortOptions.Add(new SortOption { Name = "Category", Column = "Type" });

        if (type is not null)
        {
            // Every field except the long notes ones, which have no useful order.
            foreach (var field in type.Fields.Where(f => f.DataType != FieldDataType.LongText))
            {
                SortOptions.Add(new SortOption
                {
                    Name = field.Name,
                    FieldId = field.Id,
                    IsNumeric = FieldDataTypeInfo.IsNumeric(field.DataType),
                    IsDate = field.DataType == FieldDataType.Date
                });
            }
        }

        SortOptions.Add(new SortOption { Name = "Date added", Column = "Created", IsDate = true });
        SortOptions.Add(new SortOption { Name = "Date changed", Column = "Modified", IsDate = true });

        if (IsRecycleBin)
            SortOptions.Add(new SortOption { Name = "Date deleted", Column = "Deleted", IsDate = true });

        DateFieldOptions.Clear();

        if (type is not null)
        {
            foreach (var field in type.Fields.Where(f => f.DataType == FieldDataType.Date))
                DateFieldOptions.Add(new DateFieldOption { Name = field.Name, FieldId = field.Id });
        }

        DateFieldOptions.Add(new DateFieldOption { Name = "Date added" });

        // Keep what the user chose if it still exists, so moving between categories
        // with the same field names does not reset the view under them.
        _sort = SortOptions.FirstOrDefault(o => o.Name == previousSort)
                ?? (IsRecycleBin
                    ? SortOptions.First(o => o.Column == "Deleted")
                    : SortOptions[0]);

        _sortDescending = IsRecycleBin && previousSort is null;

        _dateField = DateFieldOptions.FirstOrDefault(o => o.Name == previousDate) ?? DateFieldOptions[0];

        OnPropertyChanged(nameof(SelectedSort));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(SortOrderLabel));
        OnPropertyChanged(nameof(SortOrderHelp));
        OnPropertyChanged(nameof(SelectedDateField));
        OnPropertyChanged(nameof(ShowDateFieldChoice));
    }

    private (DateTime? From, DateTime? To) CurrentRange() =>
        SelectedPeriod.IsCustom ? (CustomFrom, CustomTo) : SelectedPeriod.Range();

    private RecordQuery BuildQuery()
    {
        var nav = SelectedNavigation!;
        var (from, to) = CurrentRange();

        var query = new RecordQuery
        {
            RecordTypeId = nav.RecordTypeId,
            GroupId = nav.GroupId,
            SearchText = SearchText,
            OnlyFavorites = nav.Kind == NavigationKind.Favorites,
            ShowDeleted = nav.Kind == NavigationKind.RecycleBin,
            From = from,
            To = to,
            DateFieldId = SelectedDateField?.FieldId,
            SortColumn = _sort?.Column ?? "Title",
            SortFieldId = _sort?.FieldId,
            SortFieldIsNumeric = _sort?.IsNumeric ?? false,
            SortDescending = SortDescending
        };

        return query;
    }

    /// <summary>
    /// Adds up every numeric field across the records the query returned.
    ///
    /// The same query object does both jobs, so the total cannot end up describing a
    /// different set of records from the one on screen.
    /// </summary>
    private void RefreshTotals(RecordQuery query, int recordCount)
    {
        Totals.Clear();
        _totalsHeading = string.Empty;
        _totalsHint = string.Empty;

        if (recordCount == 0) return;

        var sums = _services.Records.Totals(query);
        if (sums.IsEmpty) return;

        var mixed = SelectedNavigation?.RecordType is null;

        foreach (var type in RecordTypes)
        {
            foreach (var field in type.Fields)
            {
                if (!field.CountsInListTotals) continue;
                if (!sums.Sums.TryGetValue(field.Id, out var sum)) continue;

                var rounded = field.DataType == FieldDataType.Money
                    ? Math.Round(sum, 2, MidpointRounding.AwayFromZero)
                    : sum;

                var canonical = rounded.ToString(
                    field.DataType == FieldDataType.Money ? "0.00" : "0.##########",
                    CultureInfo.InvariantCulture);

                Totals.Add(new TotalLine
                {
                    // On a group or All records the same field name can appear in two
                    // categories, so the category is named alongside it.
                    FieldName = mixed ? $"{field.Name} ({type.Name})" : field.Name,
                    Amount = FieldValueFormat.ToDisplay(field.DataType, canonical),
                    Spoken = FieldValueFormat.ToSpoken(field.DataType, canonical),
                    Count = sums.Counts.GetValueOrDefault(field.Id),
                    OutOf = recordCount
                });
            }
        }

        if (Totals.Count == 0)
        {
            // Nothing is ticked. Say so once, quietly, rather than leaving someone to
            // wonder why a category full of amounts shows no total at all.
            var couldBeTotalled = SelectedNavigation?.RecordType?.Fields
                .Any(f => FieldDataTypeInfo.IsNumeric(f.DataType)) == true;

            if (couldBeTotalled)
            {
                _totalsHint =
                    "No field here is set to be added up. To total one, press Control plus T, " +
                    "edit a number or money field, and tick \"Add this field up under the " +
                    "record list\".";
            }

            return;
        }

        var noun = recordCount == 1 ? "record" : "records";
        _totalsHeading = IsFiltered
            ? $"Totals for the {recordCount} {noun} shown"
            : $"Totals for all {recordCount} {noun}";
    }

    public void LoadRecords()
    {
        if (SelectedNavigation is null) return;

        IsLoading = true;
        try
        {
            var query = BuildQuery();
            var results = _services.Records.Query(query);
            RefreshTotals(query, results.Count);

            // On a mixed view every record may have a different field set, so the
            // row is built against its own type's fields.
            var fieldsByType = RecordTypes.ToDictionary(t => t.Id, t => t.Fields);
            var showTypeName = SelectedNavigation.RecordType is null;

            var previousId = SelectedRecord?.Id;

            Records.Clear();
            foreach (var record in results)
            {
                var fields = fieldsByType.GetValueOrDefault(record.RecordTypeId, new List<FieldDef>());
                Records.Add(new RecordRowViewModel(record, fields, showTypeName));
            }

            SelectedRecord = Records.FirstOrDefault(r => r.Id == previousId) ?? Records.FirstOrDefault();

            RefreshGroupSummary();

            OnPropertyChanged(nameof(ShowGroupSummary));
            OnPropertyChanged(nameof(GroupExpenseHeadline));
            OnPropertyChanged(nameof(GroupExpenseDetail));
            OnPropertyChanged(nameof(ListSummary));
            OnPropertyChanged(nameof(ListCountLine));
            OnPropertyChanged(nameof(SelectionPositionText));
            OnPropertyChanged(nameof(HasRecords));
            OnPropertyChanged(nameof(IsEmptyList));
            OnPropertyChanged(nameof(ShowListView));
            OnPropertyChanged(nameof(ShowTableView));
            OnPropertyChanged(nameof(EmptyStateHeading));
            OnPropertyChanged(nameof(EmptyStateHint));
            OnPropertyChanged(nameof(ShowTotals));
            OnPropertyChanged(nameof(TotalsPanelVisible));
            OnPropertyChanged(nameof(TotalsHeading));
            OnPropertyChanged(nameof(TotalsHint));
            OnPropertyChanged(nameof(ShowTotalsHint));
            OnPropertyChanged(nameof(TotalsSentence));
            OnPropertyChanged(nameof(TotalsSpoken));
            OnPropertyChanged(nameof(PeriodDescription));
            OnPropertyChanged(nameof(ShowPeriodDescription));
            RecordsReloaded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RebuildTableColumns()
    {
        var type = SelectedNavigation?.RecordType;

        TableColumns = type is null
            ? new List<FieldDef>()
            : type.Fields.Where(f => f.ShowInList).ToList();

        TableColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----------------------------------------------------------------- editing

    /// <summary>
    /// Starts a new record in the given category.
    ///
    /// The caller passes the category because on a group, or on All records, which one
    /// it should be is a question only the user can answer, and asking it is a dialog —
    /// which belongs to the window, not here.
    /// </summary>
    public RecordEditorViewModel? BeginNewRecord(RecordType? type = null)
    {
        type ??= NewRecordChoices.Count == 1 ? NewRecordChoices[0] : null;
        if (type is null) return null;

        Editor = new RecordEditorViewModel(type, existing: null);
        return Editor;
    }

    public RecordEditorViewModel? BeginEditRecord(RecordRowViewModel? row)
    {
        if (row is null) return null;

        var type = RecordTypes.FirstOrDefault(t => t.Id == row.Record.RecordTypeId);
        if (type is null) return null;

        var fresh = _services.Records.GetById(row.Id);
        if (fresh is null) return null;

        var editor = new RecordEditorViewModel(type, fresh);
        foreach (var attachment in _services.Attachments.GetForRecord(fresh.Id))
            editor.Attachments.Add(attachment);
        editor.RefreshAttachmentsHeading();

        Editor = editor;
        return editor;
    }

    public void CloseEditor()
    {
        Editor = null;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsListVisible));
    }

    /// <summary>
    /// Saves the open editor. Returns the saved record id, or zero when validation
    /// failed; the caller is responsible for announcing and for moving focus.
    /// </summary>
    public long SaveEditor()
    {
        if (Editor is null) return 0;

        var record = Editor.ToRecordItem();
        var id = _services.Records.Save(record);

        Editor.AcceptSaved(id);
        ReloadNavigation();
        LoadRecords();
        SelectedRecord = Records.FirstOrDefault(r => r.Id == id);

        return id;
    }

    public List<string> FindDuplicateTitles()
    {
        if (Editor is null || !_services.Settings.WarnOnDuplicateTitle) return new List<string>();

        return _services.Records.FindPossibleDuplicates(
            Editor.RecordType.Id, Editor.Title.Trim(), Editor.RecordId);
    }

    // ----------------------------------------------------------------- deleting

    public int MoveToRecycleBin(IEnumerable<RecordRowViewModel> rows)
    {
        var ids = rows.Select(r => r.Id).ToList();
        if (ids.Count == 0) return 0;

        var moved = _services.Records.MoveToRecycleBin(ids);
        ReloadNavigation();
        LoadRecords();
        return moved;
    }

    public int RestoreFromRecycleBin(IEnumerable<RecordRowViewModel> rows)
    {
        var ids = rows.Select(r => r.Id).ToList();
        if (ids.Count == 0) return 0;

        var restored = _services.Records.Restore(ids);
        ReloadNavigation();
        LoadRecords();
        return restored;
    }

    public int PurgePermanently(IEnumerable<RecordRowViewModel> rows)
    {
        var ids = rows.Select(r => r.Id).ToList();
        if (ids.Count == 0) return 0;

        var orphanedFiles = _services.Records.PurgePermanently(ids);
        AttachmentRepositoryCleanup(orphanedFiles);

        ReloadNavigation();
        LoadRecords();
        return ids.Count;
    }

    public int EmptyRecycleBin()
    {
        var binned = _services.Records.Query(new RecordQuery { ShowDeleted = true });
        if (binned.Count == 0) return 0;

        var orphanedFiles = _services.Records.PurgePermanently(binned.Select(r => r.Id));
        AttachmentRepositoryCleanup(orphanedFiles);

        ReloadNavigation();
        LoadRecords();
        return binned.Count;
    }

    private static void AttachmentRepositoryCleanup(List<string> relativePaths) =>
        Core.Data.AttachmentRepository.DeleteVaultFiles(relativePaths);

    public void ToggleFavorite(RecordRowViewModel? row)
    {
        if (row is null) return;

        _services.Records.SetFavorite(row.Id, !row.IsFavorite);
        ReloadNavigation();
        LoadRecords();
    }

    /// <summary>The records the current action should apply to, in list order.</summary>
    public List<RecordRowViewModel> RecordsForAction(IEnumerable<RecordRowViewModel>? selection)
    {
        var rows = selection?.ToList() ?? new List<RecordRowViewModel>();
        if (rows.Count > 0) return rows;

        return SelectedRecord is null ? new List<RecordRowViewModel>() : new List<RecordRowViewModel> { SelectedRecord };
    }

    /// <summary>The record set an export should cover: whatever the list is showing.</summary>
    public List<RecordItem> RecordsForExport() => Records.Select(r => r.Record).ToList();
}
