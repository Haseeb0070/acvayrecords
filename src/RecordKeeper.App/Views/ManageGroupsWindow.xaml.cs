using System.Globalization;
using System.Windows;
using System.Windows.Input;
using RecordKeeper.App.Accessibility;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Views;

/// <summary>Adds, renames and deletes groups.</summary>
public partial class ManageGroupsWindow : Window
{
    /// <summary>A group dressed for the list, with its counts already worded.</summary>
    private sealed class GroupRow
    {
        public required CategoryGroup Group { get; init; }
        public required int CategoryCount { get; init; }

        public string Name => Group.Name;

        public string Summary =>
            $"{Describe(CategoryCount, "category", "categories")}, " +
            $"{Describe(Group.RecordCount, "record", "records")}";

        public string ExpenseLine => Group.ExpenseTotal is null
            ? string.Empty
            : "Expenses " + Money.Text(Group.ExpenseTotal.Value);

        public string AccessibleName => Group.ExpenseTotal is null
            ? $"{Name}. {Summary}. Nothing in it is marked as an expense."
            : $"{Name}. {Summary}. Expenses totalling " +
              Money.Spoken(Group.ExpenseTotal.Value) + ".";

        private static string Describe(int count, string one, string many) =>
            count == 1 ? $"1 {one}" : $"{count} {many}";
    }

    private readonly AppServices _services;
    private readonly Announcer _announcer;

    public ManageGroupsWindow(AppServices services)
    {
        InitializeComponent();

        _services = services;
        _announcer = services.Announcer;

        AddGroupButton.Click += (_, _) => AddGroup();
        NewGroupNameBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            AddGroup();
            e.Handled = true;
        };

        RenameGroupButton.Click += (_, _) => RenameGroup();
        DeleteGroupButton.Click += (_, _) => DeleteGroup();
        CloseButton.Click += (_, _) => Close();

        GroupList.SelectionChanged += (_, _) => AnnounceSelected();

        Loaded += (_, _) =>
        {
            Reload();
            NewGroupNameBox.Focus();
            _announcer.Say("Groups. Type a name and press Enter to add one.");
        };
    }

    /// <summary>True when anything changed, so the caller knows to refresh.</summary>
    public bool Changed { get; private set; }

    private void Reload(long? selectId = null)
    {
        var categories = _services.RecordTypes.GetAll(includeFieldsAndCounts: false);

        var rows = _services.Groups.GetAll()
            .Select(group => new GroupRow
            {
                Group = group,
                CategoryCount = categories.Count(c => c.GroupId == group.Id)
            })
            .ToList();

        GroupList.ItemsSource = rows;
        GroupList.SelectedItem = selectId is not null
            ? rows.FirstOrDefault(r => r.Group.Id == selectId)
            : rows.FirstOrDefault();

        // An empty bordered box says nothing; a sentence says what to do next.
        var empty = rows.Count == 0;
        GroupList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        NoGroupsText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        RenameGroupButton.IsEnabled = !empty;
        DeleteGroupButton.IsEnabled = !empty;
    }

    private GroupRow? Selected => GroupList.SelectedItem as GroupRow;

    private void AnnounceSelected()
    {
        if (Selected is { } row) _announcer.Say(row.AccessibleName);
    }

    private void AddGroup()
    {
        var name = NewGroupNameBox.Text.Trim();
        if (name.Length == 0)
        {
            _announcer.Say("Type a name for the group first.", AnnouncementKind.Warning);
            NewGroupNameBox.Focus();
            return;
        }

        if (_services.Groups.NameExists(name))
        {
            var free = _services.Groups.SuggestName(name);
            _announcer.SayError($"There is already a group called {name}. Try {free}.");
            NewGroupNameBox.Text = free;
            NewGroupNameBox.SelectAll();
            NewGroupNameBox.Focus();
            return;
        }

        var group = new CategoryGroup
        {
            Name = name,
            SortOrder = GroupList.Items.Count
        };

        try
        {
            _services.Groups.Save(group);
        }
        catch (Exception exception)
        {
            App.Log("Creating a group failed", exception);
            _announcer.SayError("The group could not be created. " + exception.Message);
            return;
        }

        Changed = true;
        NewGroupNameBox.Clear();
        Reload(group.Id);
        NewGroupNameBox.Focus();

        _announcer.SaySuccess($"Group {name} added. It has no categories in it yet.");
    }

    private void RenameGroup()
    {
        if (Selected is not { } row)
        {
            _announcer.Say("Choose a group first.", AnnouncementKind.Warning);
            return;
        }

        var name = NewGroupNameBox.Text.Trim();
        if (name.Length == 0)
        {
            _announcer.Say(
                $"Type the new name in the box above, then press Rename to change {row.Name}.",
                AnnouncementKind.Warning);
            NewGroupNameBox.Focus();
            return;
        }

        if (_services.Groups.NameExists(name, row.Group.Id))
        {
            _announcer.SayError($"There is already a group called {name}.");
            NewGroupNameBox.SelectAll();
            NewGroupNameBox.Focus();
            return;
        }

        var was = row.Name;
        row.Group.Name = name;

        try
        {
            _services.Groups.Save(row.Group);
        }
        catch (Exception exception)
        {
            App.Log("Renaming a group failed", exception);
            _announcer.SayError("The group could not be renamed. " + exception.Message);
            return;
        }

        Changed = true;
        NewGroupNameBox.Clear();
        Reload(row.Group.Id);

        _announcer.SaySuccess($"{was} renamed to {name}.");
    }

    private void DeleteGroup()
    {
        if (Selected is not { } row)
        {
            _announcer.Say("Choose a group first.", AnnouncementKind.Warning);
            return;
        }

        var answer = MessageBox.Show(this,
            $"Delete the group \"{row.Name}\"?\n\n" +
            $"Its {row.CategoryCount} " +
            (row.CategoryCount == 1 ? "category stays" : "categories stay") +
            " exactly as they are, along with every record in them. They simply stop " +
            "belonging to a group, so they no longer count towards a group expense total.",
            "Delete this group?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("The group was left alone.");
            return;
        }

        try
        {
            _services.Groups.Delete(row.Group.Id);
        }
        catch (Exception exception)
        {
            App.Log("Deleting a group failed", exception);
            _announcer.SayError("The group could not be deleted. " + exception.Message);
            return;
        }

        Changed = true;
        Reload();

        _announcer.Say(
            $"{row.Name} deleted. Its categories and records are untouched and are now ungrouped.",
            AnnouncementKind.Warning);
    }
}
