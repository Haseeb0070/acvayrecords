using System.Windows;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.Views;

/// <summary>Adds or changes one custom field on a record type.</summary>
public partial class FieldEditWindow : Window
{
    private sealed record TypeChoice(FieldDataType Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly FieldDef _field;

    /// <summary>The other field names on this type, which a formula may refer to.</summary>
    private readonly List<string> _siblingNames;

    /// <summary>The group this field's category belongs to, or empty when ungrouped.</summary>
    private readonly string _groupName;

    public FieldEditWindow(FieldDef field, bool isNew, IEnumerable<string> siblingFieldNames,
                           string groupName = "")
    {
        InitializeComponent();

        _field = field;
        _groupName = groupName ?? string.Empty;
        _siblingNames = siblingFieldNames
            .Where(name => !name.Equals(field.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        HeadingText.Text = isNew ? "New field" : "Edit field: " + field.Name;
        Title = isNew ? "New field" : "Edit field";

        TypeBox.ItemsSource = Enum.GetValues<FieldDataType>()
            .Select(value => new TypeChoice(value, FieldDataTypeInfo.Describe(value)))
            .ToList();

        NameBox.Text = field.Name;
        TypeBox.SelectedItem = (TypeBox.ItemsSource as List<TypeChoice>)!
            .First(choice => choice.Value == field.DataType);
        RequiredBox.IsChecked = field.IsRequired;
        ShowInListBox.IsChecked = field.ShowInList;
        ChoicesBox.Text = field.Choices.Replace("\n", Environment.NewLine);
        DefaultBox.Text = field.DefaultValue;
        HelpBox.Text = field.HelpText;
        FormulaBox.Text = field.Formula;
        IsExpenseBox.IsChecked = field.IsExpense;
        TotalInListBox.IsChecked = field.TotalInList;

        TypeBox.SelectionChanged += (_, _) => UpdateForSelectedType();
        FormulaBox.TextChanged += (_, _) => UpdateForFormula();
        OkButton.Click += (_, _) => Commit();

        Loaded += (_, _) =>
        {
            UpdateForSelectedType();
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private FieldDataType SelectedType =>
        (TypeBox.SelectedItem as TypeChoice)?.Value ?? FieldDataType.Text;

    private bool HasFormula => FormulaBox.Text.Trim().Length > 0;

    /// <summary>
    /// Shows only the parts that apply to the chosen kind of value: options for a
    /// choice field, a formula for a number or an amount of money.
    /// </summary>
    private void UpdateForSelectedType()
    {
        var isChoice = SelectedType == FieldDataType.Choice;
        var canCalculate = FieldDataTypeInfo.IsNumeric(SelectedType);

        ChoicesPanel.Visibility = isChoice ? Visibility.Visible : Visibility.Collapsed;
        FormulaPanel.Visibility = canCalculate ? Visibility.Visible : Visibility.Collapsed;

        // Only a number or an amount of money can be added up, so both totalling
        // options are hidden for anything else rather than shown and quietly ignored.
        ExpensePanel.Visibility = canCalculate ? Visibility.Visible : Visibility.Collapsed;
        TotalPanel.Visibility = canCalculate ? Visibility.Visible : Visibility.Collapsed;

        TotalHintText.Text = SelectedType == FieldDataType.Money
            ? "A total price or an amount owed is worth a running total. A price per " +
              "piece usually is not: adding those up gives the sum of the prices."
            : "Worth a running total when the numbers are quantities of the same thing, " +
              "such as bags bought. Not worth one for a rate or a measurement.";

        ExpenseHintText.Text = _groupName.Length > 0
            ? $"Totalled across every category in the {_groupName} group."
            : "This category is not in a group yet, so there is nothing to total it " +
              "against. Put it in a group first, in Categories and their fields.";

        TypeHintText.Text = FieldDataTypeInfo.InputHint(SelectedType);

        UpdateForFormula();
    }

    /// <summary>
    /// A worked-out field is not typed into, so "must be filled in" and a starting
    /// value have no meaning for it. Disabling them is clearer than leaving controls
    /// on screen that quietly do nothing.
    /// </summary>
    private void UpdateForFormula()
    {
        var calculated = HasFormula && FieldDataTypeInfo.IsNumeric(SelectedType);

        RequiredBox.IsEnabled = !calculated;
        DefaultBox.IsEnabled = !calculated;

        if (FormulaPanel.Visibility != Visibility.Visible)
        {
            FormulaHintText.Text = string.Empty;
            return;
        }

        if (!calculated)
        {
            FormulaHintText.Text =
                _siblingNames.Count == 0
                    ? "Leave empty to type this field in yourself. There are no other fields to work it out from yet."
                    : "Leave empty to type this field in yourself. Fields you can use: " +
                      string.Join(", ", _siblingNames.Select(name => "[" + name + "]")) + ".";
            return;
        }

        if (FormulaEvaluator.TryValidate(FormulaBox.Text, _siblingNames, NameBox.Text.Trim(), out var error))
        {
            FormulaHintText.Text = "This field will be worked out for you: " +
                                   FormulaEvaluator.Describe(FormulaBox.Text) + ".";
        }
        else
        {
            FormulaHintText.Text = error;
        }
    }

    private void Commit()
    {
        var announcer = AppServices.Current.Announcer;

        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            announcer.SayError("The field needs a name.");
            NameBox.Focus();
            return;
        }

        if (SelectedType == FieldDataType.Choice &&
            ChoicesBox.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length == 0)
        {
            announcer.SayError("A choice field needs at least one option. Type one option on each line.");
            ChoicesBox.Focus();
            return;
        }

        var formula = FieldDataTypeInfo.IsNumeric(SelectedType) ? FormulaBox.Text.Trim() : string.Empty;

        if (formula.Length > 0 &&
            !FormulaEvaluator.TryValidate(formula, _siblingNames, name, out var formulaError))
        {
            announcer.SayError("The formula cannot be used. " + formulaError);
            FormulaBox.Focus();
            FormulaBox.SelectAll();
            return;
        }

        _field.Name = name;
        _field.DataType = SelectedType;
        _field.ShowInList = ShowInListBox.IsChecked == true;

        // Only a number or money can be added up; anything else never counts.
        _field.IsExpense = FieldDataTypeInfo.IsNumeric(SelectedType) && IsExpenseBox.IsChecked == true;
        _field.TotalInList = FieldDataTypeInfo.IsNumeric(SelectedType) && TotalInListBox.IsChecked == true;
        _field.HelpText = HelpBox.Text.Trim();
        _field.Formula = formula;

        // A worked-out field is never typed into, so these two cannot apply to it.
        _field.IsRequired = formula.Length == 0 && RequiredBox.IsChecked == true;
        _field.DefaultValue = formula.Length == 0 ? DefaultBox.Text.Trim() : string.Empty;

        // Stored with plain newlines so the database stays platform-neutral.
        _field.Choices = SelectedType == FieldDataType.Choice
            ? string.Join("\n", ChoicesBox.Text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : string.Empty;

        DialogResult = true;
    }
}
