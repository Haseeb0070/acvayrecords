using System.Globalization;
using System.Text;
using RecordKeeper.App.Mvvm;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.ViewModels;

/// <summary>
/// One row in the record list.
///
/// The important member here is <see cref="AccessibleName"/>. In the list view each
/// record is a single list item, and this is the sentence Narrator reads for it. Packing
/// the useful fields into that one sentence means a user can arrow down a list and hear
/// what each record is, instead of having to open every one to find out.
/// </summary>
public sealed class RecordRowViewModel : ObservableObject
{
    private readonly List<FieldDef> _fields;

    public RecordRowViewModel(RecordItem record, IEnumerable<FieldDef> fields, bool showTypeName)
    {
        Record = record;
        _fields = fields.ToList();
        ShowTypeName = showTypeName;

        foreach (var field in _fields)
        {
            DisplayValues["f" + field.Id] =
                FieldValueFormat.ToDisplay(field.DataType, record.GetValue(field.Id));
        }
    }

    public RecordItem Record { get; }

    public long Id => Record.Id;
    public string Title => Record.Title;
    public string TypeName => Record.RecordTypeName;
    public bool ShowTypeName { get; }
    public bool IsFavorite => Record.IsFavorite;
    public int AttachmentCount => Record.AttachmentCount;

    public string TagsText => string.Join(", ", Record.Tags);

    // Short date and time in the user's own regional format, so these columns match
    // the date fields sitting beside them in the same table rather than using a
    // second, different-looking date style.
    public string ModifiedText =>
        Record.ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string DeletedText => Record.DeletedUtc is null
        ? string.Empty
        : Record.DeletedUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Field values keyed as "f" plus the field id, for the table columns to bind to.</summary>
    public Dictionary<string, string> DisplayValues { get; } = new();

    /// <summary>The second line shown under the title in the list view.</summary>
    public string SummaryLine
    {
        get
        {
            var parts = new List<string>();

            if (ShowTypeName && TypeName.Length > 0) parts.Add(TypeName);

            foreach (var field in _fields.Where(f => f.ShowInList).Take(3))
            {
                var value = DisplayValues.GetValueOrDefault("f" + field.Id, string.Empty);
                if (value.Length > 0) parts.Add($"{field.Name}: {value}");
            }

            if (Record.Tags.Count > 0) parts.Add("Tags: " + TagsText);

            return string.Join("   ·   ", parts);
        }
    }

    /// <summary>Short markers shown after the title: a star for favourite, a count for files.</summary>
    public string BadgeLine
    {
        get
        {
            var parts = new List<string>();
            if (IsFavorite) parts.Add("★ Favourite");
            if (AttachmentCount == 1) parts.Add("1 file");
            else if (AttachmentCount > 1) parts.Add($"{AttachmentCount} files");
            return string.Join("   ", parts);
        }
    }

    public bool HasBadges => BadgeLine.Length > 0;

    /// <summary>The full sentence read aloud for this row.</summary>
    public string AccessibleName
    {
        get
        {
            var spoken = new StringBuilder();
            spoken.Append(Record.Title);

            if (ShowTypeName && TypeName.Length > 0) spoken.Append(". Type ").Append(TypeName);

            foreach (var field in _fields.Where(f => f.ShowInList).Take(4))
            {
                var value = FieldValueFormat.ToSpoken(field.DataType, Record.GetValue(field.Id));
                if (value is "empty" or "") continue;
                spoken.Append(". ").Append(field.Name).Append(' ').Append(value);
            }

            if (Record.Tags.Count > 0)
                spoken.Append(". Tagged ").Append(string.Join(", ", Record.Tags));

            if (IsFavorite) spoken.Append(". Favourite");

            if (AttachmentCount == 1) spoken.Append(". 1 attached file");
            else if (AttachmentCount > 1) spoken.Append($". {AttachmentCount} attached files");

            if (Record.IsDeleted && DeletedText.Length > 0)
                spoken.Append(". In the recycle bin since ").Append(DeletedText);

            spoken.Append('.');
            return spoken.ToString();
        }
    }
}
