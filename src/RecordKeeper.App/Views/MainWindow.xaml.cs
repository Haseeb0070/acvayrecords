using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using RecordKeeper.App.Accessibility;
using RecordKeeper.App.Commands;
using RecordKeeper.App.ViewModels;
using RecordKeeper.Core.Data;
using RecordKeeper.Core.Models;
using RecordKeeper.Core.Services;

namespace RecordKeeper.App.Views;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly Announcer _announcer;
    private readonly MainViewModel _model;
    private readonly CommandRegistry _commands = new();

    /// <summary>
    /// The panes F6 moves between, as (element, spoken name) pairs. Rebuilt whenever
    /// the main area switches between the list and the editor.
    /// </summary>
    private readonly List<(FrameworkElement Element, string Name)> _panes = new();

    private int _paneIndex;

    /// <summary>The narrowest the category list is allowed to be when it is showing.</summary>
    private const double NavigationMinimumWidth = 170;

    /// <summary>The width the category list had when it was last hidden.</summary>
    private double _navigationWidth = 220;

    /// <summary>Stops the worked-out totals being announced twice over with no change.</summary>
    private string _lastAnnouncedTotals = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        _services = AppServices.Current;
        _announcer = _services.Announcer;
        _model = new MainViewModel(_services);

        DataContext = _model;

        _announcer.AttachLiveRegion(StatusText);

        RestoreWindowGeometry();
        ApplyWindowIcon();
        BuildCommandRegistry();
        WireMenu();
        WireButtons();

        _model.TableColumnsChanged += (_, _) => RebuildGridColumns();
        _model.ConfirmLeavingEditor = ConfirmLeavingEditor;
        _model.RecordsReloaded += (_, _) => OnRecordsReloaded();
        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsEditing)) RebuildPaneList();
            if (e.PropertyName is nameof(MainViewModel.SelectionPositionText)
                               or nameof(MainViewModel.SelectedRecord))
            {
                PositionText.Text = _model.SelectionPositionText;
            }
        };

        RecordList.SelectionChanged += (_, _) => PositionText.Text = _model.SelectionPositionText;
        RecordList.MouseDoubleClick += (_, _) => OpenSelectedRecord();
        RecordGrid.MouseDoubleClick += (_, _) => OpenSelectedRecord();

        // Announce a changed total once the user leaves the field that feeds it.
        // Announcing on every keystroke would talk over them as they type.
        FieldsHost.AddHandler(
            LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnFieldLostKeyboardFocus),
            handledEventsToo: true);

        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;

        RebuildGridColumns();
        RebuildPaneList();

        Loaded += OnLoaded;
    }

    private void OnFieldLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var editor = _model.Editor;
        if (editor is null || !editor.HasComputedFields) return;

        var totals = editor.ComputedSummary;
        if (totals.Length == 0 || totals == _lastAnnouncedTotals) return;

        _lastAnnouncedTotals = totals;
        _announcer.Say(totals);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionText.Text = _model.SelectionPositionText;

        // Said once, if the starter record types were installed or tidied up on this launch.
        var seeded = _services.SeedOutcome.Describe();
        if (seeded.Length > 0) seeded += " ";

        if (_services.Settings.NeedsFirstRun)
        {
            _services.Settings.NeedsFirstRun = false;
            _services.SaveSettings();

            _announcer.Say(
                "Welcome to acvayrecords. Everything is stored on this computer only. " +
                seeded +
                "Press F1 for the full list of keyboard shortcuts, or Control plus K to search for a command. " +
                _model.ListSummary);
        }
        else
        {
            _announcer.Say(seeded + _model.ListSummary + " Press F1 for keyboard shortcuts.");
        }

        VisualTreeHelpers.FocusAccessibly(NavList);
    }

    // ===================================================================== commands

    /// <summary>
    /// Registers every command once. The menu, the F1 reference and the Ctrl+K palette
    /// are all driven from this list, so they cannot disagree with each other.
    /// </summary>
    private void BuildCommandRegistry()
    {
        _commands.Add("Records", "New record", "Ctrl + N",
            "Adds a record to the selected category. On a group, or on All records, it asks " +
            "which category the record belongs to first.",
            NewRecord, () => _model.CanAddRecord, "add create");

        _commands.Add("Records", "Open record", "Enter",
            "Opens the selected record so its fields can be changed.", OpenSelectedRecord,
            () => _model.HasSelection, "edit change");

        _commands.Add("Records", "Mark as favourite", "Ctrl + D",
            "Adds or removes the selected record from Favourites.", ToggleFavorite,
            () => _model.HasSelection, "star pin");

        _commands.Add("Records", "Move to recycle bin", "Delete",
            "Moves the selected records to the recycle bin, where they can be restored.",
            DeleteSelected, () => _model.HasSelection && !_model.IsRecycleBin, "remove");

        _commands.Add("Records", "Restore from recycle bin", "Ctrl + R",
            "Puts the selected deleted records back where they were.", RestoreSelected,
            () => _model.IsRecycleBin && _model.HasSelection, "undelete undo");

        _commands.Add("Records", "Delete permanently", "Shift + Delete",
            "Destroys the selected records in the recycle bin. This cannot be undone.",
            PurgeSelected, () => _model.IsRecycleBin && _model.HasSelection, "purge");

        _commands.Add("Records", "Empty the recycle bin", "",
            "Destroys everything in the recycle bin. This cannot be undone.", EmptyRecycleBin,
            () => true, "purge clear");

        _commands.Add("Finding things", "Search", "Ctrl + F",
            "Moves to the search box. Results narrow as you type.", FocusSearch, null, "find filter");

        _commands.Add("Finding things", "Clear the search", "Ctrl + Shift + C",
            "Empties the search box and shows everything again.", ClearSearch, null, "reset");

        _commands.Add("Finding things", "Find a command", "Ctrl + K",
            "Opens a box where you can type the name of any command instead of using the menus.",
            OpenCommandPalette, null, "palette menu");

        _commands.Add("Finding things", "Say where I am", "Ctrl + Shift + W",
            "Reads out the current category, the number of records and your position in the list.",
            SayWhereIAm, null, "position orientation");

        _commands.Add("Moving around", "Next pane", "F6",
            "Moves to the next part of the window: categories, search, the list, then the status bar.",
            () => CyclePane(1), null, "navigate");

        _commands.Add("Moving around", "Previous pane", "Shift + F6",
            "Moves to the previous part of the window.", () => CyclePane(-1), null, "navigate");

        _commands.Add("Moving around", "Jump to a category", "Alt + 1 to Alt + 9",
            "Jumps straight to one of the first nine categories in the list.", () => { }, null, "navigate");

        _commands.Add("Moving around", "Show or hide the category list", "Ctrl + L",
            "Hides the left-hand list to give the records the full width of the window.",
            ToggleNavigationPane, null, "collapse");

        _commands.Add("View", "Switch between list and table", "Ctrl + Shift + V",
            "List view gives each record one line that a screen reader reads as a sentence. " +
            "Table view puts the fields in columns.", ToggleViewMode, null, "layout grid");

        _commands.Add("View", "Show or hide the sorting and period controls", "",
            "Puts the order and period lists away, or brings them back. What you chose " +
            "stays in force either way.", ToggleSortControls, null, "sort order period filter");

        _commands.Add("View", "Show or hide the totals", "",
            "Puts the totals panel away, or brings it back. The totals are read out with " +
            "the record count either way.", ToggleTotalsPanel, null, "total sum amount");

        _commands.Add("View", "Make text bigger", "Ctrl + Plus",
            "Increases the text size throughout the application.", () => ChangeFontSize(+1), null, "zoom larger");

        _commands.Add("View", "Make text smaller", "Ctrl + Minus",
            "Decreases the text size throughout the application.", () => ChangeFontSize(-1), null, "zoom smaller");

        _commands.Add("View", "Reset text size", "Ctrl + 0",
            "Puts the text size back to the standard size.", ResetFontSize, null, "zoom default");

        _commands.Add("View", "Refresh", "F5",
            "Reloads the categories and records from the database.", Refresh, null, "reload");

        _commands.Add("Editing a record", "Save", "Ctrl + S",
            "Saves the record being edited.", SaveEditor, () => _model.IsEditing, "store");

        _commands.Add("Editing a record", "Cancel", "Escape",
            "Goes back to the list. You are asked first if there are unsaved changes.",
            CancelEditor, () => _model.IsEditing, "back close");

        _commands.Add("Editing a record", "Attach a file", "Ctrl + Shift + A",
            "Copies a file into the record being edited.", AddAttachment, () => _model.IsEditing, "file upload");

        _commands.Add("Export and backup", "Export list to PDF", "Ctrl + E",
            "Writes the records now listed to a printable PDF table.", () => ExportPdf(details: false),
            null, "print report");

        _commands.Add("Export and backup", "Export full details to PDF", "",
            "Writes every field of every listed record to a PDF, one block per record.",
            () => ExportPdf(details: true), null, "print report");

        _commands.Add("Export and backup", "Export to plain text", "Ctrl + T",
            "Writes the listed records to a plain text file. This is the format a screen reader reads best.",
            ExportText, null, "txt report");

        _commands.Add("Export and backup", "Export to CSV for a spreadsheet", "",
            "Writes the listed records to a CSV file that Excel can open.", ExportCsv, null, "excel");

        _commands.Add("Export and backup", "Back up now", "Ctrl + B",
            "Copies the database and all attached files into a single dated zip.", BackupNow, null, "save copy");

        _commands.Add("Export and backup", "Open data folder", "",
            "Opens the folder holding the database, the attachments and the backups.",
            OpenDataFolder, null, "explorer files");

        _commands.Add("Setup", "New category", "Ctrl + Shift + N",
            "Creates a new kind of record, then takes you straight to adding its fields.",
            NewCategory, null, "add type kind");

        _commands.Add("Setup", "Categories and their fields", "Ctrl + Shift + T",
            "Adds, renames or changes the categories of record and the fields they hold.",
            ManageRecordTypes, null, "fields categories types");

        _commands.Add("Setup", "Settings", "Ctrl + Comma",
            "Changes the text size, the colours, the focus outline, spoken confirmations and backups.",
            OpenSettings, null, "preferences options");

        _commands.Add("Tools", "Work out every total again", "",
            "Re-applies the formulas to records saved before a formula was changed.",
            RecalculateTotals, null, "recalculate formula totals refresh");

        _commands.Add("Help", "Keyboard shortcuts", "F1",
            "Lists every command and its shortcut.", ShowShortcuts, null, "help keys");

        _commands.Add("Help", "About acvayrecords", "",
            "Shows the version and where the data is stored.", ShowAbout, null, "version");
    }

    private void WireMenu()
    {
        MenuNewRecord.Click += (_, _) => NewRecord();
        MenuExportPdfList.Click += (_, _) => ExportPdf(details: false);
        MenuExportPdfDetails.Click += (_, _) => ExportPdf(details: true);
        MenuExportText.Click += (_, _) => ExportText();
        MenuExportCsv.Click += (_, _) => ExportCsv();
        MenuBackup.Click += (_, _) => BackupNow();
        MenuOpenDataFolder.Click += (_, _) => OpenDataFolder();
        MenuExit.Click += (_, _) => Close();

        MenuEditRecord.Click += (_, _) => OpenSelectedRecord();
        MenuToggleFavorite.Click += (_, _) => ToggleFavorite();
        MenuDelete.Click += (_, _) => DeleteSelected();
        MenuRestore.Click += (_, _) => RestoreSelected();
        MenuPurge.Click += (_, _) => PurgeSelected();
        MenuEmptyBin.Click += (_, _) => EmptyRecycleBin();

        MenuToggleView.Click += (_, _) => ToggleViewMode();
        MenuToggleSort.Click += (_, _) => ToggleSortControls();
        MenuToggleTotals.Click += (_, _) => ToggleTotalsPanel();
        MenuFocusSearch.Click += (_, _) => FocusSearch();
        MenuClearSearch.Click += (_, _) => ClearSearch();
        MenuFontBigger.Click += (_, _) => ChangeFontSize(+1);
        MenuFontSmaller.Click += (_, _) => ChangeFontSize(-1);
        MenuFontReset.Click += (_, _) => ResetFontSize();
        MenuToggleNav.Click += (_, _) => ToggleNavigationPane();
        MenuWhereAmI.Click += (_, _) => SayWhereIAm();
        MenuRefresh.Click += (_, _) => Refresh();

        MenuRecalculate.Click += (_, _) => RecalculateTotals();
        MenuNewCategory.Click += (_, _) => NewCategory();
        MenuManageTypes.Click += (_, _) => ManageRecordTypes();
        MenuCommandPalette.Click += (_, _) => OpenCommandPalette();
        MenuSettings.Click += (_, _) => OpenSettings();

        MenuShortcuts.Click += (_, _) => ShowShortcuts();
        MenuAbout.Click += (_, _) => ShowAbout();
    }

    private void WireButtons()
    {
        ToggleSortButton.Click += (_, _) => ToggleSortControls();
        ToggleTotalsButton.Click += (_, _) => ToggleTotalsPanel();

        SortOrderButton.Click += (_, _) =>
        {
            _model.SortDescending = !_model.SortDescending;
            _announcer.Say($"{_model.SortOrderLabel}. {_model.ListSummary}");
        };

        // A date is applied when the user leaves the box or presses Enter, not as they
        // type: reloading the list on every keystroke would talk over them.
        WireDateBox(FromDateBox, isStart: true);
        WireDateBox(ToDateBox, isStart: false);

        NewRecordButton.Click += (_, _) => NewRecord();
        EmptyStateNewButton.Click += (_, _) => NewRecord();
        OpenRecordButton.Click += (_, _) => OpenSelectedRecord();
        ToggleViewButton.Click += (_, _) => ToggleViewMode();

        SaveButton.Click += (_, _) => SaveEditor();
        CancelButton.Click += (_, _) => CancelEditor();
        DeleteFromEditorButton.Click += (_, _) => DeleteCurrentlyEditedRecord();

        AddAttachmentButton.Click += (_, _) => AddAttachment();
        OpenAttachmentButton.Click += (_, _) => OpenSelectedAttachment();
        RemoveAttachmentButton.Click += (_, _) => RemoveSelectedAttachment();
    }

    // ===================================================================== keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        var typing = VisualTreeHelpers.IsTypingInTextBox();

        // ---- Pane movement, always available ----
        if (e.Key == Key.F6)
        {
            CyclePane(shift ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F1)
        {
            ShowShortcuts();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            Refresh();
            e.Handled = true;
            return;
        }

        // ---- Alt plus a digit jumps to a category ----
        if (alt && e.Key is >= Key.D1 and <= Key.D9)
        {
            // The jump can be refused, when it would leave an unsaved record behind.
            var before = _model.SelectedNavigation;
            _model.SelectNavigationByNumber(e.Key - Key.D1 + 1);

            if (!ReferenceEquals(before, _model.SelectedNavigation))
                _announcer.Say(_model.ListSummary);
            e.Handled = true;
            return;
        }

        if (ctrl && !shift)
        {
            switch (e.Key)
            {
                case Key.N: NewRecord(); e.Handled = true; return;
                case Key.F: FocusSearch(); e.Handled = true; return;
                case Key.K: OpenCommandPalette(); e.Handled = true; return;
                case Key.S: SaveEditor(); e.Handled = true; return;
                case Key.D: ToggleFavorite(); e.Handled = true; return;
                case Key.E: ExportPdf(details: false); e.Handled = true; return;
                case Key.T: ExportText(); e.Handled = true; return;
                case Key.B: BackupNow(); e.Handled = true; return;
                case Key.L: ToggleNavigationPane(); e.Handled = true; return;
                case Key.R: RestoreSelected(); e.Handled = true; return;
                case Key.OemComma: OpenSettings(); e.Handled = true; return;

                // Both the main keyboard and the numeric keypad.
                case Key.OemPlus or Key.Add: ChangeFontSize(+1); e.Handled = true; return;
                case Key.OemMinus or Key.Subtract: ChangeFontSize(-1); e.Handled = true; return;
                case Key.D0 or Key.NumPad0: ResetFontSize(); e.Handled = true; return;
            }
        }

        if (ctrl && shift)
        {
            switch (e.Key)
            {
                case Key.V: ToggleViewMode(); e.Handled = true; return;
                case Key.N: NewCategory(); e.Handled = true; return;
                case Key.T: ManageRecordTypes(); e.Handled = true; return;
                case Key.A: AddAttachment(); e.Handled = true; return;
                case Key.C: ClearSearch(); e.Handled = true; return;
                case Key.W: SayWhereIAm(); e.Handled = true; return;
            }
        }

        // ---- Keys that must not fire while the user is typing ----
        if (e.Key == Key.Escape)
        {
            if (ReferenceEquals(Keyboard.FocusedElement, SearchBox) && _model.IsSearching)
            {
                ClearSearch();
                e.Handled = true;
                return;
            }

            if (_model.IsEditing)
            {
                CancelEditor();
                e.Handled = true;
            }
            return;
        }

        if (typing)
        {
            // Enter in the search box sends the user to the results, which is where
            // they want to be after typing a query.
            if (e.Key == Key.Enter && ReferenceEquals(Keyboard.FocusedElement, SearchBox))
            {
                FocusRecordList();
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when !_model.IsEditing:
                OpenSelectedRecord();
                e.Handled = true;
                break;

            case Key.Delete when shift:
                PurgeSelected();
                e.Handled = true;
                break;

            case Key.Delete:
                DeleteSelected();
                e.Handled = true;
                break;
        }
    }

    // ====================================================== showing and hiding

    /// <summary>
    /// Shows or hides the sorting and period controls, and says what happened.
    ///
    /// Focus moves into the controls when they appear, because a keyboard user who
    /// asked for them should not then have to go looking for where they went.
    /// </summary>
    private void ToggleSortControls()
    {
        _model.ShowSortControls = !_model.ShowSortControls;
        RebuildPaneList();

        if (_model.ShowSortControls)
        {
            Dispatcher.BeginInvoke(() =>
            {
                VisualTreeHelpers.FocusAccessibly(SortByBox);
                _announcer.Say(
                    $"Sorting and period shown. Sorted by {_model.SelectedSort?.Name}, " +
                    $"{_model.SortOrderLabel.ToLowerInvariant()}. {_model.PeriodDescription}");
            });
            return;
        }

        _announcer.Say("Sorting and period hidden. The order and the period stay as they are.");
        VisualTreeHelpers.FocusAccessibly(ToggleSortButton);
    }

    private void ToggleTotalsPanel()
    {
        _model.ShowTotalsPanel = !_model.ShowTotalsPanel;

        if (!_model.ShowTotalsPanel)
        {
            _announcer.Say("Totals hidden. They are still read out with the record count.");
            return;
        }

        _announcer.Say(_model.TotalsSpoken.Length > 0
            ? "Totals shown. " + _model.TotalsSpoken
            : "Totals shown. " + (_model.TotalsHint.Length > 0
                ? _model.TotalsHint
                : "Nothing here adds up to anything yet."));
    }

    // ================================================================ date boxes

    /// <summary>
    /// Applies what was typed in a period date box, on Enter or on leaving it.
    ///
    /// The same wording the date fields accept works here — "today", "yesterday", or a
    /// date — so there is one way to type a date in the whole application. An empty
    /// box means that end of the range is open, which is a real answer rather than an
    /// error.
    /// </summary>
    private void WireDateBox(TextBox box, bool isStart)
    {
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Apply(announce: true);
            e.Handled = true;
        };

        box.LostFocus += (_, _) => Apply(announce: false);

        void Apply(bool announce)
        {
            var typed = box.Text.Trim();

            if (typed.Length == 0)
            {
                if (isStart) _model.CustomFrom = null;
                else _model.CustomTo = null;

                if (announce)
                    _announcer.Say($"{(isStart ? "Start" : "End")} date cleared. {_model.ListSummary}");
                return;
            }

            if (!FieldValueFormat.TryCanonicalize(FieldDataType.Date, typed, out var canonical, out var error))
            {
                _announcer.SayError(error);
                box.SelectAll();
                return;
            }

            var date = DateTime.ParseExact(canonical, FieldValueFormat.DateFormat,
                CultureInfo.InvariantCulture);

            // Show it back in the form the rest of the app uses, so what is on screen
            // and what is being filtered on cannot drift apart.
            box.Text = FieldValueFormat.ToDisplay(FieldDataType.Date, canonical);

            if (isStart) _model.CustomFrom = date;
            else _model.CustomTo = date;

            if (announce) _announcer.Say(_model.ListSummary);
        }
    }

    // ===================================================================== panes

    private void RebuildPaneList()
    {
        _panes.Clear();

        if (NavPane.IsVisible)
            _panes.Add((NavList, "Categories"));

        if (_model.IsEditing)
        {
            _panes.Add((TitleBox, "Record fields"));
            _panes.Add((SaveButton, "Save and cancel buttons"));
        }
        else
        {
            _panes.Add((SearchBox, "Search"));
            if (_model.ShowSortControls) _panes.Add((SortByBox, "Sort and period"));
            _panes.Add((_model.IsTableView ? RecordGrid : RecordList, "Record list"));

            if (NewRecordButton.IsVisible) _panes.Add((NewRecordButton, "Buttons"));
        }

        _panes.Add((StatusText, "Status bar"));
        _paneIndex = 0;
    }

    /// <summary>
    /// Moves to the next or previous pane and says which one it is.
    ///
    /// Announcing the pane name is the point of this: tabbing through a window tells
    /// you about individual controls, but F6 tells you which region you are in, which
    /// is how a screen-reader user keeps a mental map of the window.
    /// </summary>
    private void CyclePane(int direction)
    {
        if (_panes.Count == 0) RebuildPaneList();
        if (_panes.Count == 0) return;

        for (var attempt = 0; attempt < _panes.Count; attempt++)
        {
            _paneIndex = (_paneIndex + direction + _panes.Count) % _panes.Count;
            var (element, name) = _panes[_paneIndex];

            if (!element.IsVisible) continue;

            if (VisualTreeHelpers.FocusAccessibly(element))
            {
                _announcer.Say(name);
                return;
            }
        }
    }

    private void FocusSearch()
    {
        if (_model.IsEditing) CancelEditor();
        if (VisualTreeHelpers.FocusAccessibly(SearchBox))
        {
            SearchBox.SelectAll();
            _announcer.Say("Search. Type any part of a title, a field, a note or a tag.");
        }
    }

    private void FocusRecordList()
    {
        var target = _model.IsTableView ? (FrameworkElement)RecordGrid : RecordList;
        if (VisualTreeHelpers.FocusAccessibly(target))
            _announcer.Say(_model.ListSummary + " " + _model.SelectionPositionText);
    }

    private void OnRecordsReloaded()
    {
        PositionText.Text = _model.SelectionPositionText;

        // Keep the selected row visible so Magnifier follows it.
        if (_model.SelectedRecord is not null && !_model.IsTableView)
            RecordList.ScrollIntoView(_model.SelectedRecord);
    }

    /// <summary>
    /// Hides or shows the category list down the left-hand side.
    ///
    /// Hiding it has to take the column with it. A column keeps its minimum width
    /// however narrow it is told to be, so setting the width to nothing used to leave
    /// an empty strip the width of that minimum down the side of the window. The
    /// minimum goes to nothing first, and comes back when the list does.
    ///
    /// Whatever width the splitter was left at is remembered, so showing the list
    /// again gives it back the size the user chose rather than the one it was born
    /// with.
    /// </summary>
    private void ToggleNavigationPane()
    {
        var hiding = NavPane.Visibility == Visibility.Visible;

        if (hiding)
        {
            if (NavColumn.ActualWidth > 0) _navigationWidth = NavColumn.ActualWidth;

            NavPane.Visibility = Visibility.Collapsed;
            NavSplitter.Visibility = Visibility.Collapsed;

            NavColumn.MinWidth = 0;
            NavColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
        }
        else
        {
            NavPane.Visibility = Visibility.Visible;
            NavSplitter.Visibility = Visibility.Visible;

            NavColumn.MinWidth = NavigationMinimumWidth;
            NavColumn.Width = new GridLength(Math.Max(_navigationWidth, NavigationMinimumWidth));
            SplitterColumn.Width = new GridLength(4);
        }

        RebuildPaneList();
        _announcer.Say(hiding ? "Category list hidden." : "Category list shown.");

        if (hiding) FocusRecordList();
        else VisualTreeHelpers.FocusAccessibly(NavList);
    }

    private void SayWhereIAm()
    {
        var where = _model.IsEditing
            ? $"Editing a {_model.Editor!.RecordType.Name} record. " +
              $"{_model.Editor.Fields.Count + 1} fields. " +
              (_model.Editor.ComputedSummary.Length > 0 ? _model.Editor.ComputedSummary + ". " : string.Empty) +
              (_model.Editor.IsDirty ? "There are unsaved changes." : "No unsaved changes.")
            : $"{_model.ListHeading}. {_model.ListSummary} {_model.SelectionPositionText}. " +
              $"{_model.ViewModeName}." +
              (_model.IsSearching ? $" Searching for {_model.SearchText}." : string.Empty);

        _announcer.Say(where);
    }

    // ===================================================================== records

    /// <summary>
    /// Starts a new record, asking which category it belongs to when the selection
    /// covers more than one — a group, or All records. On Favourites and in the recycle
    /// bin there is no category to add to at all, so the button is not shown there and
    /// the shortcut says why rather than doing nothing.
    /// </summary>
    private void NewRecord()
    {
        // Starting a second record on top of a half-written one would lose the first.
        if (!TryLeaveEditor("starting another record")) return;

        var choices = _model.NewRecordChoices;

        if (choices.Count == 0)
        {
            _announcer.Say(_model.CannotAddRecordReason, AnnouncementKind.Warning);
            VisualTreeHelpers.FocusAccessibly(NavList);
            return;
        }

        var type = choices[0];

        if (choices.Count > 1)
        {
            var chooser = new ChooseCategoryWindow(_services, choices, _model.ListHeading)
            {
                Owner = this
            };

            if (chooser.ShowDialog() != true || chooser.Chosen is null)
            {
                _announcer.Say("No record was added.");
                VisualTreeHelpers.FocusAccessibly(NewRecordButton);
                return;
            }

            type = chooser.Chosen;
        }

        var editor = _model.BeginNewRecord(type);
        if (editor is null) return;

        _lastAnnouncedTotals = editor.ComputedSummary;
        RebuildPaneList();
        Dispatcher.BeginInvoke(() =>
        {
            VisualTreeHelpers.FocusAccessibly(TitleBox);
            _announcer.Say(editor.OpeningAnnouncement);
        });
    }

    private void OpenSelectedRecord()
    {
        if (_model.IsEditing) return;

        if (_model.SelectedRecord is null)
        {
            _announcer.Say("No record is selected.", AnnouncementKind.Warning);
            return;
        }

        if (_model.IsRecycleBin)
        {
            _announcer.Say(
                "This record is in the recycle bin. Press Control plus R to restore it before editing.",
                AnnouncementKind.Warning);
            return;
        }

        var editor = _model.BeginEditRecord(_model.SelectedRecord);
        if (editor is null)
        {
            _announcer.SayError("That record could not be opened. It may have just been deleted.");
            return;
        }

        _lastAnnouncedTotals = editor.ComputedSummary;
        RebuildPaneList();
        Dispatcher.BeginInvoke(() =>
        {
            VisualTreeHelpers.FocusAccessibly(TitleBox);
            _announcer.Say(editor.OpeningAnnouncement);
        });
    }

    private void SaveEditor()
    {
        var editor = _model.Editor;
        if (editor is null) return;

        if (!editor.Validate(out var firstInvalid, out var summary))
        {
            // Move focus to the problem. Saying the error without moving focus leaves
            // a screen-reader user hunting for which field is wrong.
            if (firstInvalid is null) VisualTreeHelpers.FocusAccessibly(TitleBox);
            else FocusField(firstInvalid);

            _announcer.SayError("Not saved. " + summary);
            return;
        }

        var duplicates = _model.FindDuplicateTitles();
        if (duplicates.Count > 0)
        {
            var answer = MessageBox.Show(
                $"There is already a {editor.RecordType.Name.ToLowerInvariant()} called \"{editor.Title.Trim()}\".\n\n" +
                "Do you want to save this one as well?",
                "A record with this title already exists",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                _announcer.Say("Not saved. Change the title, or save again to keep both.");
                VisualTreeHelpers.FocusAccessibly(TitleBox);
                return;
            }
        }

        var wasNew = editor.IsNew;
        var title = editor.Title.Trim();

        try
        {
            _model.SaveEditor();
        }
        catch (Exception exception)
        {
            App.Log("Saving a record failed", exception);
            _announcer.SayError("The record could not be saved. " + exception.Message);
            return;
        }

        _model.CloseEditor();
        RebuildPaneList();

        _announcer.SaySuccess(wasNew
            ? $"Saved. {title} was added. {_model.ListSummary}"
            : $"Saved. {title} was updated.");

        FocusRecordList();
    }

    private void CancelEditor()
    {
        var editor = _model.Editor;
        if (editor is null) return;

        if (editor.IsDirty)
        {
            switch (AskAboutUnsavedRecord("going back"))
            {
                case MessageBoxResult.Yes:
                    SaveEditor();
                    return;
                case MessageBoxResult.Cancel:
                    _announcer.Say("Still editing.");
                    VisualTreeHelpers.FocusAccessibly(TitleBox);
                    return;
            }
        }

        _model.CloseEditor();
        RebuildPaneList();
        _announcer.Say("Back to the list. " + _model.ListSummary);
        FocusRecordList();
    }

    // ======================================================= leaving the editor

    /// <summary>
    /// The one question asked whenever an unsaved record is about to be left behind,
    /// worded the same way wherever it is asked from.
    /// </summary>
    private MessageBoxResult AskAboutUnsavedRecord(string whatIsHappening)
    {
        var editor = _model.Editor;
        var what = editor is null
            ? "This record"
            : $"This {editor.RecordType.Name.ToLowerInvariant()} record";

        var named = editor is null || editor.Title.Trim().Length == 0
            ? what
            : $"{what}, {editor.Title.Trim()},";

        return MessageBox.Show(this,
            $"{named} has changes that have not been saved.\n\n" +
            $"Yes saves it before {whatIsHappening}. " +
            "No throws the changes away. " +
            "Cancel stays here and keeps editing.",
            "Save this record first?",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
    }

    /// <summary>
    /// Asked by the view model when the selection is about to move away from an open
    /// editor. Returning false leaves the category list where it was.
    ///
    /// Saving cannot happen here: a save reloads the navigation list, and this runs
    /// inside the property that is being set while the list is mid-change. So the save
    /// is handed to the dispatcher, and the category the user asked for is selected
    /// once it has finished — by what it stands for, since the reload replaces the
    /// objects themselves.
    /// </summary>
    private bool ConfirmLeavingEditor(NavigationItem? target)
    {
        var editor = _model.Editor;
        if (editor is null) return true;

        // An untouched record is not worth a question. There is nothing to lose.
        if (!editor.IsDirty)
        {
            _model.CloseEditor();
            RebuildPaneList();
            return true;
        }

        switch (AskAboutUnsavedRecord($"going to {Describe(target)}"))
        {
            case MessageBoxResult.Yes:
                Dispatcher.BeginInvoke(() =>
                {
                    SaveEditor();

                    // SaveEditor leaves the editor open when it could not save, for
                    // example when a required field is empty. Moving on then would
                    // throw away the very record the user asked to keep.
                    if (_model.Editor is null) _model.SelectNavigationLike(target);
                });
                return false;

            case MessageBoxResult.No:
                _model.CloseEditor();
                RebuildPaneList();
                _announcer.Say("The changes were thrown away.", AnnouncementKind.Warning);
                return true;

            default:
                _announcer.Say("Still editing. The category list has not moved.");
                VisualTreeHelpers.FocusAccessibly(TitleBox);
                return false;
        }
    }

    /// <summary>
    /// The same question, for the places that are not a property being set and so can
    /// simply save and carry on. True means it is safe to go ahead.
    /// </summary>
    private bool TryLeaveEditor(string whatIsHappening)
    {
        var editor = _model.Editor;
        if (editor is null) return true;

        if (!editor.IsDirty)
        {
            _model.CloseEditor();
            RebuildPaneList();
            return true;
        }

        switch (AskAboutUnsavedRecord(whatIsHappening))
        {
            case MessageBoxResult.Yes:
                SaveEditor();
                return _model.Editor is null;

            case MessageBoxResult.No:
                _model.CloseEditor();
                RebuildPaneList();
                _announcer.Say("The changes were thrown away.", AnnouncementKind.Warning);
                return true;

            default:
                _announcer.Say("Still editing.");
                VisualTreeHelpers.FocusAccessibly(TitleBox);
                return false;
        }
    }

    private static string Describe(NavigationItem? item) =>
        item is null ? "another view" : item.Name;

    private void FocusField(FieldEditorViewModel field)
    {
        FieldsHost.UpdateLayout();

        var container = FieldsHost.ItemContainerGenerator.ContainerFromItem(field) as DependencyObject;
        var input = VisualTreeHelpers.FindFieldInput(container);

        if (!VisualTreeHelpers.FocusAccessibly(input))
            VisualTreeHelpers.FocusAccessibly(TitleBox);
    }

    private void ToggleFavorite()
    {
        var row = _model.SelectedRecord;
        if (row is null)
        {
            _announcer.Say("No record is selected.", AnnouncementKind.Warning);
            return;
        }

        var becomingFavorite = !row.IsFavorite;
        _model.ToggleFavorite(row);

        _announcer.Say(becomingFavorite
            ? $"{row.Title} added to Favourites."
            : $"{row.Title} removed from Favourites.");
    }

    private List<RecordRowViewModel> CurrentSelection()
    {
        var selected = _model.IsTableView
            ? RecordGrid.SelectedItems.Cast<RecordRowViewModel>()
            : RecordList.SelectedItems.Cast<RecordRowViewModel>();

        return _model.RecordsForAction(selected);
    }

    private void DeleteSelected()
    {
        if (_model.IsRecycleBin)
        {
            _announcer.Say(
                "These records are already in the recycle bin. " +
                "Press Shift plus Delete to destroy them permanently, or Control plus R to restore them.",
                AnnouncementKind.Warning);
            return;
        }

        var rows = CurrentSelection();
        if (rows.Count == 0)
        {
            _announcer.Say("No record is selected.", AnnouncementKind.Warning);
            return;
        }

        var what = rows.Count == 1 ? $"\"{rows[0].Title}\"" : $"{rows.Count} records";

        var answer = MessageBox.Show(
            $"Move {what} to the recycle bin?\n\n" +
            "Nothing is destroyed. You can put it back from the recycle bin at any time.",
            "Move to the recycle bin?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("Nothing was deleted.");
            return;
        }

        var moved = _model.MoveToRecycleBin(rows);
        _announcer.Say(
            $"{moved} {(moved == 1 ? "record" : "records")} moved to the recycle bin. " +
            "They can be restored from the Recycle bin category.",
            AnnouncementKind.Warning);

        FocusRecordList();
    }

    private void DeleteCurrentlyEditedRecord()
    {
        var editor = _model.Editor;
        if (editor is null) return;

        if (editor.IsNew)
        {
            _announcer.Say("This record has not been saved yet. Press Escape to throw it away.",
                AnnouncementKind.Warning);
            return;
        }

        var answer = MessageBox.Show(
            $"Move \"{editor.Title}\" to the recycle bin?\n\n" +
            "Nothing is destroyed. You can put it back from the recycle bin at any time.",
            "Move to the recycle bin?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("Nothing was deleted.");
            return;
        }

        var title = editor.Title;
        _services.Records.MoveToRecycleBin(new[] { editor.RecordId });

        _model.CloseEditor();
        _model.ReloadNavigation();
        _model.LoadRecords();
        RebuildPaneList();

        _announcer.Say($"{title} moved to the recycle bin.", AnnouncementKind.Warning);
        FocusRecordList();
    }

    private void RestoreSelected()
    {
        if (!_model.IsRecycleBin)
        {
            _announcer.Say(
                "Restoring works in the recycle bin. Choose Recycle bin in the category list first.",
                AnnouncementKind.Warning);
            return;
        }

        var rows = CurrentSelection();
        if (rows.Count == 0)
        {
            _announcer.Say("No record is selected.", AnnouncementKind.Warning);
            return;
        }

        var restored = _model.RestoreFromRecycleBin(rows);
        _announcer.SaySuccess($"{restored} {(restored == 1 ? "record" : "records")} restored.");
        FocusRecordList();
    }

    private void PurgeSelected()
    {
        if (!_model.IsRecycleBin)
        {
            _announcer.Say(
                "Permanent deletion only works inside the recycle bin, so that nothing can be destroyed by accident. " +
                "Press Delete to move records there first.",
                AnnouncementKind.Warning);
            return;
        }

        var rows = CurrentSelection();
        if (rows.Count == 0) return;

        var what = rows.Count == 1 ? $"\"{rows[0].Title}\"" : $"{rows.Count} records";

        var answer = MessageBox.Show(
            $"Permanently delete {what}, and any files attached to them?\n\n" +
            "This cannot be undone. The only way back would be a backup zip.",
            "Delete permanently?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("Nothing was deleted.");
            return;
        }

        var purged = _model.PurgePermanently(rows);
        _announcer.Say($"{purged} {(purged == 1 ? "record" : "records")} permanently deleted.",
            AnnouncementKind.Warning);
        FocusRecordList();
    }

    private void EmptyRecycleBin()
    {
        var count = _services.Records.CountInRecycleBin();
        if (count == 0)
        {
            _announcer.Say("The recycle bin is already empty.");
            return;
        }

        var answer = MessageBox.Show(
            $"Permanently delete all {count} {(count == 1 ? "record" : "records")} in the recycle bin, " +
            "and any files attached to them?\n\n" +
            "This cannot be undone. The only way back would be a backup zip.",
            "Empty the recycle bin?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("The recycle bin was left alone.");
            return;
        }

        var emptied = _model.EmptyRecycleBin();
        _announcer.Say($"Recycle bin emptied. {emptied} {(emptied == 1 ? "record" : "records")} permanently deleted.",
            AnnouncementKind.Warning);
    }

    private void Refresh()
    {
        _model.ReloadNavigation();
        _model.LoadRecords();
        _announcer.Say("Refreshed. " + _model.ListSummary);
    }

    private void ClearSearch()
    {
        if (!_model.IsSearching)
        {
            _announcer.Say("The search box is already empty.");
            return;
        }

        _model.SearchText = string.Empty;
        _announcer.Say("Search cleared. " + _model.ListSummary);
    }

    // ===================================================================== view

    private void ToggleViewMode()
    {
        _model.ViewMode = _model.IsTableView ? RecordViewMode.Form : RecordViewMode.Table;

        _services.Settings.DefaultViewMode = _model.ViewMode;
        _services.SaveSettings();

        RebuildGridColumns();
        RebuildPaneList();

        var described = _model.IsTableView
            ? "Table view. Each field is a column. Use the arrow keys to move between cells."
            : "List view. Each record is one item that is read out as a sentence. Use the up and down arrow keys.";

        // Without this the switch looks broken on an empty category: the same
        // empty-state panel shows in either view, so nothing appears to happen.
        if (_model.IsEmptyList)
            described += $" {_model.ListHeading} has no records yet, so there is nothing to show in either view.";

        _announcer.Say(described);

        FocusRecordList();
    }

    private void ChangeFontSize(int steps)
    {
        var settings = _services.Settings;
        var next = Math.Clamp(settings.FontSize + steps, settings.FontSizeMinimum, settings.FontSizeMaximum);

        if (Math.Abs(next - settings.FontSize) < 0.01)
        {
            _announcer.Say(steps > 0
                ? $"Already at the largest text size, {settings.FontSize:0} point."
                : $"Already at the smallest text size, {settings.FontSize:0} point.");
            return;
        }

        settings.FontSize = next;
        _services.SaveSettings();
        _announcer.Say($"Text size {next:0} point.");
    }

    private void ResetFontSize()
    {
        _services.Settings.FontSize = 15;
        _services.SaveSettings();
        _announcer.Say("Text size reset to 15 point.");
    }

    /// <summary>
    /// Rebuilds the table columns for the selected category. Each column gets an
    /// explicit header so the screen reader names the cell's column as you move across.
    /// </summary>
    private void RebuildGridColumns()
    {
        RecordGrid.Columns.Clear();

        RecordGrid.Columns.Add(new DataGridTextColumn
        {
            // Use whatever this type calls its identifying field, so the column header
            // the screen reader reads out matches the label in the editor.
            Header = _model.SelectedNavigation?.RecordType?.EffectiveTitleLabel ?? "Title",
            Binding = new Binding(nameof(RecordRowViewModel.Title)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });

        if (_model.SelectedNavigation?.RecordType is null)
        {
            RecordGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Type",
                Binding = new Binding(nameof(RecordRowViewModel.TypeName)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        foreach (var field in _model.TableColumns)
        {
            RecordGrid.Columns.Add(new DataGridTextColumn
            {
                Header = field.Name,
                Binding = new Binding(nameof(RecordRowViewModel.DisplayValues))
                {
                    Converter = new FieldValueConverter(),
                    ConverterParameter = "f" + field.Id
                },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        RecordGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Tags",
            Binding = new Binding(nameof(RecordRowViewModel.TagsText)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        RecordGrid.Columns.Add(new DataGridTextColumn
        {
            Header = _model.IsRecycleBin ? "Deleted" : "Last changed",
            Binding = new Binding(_model.IsRecycleBin
                ? nameof(RecordRowViewModel.DeletedText)
                : nameof(RecordRowViewModel.ModifiedText)),
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            // A date and time needs a floor, or a star width shares it away and the
            // "AM" ends up cut off the end.
            MinWidth = 170
        });
    }

    // ===================================================================== attachments

    private void AddAttachment()
    {
        var editor = _model.Editor;
        if (editor is null)
        {
            _announcer.Say("Open a record first, then attach a file to it.", AnnouncementKind.Warning);
            return;
        }

        if (editor.IsNew)
        {
            _announcer.Say(
                "Save this record first. A file needs a saved record to attach to. Press Control plus S.",
                AnnouncementKind.Warning);
            VisualTreeHelpers.FocusAccessibly(SaveButton);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose a file to attach",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            _announcer.Say("No file was attached.");
            return;
        }

        var added = 0;
        foreach (var path in dialog.FileNames)
        {
            try
            {
                var attachment = _services.Attachments.Add(editor.RecordId, path);
                editor.Attachments.Add(attachment);
                added++;
            }
            catch (Exception exception)
            {
                App.Log("Attaching a file failed", exception);
                _announcer.SayError($"{Path.GetFileName(path)} could not be attached. {exception.Message}");
            }
        }

        if (added == 0) return;

        editor.RefreshAttachmentsHeading();
        _model.LoadRecords();

        _announcer.SaySuccess(added == 1
            ? $"{Path.GetFileName(dialog.FileNames[0])} attached and copied into the data folder."
            : $"{added} files attached and copied into the data folder.");

        VisualTreeHelpers.FocusAccessibly(AttachmentList);
    }

    private void OpenSelectedAttachment()
    {
        if (AttachmentList.SelectedItem is not Attachment attachment)
        {
            _announcer.Say("Choose an attached file first.", AnnouncementKind.Warning);
            return;
        }

        var path = AttachmentRepository.AbsolutePathOf(attachment);
        if (!File.Exists(path))
        {
            _announcer.SayError($"{attachment.FileName} is missing from the data folder.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            _announcer.Say($"Opening {attachment.FileName}.");
        }
        catch (Exception exception)
        {
            App.Log("Opening an attachment failed", exception);
            _announcer.SayError($"{attachment.FileName} could not be opened. {exception.Message}");
        }
    }

    private void RemoveSelectedAttachment()
    {
        var editor = _model.Editor;
        if (editor is null) return;

        if (AttachmentList.SelectedItem is not Attachment attachment)
        {
            _announcer.Say("Choose an attached file first.", AnnouncementKind.Warning);
            return;
        }

        var answer = MessageBox.Show(
            $"Remove \"{attachment.FileName}\" from this record?\n\n" +
            "The copy kept in the data folder is deleted. The original file wherever you got it from is untouched.",
            "Remove the attached file?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("The file was left attached.");
            return;
        }

        try
        {
            _services.Attachments.Remove(attachment.Id);
            editor.Attachments.Remove(attachment);
            editor.RefreshAttachmentsHeading();
            _model.LoadRecords();

            _announcer.Say($"{attachment.FileName} removed.", AnnouncementKind.Warning);
            VisualTreeHelpers.FocusAccessibly(AddAttachmentButton);
        }
        catch (Exception exception)
        {
            App.Log("Removing an attachment failed", exception);
            _announcer.SayError($"{attachment.FileName} could not be removed. {exception.Message}");
        }
    }

    // ===================================================================== export

    private RecordType? ExportType()
    {
        var type = _model.SelectedNavigation?.RecordType;
        if (type is not null) return type;

        _announcer.Say(
            "Choose a single category on the left before exporting, so the export knows which fields to include.",
            AnnouncementKind.Warning);
        VisualTreeHelpers.FocusAccessibly(NavList);
        return null;
    }

    private void ExportPdf(bool details)
    {
        var type = ExportType();
        if (type is null) return;

        var records = _model.RecordsForExport();
        if (records.Count == 0)
        {
            _announcer.Say("There is nothing in the list to export.", AnnouncementKind.Warning);
            return;
        }

        RunExport("PDF", ExportService.SuggestPath(type.Name, ".pdf"), path =>
            details
                ? _services.Pdf.ExportDetails(records, type, path)
                : _services.Pdf.ExportList(records, type, path),
            records.Count,
            extraSpokenNote:
            " Remember that a PDF carries no heading structure for a screen reader. " +
            "Control plus T writes the same records as plain text, which reads better.");
    }

    private void ExportText()
    {
        var type = ExportType();
        if (type is null) return;

        var records = _model.RecordsForExport();
        if (records.Count == 0)
        {
            _announcer.Say("There is nothing in the list to export.", AnnouncementKind.Warning);
            return;
        }

        RunExport("plain text", ExportService.SuggestPath(type.Name, ".txt"),
            path => _services.Export.ExportText(records, type, path), records.Count);
    }

    private void ExportCsv()
    {
        var type = ExportType();
        if (type is null) return;

        var records = _model.RecordsForExport();
        if (records.Count == 0)
        {
            _announcer.Say("There is nothing in the list to export.", AnnouncementKind.Warning);
            return;
        }

        RunExport("CSV", ExportService.SuggestPath(type.Name, ".csv"),
            path => _services.Export.ExportCsv(records, type, path), records.Count);
    }

    private void RunExport(string formatName, string suggestedPath, Func<string, string> write,
                           int recordCount, string extraSpokenNote = "")
    {
        var dialog = new SaveFileDialog
        {
            Title = $"Save the {formatName} export",
            FileName = Path.GetFileName(suggestedPath),
            InitialDirectory = Path.GetDirectoryName(suggestedPath),
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = Path.GetExtension(suggestedPath)
        };

        if (dialog.ShowDialog(this) != true)
        {
            _announcer.Say("Nothing was exported.");
            return;
        }

        try
        {
            var written = write(dialog.FileName);

            _announcer.SaySuccess(
                $"{recordCount} {(recordCount == 1 ? "record" : "records")} exported to {formatName}. " +
                $"Saved as {Path.GetFileName(written)} in {Path.GetDirectoryName(written)}." +
                extraSpokenNote);
        }
        catch (Exception exception)
        {
            App.Log($"Exporting to {formatName} failed", exception);
            _announcer.SayError($"The {formatName} export failed. {exception.Message}");
        }
    }

    private void BackupNow()
    {
        try
        {
            var path = _services.Backup.CreateManualBackup();
            var size = new FileInfo(path).Length / 1024.0 / 1024.0;

            _announcer.SaySuccess(
                $"Backup written. {Path.GetFileName(path)}, {size:0.0} megabytes, " +
                $"in the backups folder inside {AppPaths.Root}.");
        }
        catch (Exception exception)
        {
            App.Log("Backup failed", exception);
            _announcer.SayError("The backup failed. " + exception.Message);
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
            _announcer.Say($"Opening the data folder, {AppPaths.Root}.");
        }
        catch (Exception exception)
        {
            App.Log("Opening the data folder failed", exception);
            _announcer.SayError("The data folder could not be opened. It is at " + AppPaths.Root);
        }
    }

    // ===================================================================== dialogs

    private void OpenCommandPalette()
    {
        var palette = new CommandPaletteWindow(_commands, _announcer) { Owner = this };

        if (palette.ShowDialog() == true && palette.ChosenCommand is { } command)
        {
            // Let the dialog finish closing before the command moves focus around.
            Dispatcher.BeginInvoke(() =>
            {
                if (!command.IsAvailable)
                {
                    _announcer.Say($"{command.Name} is not available right now.", AnnouncementKind.Warning);
                    return;
                }
                command.Execute();
            });
        }
    }

    private void ShowShortcuts() =>
        new ShortcutHelpWindow(_commands) { Owner = this }.ShowDialog();

    /// <summary>
    /// Creates a category, then opens the field editor on it with the cursor already in
    /// the quick-add box. Naming a category and giving it fields are one job, so the two
    /// windows are joined rather than left for the user to find separately.
    /// </summary>
    private void NewCategory()
    {
        if (!TryLeaveEditor("making a category")) return;

        var dialog = new NewCategoryWindow(_services) { Owner = this };

        if (dialog.ShowDialog() != true || dialog.Created is null)
        {
            _announcer.Say("No category was created.");
            return;
        }

        var created = dialog.Created;

        new RecordTypeEditorWindow(_services, created.Id, startOnQuickAdd: true) { Owner = this }
            .ShowDialog();

        AfterCategoriesChanged($"{created.Name} is ready.");
    }

    private void ManageRecordTypes()
    {
        if (!TryLeaveEditor("opening the category list")) return;

        new RecordTypeEditorWindow(_services) { Owner = this }.ShowDialog();
        AfterCategoriesChanged("Categories updated.");
    }

    /// <summary>
    /// Re-applies every formula to the records already saved.
    ///
    /// A record works out its totals when it is saved, so changing a formula used to
    /// leave every existing record showing the old answer until each one was opened
    /// and saved again. This goes back over them. A backup is taken first, because it
    /// writes to many records at once on the strength of one keystroke.
    /// </summary>
    private void RecalculateTotals()
    {
        // This rewrites records wholesale; an editor open over the top of it would
        // save the old values back again the moment it was saved.
        if (!TryLeaveEditor("working the totals out again")) return;

        var types = _model.RecordTypes.Where(t => t.Fields.Any(f => f.IsCalculated)).ToList();

        if (types.Count == 0)
        {
            _announcer.Say(
                "No category has a worked-out field, so there is nothing to recalculate. " +
                "A field is worked out when it has a formula, such as quantity times price.",
                AnnouncementKind.Warning);
            return;
        }

        var names = string.Join(", ", types.Select(t => t.Name));

        var answer = MessageBox.Show(this,
            $"Work out every total again in {names}?\n\n" +
            "Records keep the totals that were worked out when they were last saved, so a " +
            "formula you changed afterwards has not reached them. This applies the formulas " +
            "as they are now.\n\n" +
            "A backup is taken first, and records whose totals are already right are left " +
            "exactly as they are.",
            "Work out every total again?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("Nothing was recalculated.");
            return;
        }

        string? backup = null;
        var previousCursor = Cursor;

        try
        {
            Cursor = System.Windows.Input.Cursors.Wait;

            backup = _services.Backup.CreateManualBackup();

            var checkedRecords = 0;
            var changedRecords = 0;
            var changedValues = 0;
            var changedTypes = new List<string>();

            foreach (var type in types)
            {
                var result = _services.Records.Recalculate(type);

                checkedRecords += result.RecordsChecked;
                changedRecords += result.RecordsChanged;
                changedValues += result.ValuesChanged;

                if (result.RecordsChanged > 0) changedTypes.Add(type.Name);
            }

            _model.ReloadNavigation();
            _model.LoadRecords();
            RebuildGridColumns();

            var message = changedRecords == 0
                ? $"Every total was already right. {checkedRecords} " +
                  $"{(checkedRecords == 1 ? "record was" : "records were")} checked and " +
                  "nothing needed changing."
                : $"{changedValues} {(changedValues == 1 ? "total" : "totals")} updated across " +
                  $"{changedRecords} {(changedRecords == 1 ? "record" : "records")} in " +
                  $"{string.Join(", ", changedTypes)}. {checkedRecords} " +
                  $"{(checkedRecords == 1 ? "record was" : "records were")} checked. " +
                  $"A backup was taken first, as {Path.GetFileName(backup)}.";

            _announcer.SaySuccess(message + " " + _model.ListSummary);
        }
        catch (Exception exception)
        {
            App.Log("Recalculating totals failed", exception);

            _announcer.SayError(
                "The totals could not be worked out again. " + exception.Message +
                (backup is null
                    ? " Nothing was changed."
                    : $" A backup was taken first, as {Path.GetFileName(backup)}."));
        }
        finally
        {
            Cursor = previousCursor;
        }
    }

    private void AfterCategoriesChanged(string message)
    {
        _model.ReloadNavigation();
        _model.LoadRecords();
        RebuildGridColumns();

        _announcer.Say($"{message} {_model.ListSummary}");
    }

    private void OpenSettings()
    {
        // Settings can move the data folder or turn encryption on, both of which end
        // the session, so an unsaved record has to be dealt with before going in.
        if (!TryLeaveEditor("opening Settings")) return;

        var window = new SettingsWindow(_services) { Owner = this };

        if (window.ShowDialog() == true)
        {
            _model.ViewMode = _services.Settings.DefaultViewMode;
            RebuildGridColumns();
            _announcer.SaySuccess("Settings saved.");
        }
        else
        {
            _announcer.Say("Settings were not changed.");
        }
    }

    private void ShowAbout() =>
        new AboutWindow { Owner = this }.ShowDialog();

    /// <summary>
    /// Puts the acvaysoft mark in the title bar and the taskbar. Wrapped because an
    /// icon that cannot be produced is not worth failing a window over.
    /// </summary>
    private void ApplyWindowIcon()
    {
        try
        {
            if (TryFindResource("Brand.LogoMark") is System.Windows.Media.ImageSource mark)
                Icon = mark;
        }
        catch (Exception exception)
        {
            App.Log("Setting the window icon failed", exception);
        }
    }

    // ===================================================================== window state

    private void RestoreWindowGeometry()
    {
        var settings = _services.Settings;

        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_model.Editor?.IsDirty == true)
        {
            var answer = MessageBox.Show(this,
                "A record has changes that have not been saved.\n\n" +
                "Yes saves it and closes, No closes without saving, Cancel keeps the application open.",
                "Save before closing?",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);

            switch (answer)
            {
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    return;
                case MessageBoxResult.Yes:
                    SaveEditor();
                    if (_model.Editor?.IsDirty == true)
                    {
                        // Validation failed, so closing would lose the record.
                        e.Cancel = true;
                        return;
                    }
                    break;
            }
        }

        var settings = _services.Settings;
        settings.WindowMaximized = WindowState == WindowState.Maximized;

        if (WindowState == WindowState.Normal)
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }

        try { _services.SaveSettings(); }
        catch (Exception exception) { App.Log("Saving settings on exit failed", exception); }
    }
}
