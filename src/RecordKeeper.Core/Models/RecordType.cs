namespace RecordKeeper.Core.Models;

/// <summary>
/// A user-defined category of record, such as "Invoice" or "Contact",
/// together with the custom fields its records carry.
/// </summary>
public sealed class RecordType
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// What this type calls its identifying field, for example "Item" or "Customer name".
    ///
    /// Every record needs one required field that names it, which the list shows and the
    /// search looks at first. Letting the type label that field means the user fills in
    /// "Item" rather than both a generic "Title" and a near-identical "Item" field.
    /// Empty falls back to "Title".
    /// </summary>
    public string TitleLabel { get; set; } = string.Empty;

    public string EffectiveTitleLabel =>
        string.IsNullOrWhiteSpace(TitleLabel) ? "Title" : TitleLabel.Trim();

    /// <summary>The group this category belongs to, or null when it is ungrouped.</summary>
    public long? GroupId { get; set; }

    /// <summary>The group's name, filled in by the repository for display.</summary>
    public string GroupName { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public List<FieldDef> Fields { get; set; } = new();

    /// <summary>Count of live (not deleted) records, filled in by the repository for the navigation pane.</summary>
    public int RecordCount { get; set; }

    /// <summary>
    /// A copy of this type, fields and all, ready to be saved as a brand-new one.
    ///
    /// Ids are cleared so the repository inserts rather than updates. This is what makes
    /// "start from an existing category" safe: the copy shares no row with the original,
    /// so editing one cannot disturb the other.
    /// </summary>
    public RecordType CloneForNew(string newName) => new()
    {
        Id = 0,
        Name = newName,
        Description = Description,
        TitleLabel = TitleLabel,
        GroupId = GroupId,
        SortOrder = SortOrder,
        Fields = Fields.Select(field =>
        {
            var copy = field.Clone();
            copy.Id = 0;
            copy.RecordTypeId = 0;
            return copy;
        }).ToList()
    };

    public override string ToString() => Name;
}

/// <summary>One custom field belonging to a <see cref="RecordType"/>.</summary>
public sealed class FieldDef
{
    public long Id { get; set; }
    public long RecordTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public FieldDataType DataType { get; set; } = FieldDataType.Text;
    public bool IsRequired { get; set; }

    /// <summary>Options for <see cref="FieldDataType.Choice"/>, one per line.</summary>
    public string Choices { get; set; } = string.Empty;

    public string DefaultValue { get; set; } = string.Empty;

    /// <summary>Extra explanation. Exposed to the screen reader as the field's help text.</summary>
    public string HelpText { get; set; } = string.Empty;

    /// <summary>
    /// When set, this field is worked out from the other fields on the record instead
    /// of being typed, for example "[Quantity] * [Price per piece]". Only meaningful on
    /// a number or money field. See <see cref="FormulaEvaluator"/> for the grammar.
    /// </summary>
    public string Formula { get; set; } = string.Empty;

    /// <summary>True when this field is worked out rather than entered.</summary>
    public bool IsCalculated =>
        !string.IsNullOrWhiteSpace(Formula) && FieldDataTypeInfo.IsNumeric(DataType);

    /// <summary>
    /// Counts this field towards its group's expense total.
    ///
    /// Only meaningful on a number or an amount of money; a date or a tick box cannot
    /// be added up. A calculated field can be an expense, which is how a worked-out
    /// total price can feed the group's grand total.
    /// </summary>
    public bool IsExpense { get; set; }

    /// <summary>True when this field actually contributes to an expense total.</summary>
    public bool CountsAsExpense => IsExpense && FieldDataTypeInfo.IsNumeric(DataType);

    /// <summary>
    /// Adds this field up in the totals under the record list.
    ///
    /// Not every column of numbers is worth a sum. Adding up a price per piece gives
    /// the sum of the prices, which is a real number and almost never the one anyone
    /// wanted; adding up a total price gives what was spent. Only the user knows
    /// which of their fields is which, so they say.
    /// </summary>
    public bool TotalInList { get; set; }

    /// <summary>True when this field actually appears in the totals under the list.</summary>
    public bool CountsInListTotals => TotalInList && FieldDataTypeInfo.IsNumeric(DataType);

    public int SortOrder { get; set; }

    /// <summary>Whether this field appears as a column in the table view.</summary>
    public bool ShowInList { get; set; } = true;

    public IReadOnlyList<string> ChoiceList =>
        string.IsNullOrWhiteSpace(Choices)
            ? Array.Empty<string>()
            : Choices.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public FieldDef Clone() => (FieldDef)MemberwiseClone();

    public override string ToString() => Name;
}
