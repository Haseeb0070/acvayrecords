using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RecordKeeper.App.Accessibility;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Views;

/// <summary>Adds, copies and reshapes categories and the fields they hold.</summary>
public partial class RecordTypeEditorWindow : Window
{
    private sealed record KindChoice(FieldDataType Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record GroupChoice(long? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppServices _services;
    private readonly Announcer _announcer;

    private List<RecordType> _types = new();
    private RecordType? _current;

    /// <summary>Stops the group box reassigning while it is being filled in.</summary>
    private bool _loadingGroups;

    public RecordTypeEditorWindow(AppServices services, long? selectTypeId = null, bool startOnQuickAdd = false)
    {
        InitializeComponent();

        _services = services;
        _announcer = services.Announcer;

        QuickFieldKindBox.ItemsSource = Enum.GetValues<FieldDataType>()
            .Select(value => new KindChoice(value, FieldDataTypeInfo.Describe(value)))
            .ToList();
        QuickFieldKindBox.SelectedIndex = 0;

        TypeList.SelectionChanged += OnTypeSelected;
        FieldList.SelectionChanged += (_, _) => AnnounceSelectedField();
        FieldList.MouseDoubleClick += (_, _) => EditField();

        CategoryGroupBox.SelectionChanged += (_, _) => AssignToSelectedGroup();
        ManageGroupsButton.Click += (_, _) => ManageGroups();

        AddTypeButton.Click += (_, _) => AddType();
        DuplicateTypeButton.Click += (_, _) => DuplicateType();
        DeleteTypeButton.Click += (_, _) => DeleteType();
        SaveTypeButton.Click += (_, _) => SaveCurrentType();

        QuickAddButton.Click += (_, _) => QuickAddField();
        QuickFieldNameBox.PreviewKeyDown += OnQuickAddKeyDown;

        EditFieldButton.Click += (_, _) => EditField();
        RemoveFieldButton.Click += (_, _) => RemoveField();
        MoveFieldUpButton.Click += (_, _) => MoveField(-1);
        MoveFieldDownButton.Click += (_, _) => MoveField(+1);

        CloseButton.Click += (_, _) => Close();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;

        Loaded += (_, _) =>
        {
            ReloadTypes(selectTypeId);

            if (startOnQuickAdd && _current is not null)
            {
                QuickFieldNameBox.Focus();
                _announcer.Say(
                    $"{_current.Name} created. Add its fields: type a name, choose what it holds, " +
                    "then press Enter. The box stays ready for the next one.");
            }
            else
            {
                TypeList.Focus();
                _announcer.Say(
                    $"Categories and their fields. {_types.Count} categories. " +
                    "Use the arrow keys to choose one, then Tab to reach its details.");
            }
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F6)
        {
            // Three regions: the category list, the details, and the field list.
            if (TypeList.IsKeyboardFocusWithin)
            {
                TypeNameBox.Focus();
                _announcer.Say("Category details");
            }
            else if (FieldList.IsKeyboardFocusWithin || QuickFieldNameBox.IsKeyboardFocusWithin)
            {
                TypeList.Focus();
                _announcer.Say("Categories");
            }
            else
            {
                QuickFieldNameBox.Focus();
                _announcer.Say("Fields. Type a name to add one.");
            }
            e.Handled = true;
            return;
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && shift && e.Key == Key.N)
        {
            AddType();
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && e.Key == Key.S)
        {
            SaveCurrentType();
            e.Handled = true;
        }
    }

    private void OnQuickAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        QuickAddField();
        e.Handled = true;
    }

    private void ReloadTypes(long? selectId = null)
    {
        _types = _services.RecordTypes.GetAll();

        TypeList.SelectionChanged -= OnTypeSelected;
        TypeList.ItemsSource = _types;
        TypeList.SelectionChanged += OnTypeSelected;

        var target = selectId is not null
            ? _types.FirstOrDefault(t => t.Id == selectId)
            : _types.FirstOrDefault();

        TypeList.SelectedItem = target;
        ShowType(target);
    }

    private void OnTypeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (HasUnsavedTypeChanges() && !ConfirmDiscardTypeChanges())
        {
            TypeList.SelectionChanged -= OnTypeSelected;
            TypeList.SelectedItem = _current;
            TypeList.SelectionChanged += OnTypeSelected;
            return;
        }

        ShowType(TypeList.SelectedItem as RecordType);
    }

