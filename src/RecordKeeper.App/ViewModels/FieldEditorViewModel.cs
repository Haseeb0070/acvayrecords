using System.Globalization;
using RecordKeeper.App.Mvvm;
using RecordKeeper.Core.Models;

namespace RecordKeeper.App.ViewModels;

/// <summary>
/// One editable field on the record editor.
///
/// Dates are edited as plain text rather than with a date-picker control on purpose.
/// A picker is a calendar grid, which is slow and fiddly to drive with a screen reader,
/// whereas a text box accepts "27/10/2026" or simply the word "today" and reads back
/// cleanly. The spoken hint in <see cref="AccessibleHelp"/> says so.
///
/// A field with a formula is read-only: the app works it out from the other fields and
/// keeps it up to date as they are typed.
/// </summary>
public sealed class FieldEditorViewModel : ObservableObject
{
    private string _textValue = string.Empty;
    private bool _boolValue;
    private string? _selectedChoice;
    private string _errorMessage = string.Empty;

    private string _computedCanonical = string.Empty;
    private string _computedText = string.Empty;
    private string _computedProblem = string.Empty;

    public FieldEditorViewModel(FieldDef field, string canonicalValue)
    {
        Field = field;

        Choices = new List<string> { string.Empty };
        Choices.AddRange(field.ChoiceList);

        LoadFrom(canonicalValue);
    }

    public FieldDef Field { get; }

    public string Label => Field.Name;

    public bool IsRequired => Field.IsRequired && !IsCalculated;

    /// <summary>Shown next to the label, and spoken, so the user is never surprised.</summary>
    public string RequiredMarker => IsCalculated
        ? "worked out automatically"
        : Field.IsRequired ? "required" : string.Empty;

    public List<string> Choices { get; }

    /// <summary>True when a formula fills this field in instead of the user.</summary>
    public bool IsCalculated => Field.IsCalculated;

    // Which editor control the template should show. Exactly one of these is true.
    public bool IsLongText => !IsCalculated && Field.DataType == FieldDataType.LongText;
    public bool IsBoolean => !IsCalculated && Field.DataType == FieldDataType.Boolean;
    public bool IsChoice => !IsCalculated && Field.DataType == FieldDataType.Choice;
    public bool IsPlainText => !IsCalculated && !IsLongText && !IsBoolean && !IsChoice;

    public string TextValue
    {
        get => _textValue;
        set
        {
            if (SetProperty(ref _textValue, value)) ClearError();
        }
    }

    public bool BoolValue
    {
        get => _boolValue;
        set => SetProperty(ref _boolValue, value);
    }

