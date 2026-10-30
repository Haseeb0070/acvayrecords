using System.Collections.ObjectModel;
using System.ComponentModel;
using RecordKeeper.App.Mvvm;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.ViewModels;

/// <summary>The record currently being added or changed.</summary>
public sealed class RecordEditorViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _notes = string.Empty;
    private string _tagsText = string.Empty;
    private bool _isFavorite;
    private string _titleError = string.Empty;
    private bool _isDirty;

    public RecordEditorViewModel(RecordType recordType, RecordItem? existing)
    {
        RecordType = recordType;
        RecordId = existing?.Id ?? 0;

        if (existing is not null)
        {
            _title = existing.Title;
            _notes = existing.Notes;
            _tagsText = string.Join(", ", existing.Tags);
            _isFavorite = existing.IsFavorite;
        }

        foreach (var field in recordType.Fields)
        {
            var value = existing is not null
                ? existing.GetValue(field.Id)
                : FieldValueFormat.ResolveDefault(field);

            var editor = new FieldEditorViewModel(field, value);
            editor.PropertyChanged += OnFieldChanged;
            Fields.Add(editor);
        }

        RecalculateComputedFields();

        // A brand-new record starts clean even though defaults were filled in,
        // so closing it straight away does not trigger a pointless save prompt.
        _isDirty = false;
    }

    /// <summary>What this type calls its identifying field, for example "Item".</summary>
    public string TitleLabel => RecordType.EffectiveTitleLabel;

    /// <summary>Heading for the panel holding the record's own fields.</summary>
    public string DetailsHeading => $"{RecordType.Name} details";

    /// <summary>Shown under the label. Short, because the marker beside it already says "required".</summary>
    public string TitleHelp => "What the record list shows, and what the search looks at first.";

    /// <summary>The fuller version, for the screen reader.</summary>
    public string TitleHelpSpoken =>
        $"Required. The {TitleLabel.ToLowerInvariant()} is what the record list shows " +
        "and what the search looks at first.";

    public RecordType RecordType { get; }

    public long RecordId { get; private set; }

    public bool IsNew => RecordId == 0;

    public ObservableCollection<FieldEditorViewModel> Fields { get; } = new();

    public ObservableCollection<Attachment> Attachments { get; } = new();

    public string HeadingText => IsNew
        ? $"New {RecordType.Name.ToLowerInvariant()}"
        : $"Editing {RecordType.Name.ToLowerInvariant()}: {Title}";

    /// <summary>What the screen reader says when the editor opens.</summary>
    public string OpeningAnnouncement
    {
        get
        {
            var fieldCount = Fields.Count + 1; // the identifying field counts too
            var required = Fields.Count(f => f.IsRequired) + 1;
            var calculated = Fields.Count(f => f.IsCalculated);

            var worked = calculated switch
            {
                0 => string.Empty,
                1 => " One field is worked out for you.",
                _ => $" {calculated} fields are worked out for you."
            };

            return IsNew
                ? $"New {RecordType.Name} record. {fieldCount} fields, {required} required.{worked} " +
                  "Press Tab to move between fields, Control plus S to save, Escape to cancel."
                : $"Editing {Title}. {fieldCount} fields.{worked} " +
                  "Press Control plus S to save, Escape to go back to the list.";
        }
    }

    public bool HasComputedFields => Fields.Any(f => f.IsCalculated);

    /// <summary>
    /// The worked-out fields read as one short sentence, for announcing after the user
    /// leaves a field that feeds a formula. Announcing on every keystroke would be noise;
    /// announcing once the number has settled is what is actually useful.
    /// </summary>
    public string ComputedSummary => string.Join(". ",
        Fields.Where(f => f.IsCalculated && f.ComputedText.Length > 0)
              .Select(f => $"{f.Label} {f.ComputedText}"));

    /// <summary>
    /// Works out every calculated field from the others.
    ///
    /// Runs more than one pass so a calculated field may itself be built on another one,
    /// and stops as soon as a pass changes nothing, which also stops a circular formula
    /// from looping forever.
    /// </summary>
    public void RecalculateComputedFields()
    {
        var computed = Fields.Where(f => f.IsCalculated).ToList();
        if (computed.Count == 0) return;

        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Fields.Where(f => !f.IsCalculated))
        {
            if (field.TryGetNumber(out var number)) values[field.Field.Name] = number;
        }

        for (var pass = 0; pass < FormulaEvaluator.MaxDependencyPasses; pass++)
        {
            var changed = false;

            foreach (var field in computed)
            {
                var worked = FormulaEvaluator.TryEvaluate(
                    field.Field.Formula, values, out var result, out var problem);

                field.SetComputed(worked ? result : null, worked ? string.Empty : problem);

                if (worked)
                {
                    if (!values.TryGetValue(field.Field.Name, out var previous) ||
                        Math.Abs(previous - result) > 1e-9)
                    {
                        values[field.Field.Name] = result;
                        changed = true;
                    }
                }
                else if (values.Remove(field.Field.Name))
                {
                    changed = true;
                }
            }

            if (!changed) break;
        }

        OnPropertyChanged(nameof(ComputedSummary));
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                MarkDirty();
                TitleError = string.Empty;
                OnPropertyChanged(nameof(HeadingText));
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set { if (SetProperty(ref _notes, value)) MarkDirty(); }
    }

    public string TagsText
    {
        get => _tagsText;
        set { if (SetProperty(ref _tagsText, value)) MarkDirty(); }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (SetProperty(ref _isFavorite, value)) MarkDirty(); }
    }

    public string TitleError
    {
        get => _titleError;
        private set
        {
            if (SetProperty(ref _titleError, value))
            {
                OnPropertyChanged(nameof(HasTitleError));
                OnPropertyChanged(nameof(TitleAccessibleName));
            }
        }
    }

    public bool HasTitleError => TitleError.Length > 0;

    public string TitleAccessibleName => HasTitleError
        ? $"{TitleLabel}, required, has an error: {TitleError}"
        : $"{TitleLabel}, required";

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public int AttachmentCount => Attachments.Count;

    public bool HasAttachments => Attachments.Count > 0;

    public string AttachmentsHeading => Attachments.Count switch
    {
        0 => "Attached files: none",
        1 => "Attached files: 1",
        _ => $"Attached files: {Attachments.Count}"
    };

    public void RefreshAttachmentsHeading()
    {
        OnPropertyChanged(nameof(AttachmentsHeading));
        OnPropertyChanged(nameof(AttachmentCount));
        OnPropertyChanged(nameof(HasAttachments));
    }

    /// <summary>
    /// Validates every field. On failure, <paramref name="firstInvalid"/> is the field
    /// the caller should move keyboard focus to, and null means the title is at fault.
    /// Moving focus to the problem is what tells a screen-reader user where to look.
    /// </summary>
    public bool Validate(out FieldEditorViewModel? firstInvalid, out string summary)
    {
        firstInvalid = null;
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Title))
        {
            TitleError = $"{TitleLabel} is required. It is how you will find this record again.";
            problems.Add(TitleError);
        }

        // Make sure the stored totals match what the inputs now say.
        RecalculateComputedFields();

        foreach (var field in Fields)
        {
            if (field.TryGetCanonicalValue(out _)) continue;

            problems.Add(field.ErrorMessage);
            firstInvalid ??= field;
        }

        if (problems.Count == 0)
        {
            summary = string.Empty;
            return true;
        }

        summary = problems.Count == 1
            ? problems[0]
            : $"{problems.Count} fields need attention. {problems[0]}";

        return false;
    }

    /// <summary>Builds the record to hand to the repository. Call only after Validate succeeds.</summary>
    public RecordItem ToRecordItem()
    {
        var record = new RecordItem
        {
            Id = RecordId,
            RecordTypeId = RecordType.Id,
            RecordTypeName = RecordType.Name,
            Title = Title.Trim(),
            Notes = Notes ?? string.Empty,
            IsFavorite = IsFavorite,
            Tags = ParseTags(TagsText)
        };

        foreach (var field in Fields)
        {
            if (field.TryGetCanonicalValue(out var canonical) && canonical.Length > 0)
                record.Values[field.Field.Id] = canonical;
        }

        return record;
    }

    public void AcceptSaved(long newId)
    {
        RecordId = newId;
        IsDirty = false;
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(HeadingText));
    }

    public void SetTitleError(string message) => TitleError = message;

    /// <summary>Splits the tag box on commas, trimming blanks and duplicates.</summary>
    public static List<string> ParseTags(string? text) =>
        (text ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FieldEditorViewModel.TextValue)
                           or nameof(FieldEditorViewModel.BoolValue)
                           or nameof(FieldEditorViewModel.SelectedChoice))
        {
            MarkDirty();

            // A number the user just changed may feed a total, so keep them in step.
            if (sender is FieldEditorViewModel { IsCalculated: false }) RecalculateComputedFields();
        }
    }

    private void MarkDirty() => IsDirty = true;
}