    private void ShowType(RecordType? type)
    {
        _current = type;

        DetailsPanel.IsEnabled = type is not null;

        TypeNameBox.Text = type?.Name ?? string.Empty;
        TypeDescriptionBox.Text = type?.Description ?? string.Empty;
        TitleLabelBox.Text = type?.TitleLabel ?? string.Empty;

        FieldList.ItemsSource = type?.Fields;
        FieldList.Items.Refresh();

        PopulateGroups(type?.GroupId);
        UpdateFieldCount();

        if (type is not null)
        {
            _announcer.Say(
                $"{type.Name}. {Describe(type.Fields.Count)}. " +
                $"{type.RecordCount} {(type.RecordCount == 1 ? "record uses" : "records use")} this category.");
        }
    }

    private static string Describe(int count) => count switch
    {
        0 => "no fields yet",
        1 => "1 field",
        _ => $"{count} fields"
    };

    private void UpdateFieldCount()
    {
        var count = _current?.Fields.Count ?? 0;

        FieldCountText.Text = count switch
        {
            0 => "No fields yet. Every record still has its identifying field, notes and tags.",
            1 => "1 field. The order here is the order fields are read out in.",
            _ => $"{count} fields. The order here is the order fields are read out in."
        };
    }

    // ----------------------------------------------------------------- groups

    private void PopulateGroups(long? selectedGroupId)
    {
        _loadingGroups = true;
        try
        {
            var choices = new List<GroupChoice> { new(null, "No group") };
            choices.AddRange(_services.Groups.GetAll().Select(g => new GroupChoice(g.Id, g.Name)));

            CategoryGroupBox.ItemsSource = choices;
            CategoryGroupBox.SelectedItem =
                choices.FirstOrDefault(c => c.Id == selectedGroupId) ?? choices[0];
        }
        finally
        {
            _loadingGroups = false;
        }
    }

    private void AssignToSelectedGroup()
    {
        if (_loadingGroups || _current is null) return;
        if (CategoryGroupBox.SelectedItem is not GroupChoice choice) return;
        if (choice.Id == _current.GroupId) return;

        try
        {
            _services.Groups.AssignCategory(_current.Id, choice.Id);
        }
        catch (Exception exception)
        {
            App.Log("Assigning a category to a group failed", exception);
            _announcer.SayError("The category could not be moved. " + exception.Message);
            return;
        }

        _current.GroupId = choice.Id;
        _current.GroupName = choice.Id is null ? string.Empty : choice.Label;

        _announcer.SaySuccess(choice.Id is null
            ? $"{_current.Name} is no longer in a group, so it stops counting towards any group total."
            : $"{_current.Name} moved into {choice.Label}. Its expense fields now count towards that group's total.");
    }

    private void ManageGroups()
    {
        var window = new ManageGroupsWindow(_services) { Owner = this };
        window.ShowDialog();

        if (!window.Changed) return;

        // A group may have been renamed or removed under the current category.
        var keep = _current?.Id;
        ReloadTypes(keep);
    }

    // ----------------------------------------------------------------- categories

    private void AddType()
    {
        if (HasUnsavedTypeChanges() && !ConfirmDiscardTypeChanges()) return;

        var dialog = new NewCategoryWindow(_services) { Owner = this };

        if (dialog.ShowDialog() != true || dialog.Created is null)
        {
            _announcer.Say("No category was created.");
            return;
        }

        var created = dialog.Created;
        ReloadTypes(created.Id);

        QuickFieldNameBox.Focus();
        _announcer.SaySuccess(
            $"{created.Name} created with {Describe(created.Fields.Count)}. " +
            "Type a field name and press Enter to add one.");
    }

    private void DuplicateType()
    {
        if (_current is null) return;
        if (HasUnsavedTypeChanges() && !ConfirmDiscardTypeChanges()) return;

        var name = _services.RecordTypes.SuggestName($"{_current.Name} copy");

        // Reload from storage so the copy is of what is saved, not of unsaved edits.
        var source = _services.RecordTypes.GetById(_current.Id);
        if (source is null) return;

        var copy = source.CloneForNew(name);
        copy.SortOrder = _types.Count;

        try
        {
            _services.RecordTypes.Save(copy);
        }
        catch (Exception exception)
        {
            App.Log("Duplicating a category failed", exception);
            _announcer.SayError("The category could not be copied. " + exception.Message);
            return;
        }

        ReloadTypes(copy.Id);
        TypeNameBox.Focus();
        TypeNameBox.SelectAll();

        _announcer.SaySuccess(
            $"{name} created from {source.Name}, with {Describe(copy.Fields.Count)}. " +
            "The name is selected, so you can type over it.");
    }

