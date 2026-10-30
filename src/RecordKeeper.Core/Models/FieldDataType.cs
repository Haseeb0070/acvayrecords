namespace RecordKeeper.Core.Models;

/// <summary>The kinds of value a custom field can hold.</summary>
public enum FieldDataType
{
    Text = 0,
    LongText = 1,
    Number = 2,
    Money = 3,
    Date = 4,
    Boolean = 5,
    Choice = 6
}

public static class FieldDataTypeInfo
{
    /// <summary>Human wording used in the UI and spoken by the screen reader.</summary>
    public static string Describe(FieldDataType type) => type switch
    {
        FieldDataType.Text => "Short text",
        FieldDataType.LongText => "Long text",
        FieldDataType.Number => "Number",
        FieldDataType.Money => "Amount of money",
        FieldDataType.Date => "Date",
        FieldDataType.Boolean => "Yes or no",
        FieldDataType.Choice => "Choice from a list",
        _ => type.ToString()
    };

    /// <summary>Spoken hint telling the user what format to type.</summary>
    public static string InputHint(FieldDataType type) => type switch
    {
        FieldDataType.Text => "Type a short line of text.",
        FieldDataType.LongText => "Type any amount of text. Press Enter for a new line.",
        FieldDataType.Number => "Type a number, for example 12 or 3.5.",
        FieldDataType.Money => "Type an amount, for example 1250.00.",
        FieldDataType.Date => "Type a date as day slash month slash year, or use the arrow keys to change it.",
        FieldDataType.Boolean => "Press Space to switch between yes and no.",
        FieldDataType.Choice => "Use the up and down arrow keys to pick an option.",
        _ => string.Empty
    };

    public static bool IsNumeric(FieldDataType type) =>
        type is FieldDataType.Number or FieldDataType.Money;

    /// <summary>Spoken hint for a field the app works out rather than the user typing.</summary>
    public static string CalculatedHint(string formula) =>
        "This is worked out for you and cannot be typed into. It is " +
        FormulaEvaluator.Describe(formula) + ".";
}