    public string? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (SetProperty(ref _selectedChoice, value)) ClearError();
        }
    }

    /// <summary>The worked-out value as it should be shown, for a calculated field.</summary>
    public string ComputedText
    {
        get => _computedText;
        private set
        {
            if (SetProperty(ref _computedText, value)) OnPropertyChanged(nameof(AccessibleName));
        }
    }

    /// <summary>
    /// Why a calculated field is currently blank, for example because one of the fields
    /// it depends on has not been filled in yet. Shown and spoken as a hint, not an error.
    /// </summary>
    public string ComputedProblem
    {
        get => _computedProblem;
        private set
        {
            if (SetProperty(ref _computedProblem, value)) OnPropertyChanged(nameof(HasComputedProblem));
        }
    }

    public bool HasComputedProblem => ComputedProblem.Length > 0;

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>
    /// The name a screen reader reads for the control. Required-ness, the current error
    /// and, for a calculated field, the worked-out value are all part of the name rather
    /// than separate visual cues, because a colour or an icon carries nothing to someone
    /// who cannot see it.
    /// </summary>
    public string AccessibleName
    {
        get
        {
            var name = Field.Name;

            if (IsCalculated)
            {
                name += ", worked out automatically, read only";
                name += ComputedText.Length > 0 ? ", " + ComputedText : ", not yet available";
                return name;
            }

            if (Field.IsRequired) name += ", required";
            if (HasError) name += ", has an error: " + ErrorMessage;
            return name;
        }
    }

    /// <summary>
    /// How wide the input should be allowed to get.
    ///
    /// A quantity box stretched across a thousand pixels looks wrong and is harder to
    /// use than a short one: the eye has to travel from the label to a caret far away.
    /// Sizing the box roughly to the value it holds is most of what makes a form look
    /// considered rather than generated. These are maximums, so the fields still shrink
    /// on a narrow window or at a large text size.
    /// </summary>
    public double InputMaxWidth => Field.DataType switch
    {
        // A quantity or a date is a handful of characters; a box four times wider than
        // the value is the thing that makes a form look thrown together.
        FieldDataType.Number or FieldDataType.Money or FieldDataType.Date => 300,
        FieldDataType.Choice => 400,

        // Text can use its whole column.
        _ => double.PositiveInfinity
    };

    /// <summary>
    /// The sentence shown under the label on screen: the author's own explanation of
    /// the field, and nothing else.
    ///
    /// The format hints, the list of choices and the read-only note all live in
    /// <see cref="AccessibleHelp"/> instead. A screen reader reads them either way, and
    /// printing them as well turned every field into two lines of prose and let only
    /// four fields fit on screen at once.
    /// </summary>
    public string VisibleHelp => (Field.HelpText ?? string.Empty).Trim();

    /// <summary>The longer explanation, exposed as UI Automation help text.</summary>
    public string AccessibleHelp
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Field.HelpText)) parts.Add(Field.HelpText.Trim());

            if (IsCalculated)
            {
                parts.Add(FieldDataTypeInfo.CalculatedHint(Field.Formula));
                if (HasComputedProblem) parts.Add(ComputedProblem);
                return string.Join(" ", parts);
            }

            var hint = FieldDataTypeInfo.InputHint(Field.DataType);
            if (hint.Length > 0) parts.Add(hint);

            if (Field.DataType == FieldDataType.Choice && Field.ChoiceList.Count > 0)
                parts.Add("Options are " + string.Join(", ", Field.ChoiceList) + ".");

            return string.Join(" ", parts);
        }
    }

    /// <summary>True when the user has left a field empty. A calculated field is never empty.</summary>
    public bool IsEmpty => Field.DataType switch
    {
        _ when IsCalculated => false,
        FieldDataType.Boolean => false,
        FieldDataType.Choice => string.IsNullOrWhiteSpace(SelectedChoice),
        _ => string.IsNullOrWhiteSpace(TextValue)
    };

    /// <summary>
    /// Reads this field as a number without validating or flagging anything. Used to
    /// feed the formulas while the user is still mid-keystroke, where setting an error
    /// on every incomplete entry would be nothing but noise.
    /// </summary>
    public bool TryGetNumber(out double value)
    {
        value = 0;

        if (IsCalculated)
        {
            return _computedCanonical.Length > 0 &&
                   double.TryParse(_computedCanonical, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        if (!FieldDataTypeInfo.IsNumeric(Field.DataType)) return false;
        if (string.IsNullOrWhiteSpace(TextValue)) return false;

        return FieldValueFormat.TryCanonicalize(Field.DataType, TextValue, out var canonical, out _) &&
               canonical.Length > 0 &&
               double.TryParse(canonical, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Stores a freshly worked-out value, or clears it with the reason why.</summary>
    public void SetComputed(double? value, string problem)
    {
        if (value is null)
        {
            _computedCanonical = string.Empty;
            ComputedText = string.Empty;
            ComputedProblem = problem;
        }
        else
        {
            var rounded = Field.DataType == FieldDataType.Money
                ? Math.Round(value.Value, 2, MidpointRounding.AwayFromZero)
                : value.Value;

            _computedCanonical = Field.DataType == FieldDataType.Money
                ? rounded.ToString("0.00", CultureInfo.InvariantCulture)
                : rounded.ToString("0.##########", CultureInfo.InvariantCulture);

            ComputedText = FieldValueFormat.ToDisplay(Field.DataType, _computedCanonical);
            ComputedProblem = string.Empty;
        }

        OnPropertyChanged(nameof(AccessibleHelp));
    }

    /// <summary>
    /// Checks the entry and produces the value to store.
    /// Returns false and sets <see cref="ErrorMessage"/> when it cannot be read.
    /// </summary>
    public bool TryGetCanonicalValue(out string canonical)
    {
        if (IsCalculated)
        {
            // A calculated field cannot be wrong, only not yet available.
            canonical = _computedCanonical;
            return true;
        }

        canonical = string.Empty;

        if (Field.DataType == FieldDataType.Boolean)
        {
            canonical = BoolValue ? "true" : "false";
            return true;
        }

        var raw = IsChoice ? SelectedChoice ?? string.Empty : TextValue;

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Field.IsRequired)
            {
                ErrorMessage = $"{Field.Name} is required and cannot be left empty.";
                return false;
            }
            return true;
        }

        if (!FieldValueFormat.TryCanonicalize(Field.DataType, raw, out canonical, out var error))
        {
            ErrorMessage = error;
            return false;
        }

        return true;
    }

    private void LoadFrom(string canonicalValue)
    {
        if (IsCalculated)
        {
            // Show whatever was stored last time until the first recalculation runs.
            _computedCanonical = canonicalValue;
            _computedText = FieldValueFormat.ToDisplay(Field.DataType, canonicalValue);
            return;
        }

        switch (Field.DataType)
        {
            case FieldDataType.Boolean:
                _boolValue = canonicalValue.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;

            case FieldDataType.Choice:
                _selectedChoice = canonicalValue;
                break;

            default:
                // Show the value the way the user would type it, not the stored form.
                _textValue = FieldValueFormat.ToDisplay(Field.DataType, canonicalValue);
                break;
        }
    }

    private void ClearError()
    {
        if (HasError)
        {
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(AccessibleName));
        }
    }

    public void SetError(string message)
    {
        ErrorMessage = message;
        OnPropertyChanged(nameof(AccessibleName));
    }
}