    private void DeleteType()
    {
        if (_current is null) return;

        var answer = MessageBox.Show(this,
            $"Delete the category \"{_current.Name}\" and all of its field definitions?\n\n" +
            "This is only possible while no record uses it.",
            "Delete this category?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("The category was left alone.");
            return;
        }

        var name = _current.Name;

        if (!_services.RecordTypes.TryDelete(_current.Id, out var reason))
        {
            _announcer.SayError($"\"{name}\" could not be deleted. {reason}");
            MessageBox.Show(this, reason, "The category is still in use",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _current = null;
        ReloadTypes();
        _announcer.Say($"\"{name}\" deleted.", AnnouncementKind.Warning);
        TypeList.Focus();
    }

    private bool HasUnsavedTypeChanges()
    {
        if (_current is null) return false;

        return TypeNameBox.Text.Trim() != _current.Name ||
               TypeDescriptionBox.Text.Trim() != _current.Description ||
               TitleLabelBox.Text.Trim() != _current.TitleLabel;
    }

    private bool ConfirmDiscardTypeChanges()
    {
        var answer = MessageBox.Show(this,
            $"The details of \"{_current!.Name}\" have changed but have not been saved.\n\n" +
            "Yes throws the change away, No goes back so you can save it with Control plus S.",
            "Throw the change away?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        return answer == MessageBoxResult.Yes;
    }

    private void SaveCurrentType()
    {
        if (_current is null) return;

        var name = TypeNameBox.Text.Trim();
        if (name.Length == 0)
        {
            _announcer.SayError("The category needs a name.");
            TypeNameBox.Focus();
            return;
        }

        if (_services.RecordTypes.NameExists(name, _current.Id))
        {
            _announcer.SayError($"There is already a category called \"{name}\". Choose another name.");
            TypeNameBox.Focus();
            TypeNameBox.SelectAll();
            return;
        }

        _current.Name = name;
        _current.Description = TypeDescriptionBox.Text.Trim();
        _current.TitleLabel = TitleLabelBox.Text.Trim();

        try
        {
            var id = _services.RecordTypes.Save(_current);
            ReloadTypes(id);
            _announcer.SaySuccess($"\"{name}\" saved.");
        }
        catch (Exception exception)
        {
            App.Log("Saving a category failed", exception);
            _announcer.SayError("The category could not be saved. " + exception.Message);
        }
    }

    // ----------------------------------------------------------------- fields

    /// <summary>
    /// Adds a field from the strip at the top: just a name and a kind.
    /// Focus returns to the name box so a run of fields can be typed straight through.
    /// </summary>
    private void QuickAddField()
    {
        if (_current is null)
        {
            _announcer.Say("Choose a category first.", AnnouncementKind.Warning);
            return;
        }

        var name = QuickFieldNameBox.Text.Trim();
        if (name.Length == 0)
        {
            _announcer.Say("Type a name for the field first.", AnnouncementKind.Warning);
            QuickFieldNameBox.Focus();
            return;
        }

        if (_current.Fields.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _announcer.SayError($"{_current.Name} already has a field called {name}.");
            QuickFieldNameBox.SelectAll();
            QuickFieldNameBox.Focus();
            return;
        }

        var kind = (QuickFieldKindBox.SelectedItem as KindChoice)?.Value ?? FieldDataType.Text;

        var field = new FieldDef
        {
            Name = name,
            DataType = kind,
            ShowInList = true
        };

        _current.Fields.Add(field);

        // A choice field is incomplete without its options, so say so rather than
        // leaving the user with a dropdown that has nothing in it.
        var extra = kind == FieldDataType.Choice
            ? " Press Edit field to type its options, one on each line."
            : string.Empty;

        PersistFields($"{name} added as {FieldDataTypeInfo.Describe(kind).ToLowerInvariant()}.{extra}", field);

        QuickFieldNameBox.Clear();
        QuickFieldNameBox.Focus();
    }

    private void EditField()
    {
        if (_current is null) return;

        if (FieldList.SelectedItem is not FieldDef selected)
        {
            _announcer.Say("Choose a field first.", AnnouncementKind.Warning);
            return;
        }

        // Edit a copy, so cancelling really does change nothing.
        var draft = selected.Clone();
        var dialog = new FieldEditWindow(draft, isNew: false, SiblingNames(selected),
            _current.GroupName) { Owner = this };

        if (dialog.ShowDialog() != true)
        {
            _announcer.Say("The field was left unchanged.");
            return;
        }

        var index = _current.Fields.IndexOf(selected);
        _current.Fields[index] = draft;

        PersistFields($"Field \"{draft.Name}\" saved.", draft);
    }

    private void RemoveField()
    {
        if (_current is null) return;

        if (FieldList.SelectedItem is not FieldDef selected)
        {
            _announcer.Say("Choose a field first.", AnnouncementKind.Warning);
            return;
        }

        var answer = MessageBox.Show(this,
            $"Remove the field \"{selected.Name}\" from \"{_current.Name}\"?\n\n" +
            $"Any value already stored in this field, on every {_current.Name.ToLowerInvariant()} record, " +
            "is deleted with it. This cannot be undone.",
            "Remove this field?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            _announcer.Say("The field was left alone.");
            return;
        }

        var name = selected.Name;
        _current.Fields.Remove(selected);
        PersistFields($"Field \"{name}\" removed.", null, AnnouncementKind.Warning);
    }

    private void MoveField(int direction)
    {
        if (_current is null) return;

        if (FieldList.SelectedItem is not FieldDef selected)
        {
            _announcer.Say("Choose a field first.", AnnouncementKind.Warning);
            return;
        }

        var index = _current.Fields.IndexOf(selected);
        var target = index + direction;

        if (target < 0 || target >= _current.Fields.Count)
        {
            _announcer.Say(direction < 0
                ? $"\"{selected.Name}\" is already the first field."
                : $"\"{selected.Name}\" is already the last field.");
            return;
        }

        _current.Fields.RemoveAt(index);
        _current.Fields.Insert(target, selected);

        PersistFields($"\"{selected.Name}\" is now field {target + 1} of {_current.Fields.Count}.", selected);
    }

    /// <summary>
    /// Writes the field list straight through to the database. Field edits save as they
    /// are made rather than piling up behind another Save button, which keeps the number
    /// of things the user has to remember down to one.
    /// </summary>
    private void PersistFields(string message, FieldDef? keepSelected,
                               AnnouncementKind kind = AnnouncementKind.Success)
    {
        if (_current is null) return;

        try
        {
            _current.Name = TypeNameBox.Text.Trim().Length > 0 ? TypeNameBox.Text.Trim() : _current.Name;
            _current.Description = TypeDescriptionBox.Text.Trim();
            _current.TitleLabel = TitleLabelBox.Text.Trim();

            _services.RecordTypes.Save(_current);
        }
        catch (Exception exception)
        {
            App.Log("Saving fields failed", exception);
            _announcer.SayError("The change could not be saved. " + exception.Message);
            return;
        }

        var keepName = keepSelected?.Name;

        FieldList.ItemsSource = null;
        FieldList.ItemsSource = _current.Fields;

        if (keepName is not null)
            FieldList.SelectedItem = _current.Fields.FirstOrDefault(f => f.Name == keepName);

        UpdateFieldCount();
        _announcer.Say(message, kind);
    }

    /// <summary>
    /// The numeric fields a formula on <paramref name="except"/> could refer to.
    /// Only numbers and amounts of money can take part in arithmetic.
    /// </summary>
    private List<string> SiblingNames(FieldDef? except)
    {
        if (_current is null) return new List<string>();

        return _current.Fields
            .Where(f => !ReferenceEquals(f, except))
            .Where(f => FieldDataTypeInfo.IsNumeric(f.DataType))
            .Select(f => f.Name)
            .ToList();
    }

    private void AnnounceSelectedField()
    {
        if (FieldList.SelectedItem is not FieldDef field || _current is null) return;

        var position = _current.Fields.IndexOf(field) + 1;

        var nature = field.IsCalculated
            ? ", worked out as " + FormulaEvaluator.Describe(field.Formula)
            : field.IsRequired ? ", required" : string.Empty;

        _announcer.Say(
            $"{field.Name}, {FieldDataTypeInfo.Describe(field.DataType)}{nature}. " +
            $"Field {position} of {_current.Fields.Count}.");
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!HasUnsavedTypeChanges()) return;

        var answer = MessageBox.Show(this,
            $"The details of \"{_current!.Name}\" have not been saved.\n\n" +
            "Yes saves them, No closes without saving, Cancel keeps this window open.",
            "Save before closing?",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        switch (answer)
        {
            case MessageBoxResult.Cancel:
                e.Cancel = true;
                break;
            case MessageBoxResult.Yes:
                SaveCurrentType();
                break;
        }
    }
}
