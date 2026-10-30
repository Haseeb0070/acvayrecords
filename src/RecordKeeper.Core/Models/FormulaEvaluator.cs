using System.Globalization;
using System.Text;

namespace RecordKeeper.Core.Models;

/// <summary>
/// Works out a calculated field from the other fields on the same record.
///
/// A formula references fields by name in square brackets and supports the four
/// arithmetic operators and parentheses, for example:
///
///     [Quantity] * [Price per piece]
///     [Total amount] - [Paid amount]
///     ([Price] - [Expense]) * 0.9
///
/// Deliberately not a general expression language. A small, predictable grammar is
/// one the error messages can explain in plain words, which matters when those
/// messages are going to be read aloud.
/// </summary>
public static class FormulaEvaluator
{
    /// <summary>How deep a chain of calculated fields depending on each other may go.</summary>
    public const int MaxDependencyPasses = 4;

    /// <summary>
    /// Works out the formula. Returns false when a referenced field is empty or
    /// unreadable, which is the normal case while a record is half filled in, so
    /// <paramref name="error"/> is wording for a hint rather than a fault.
    /// </summary>
    public static bool TryEvaluate(
        string? formula,
        IReadOnlyDictionary<string, double> fieldValues,
        out double result,
        out string error)
    {
        result = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(formula))
        {
            error = "There is no formula to work out.";
            return false;
        }

        List<Token> tokens;
        try
        {
            tokens = Tokenize(formula);
        }
        catch (FormulaException exception)
        {
            error = exception.Message;
            return false;
        }

        var parser = new Parser(tokens, fieldValues);
        try
        {
            result = parser.ParseExpression();
            parser.ExpectEnd();

            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                error = "The result is not a number. Check for a division by zero.";
                return false;
            }

            return true;
        }
        catch (FormulaException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>The field names a formula depends on, in the order they appear.</summary>
    public static List<string> ReferencedFields(string? formula)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(formula)) return names;

        try
        {
            foreach (var token in Tokenize(formula))
            {
                if (token.Kind == TokenKind.Field &&
                    !names.Contains(token.Text, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(token.Text);
                }
            }
        }
        catch (FormulaException)
        {
            // A formula that will not even tokenise has no usable references.
        }

        return names;
    }

    /// <summary>
    /// Checks a formula the user has just typed, against the field names available on
    /// the record type. Catches a misspelled field name at the point it is entered
    /// rather than leaving a field silently blank on every record.
    /// </summary>
    public static bool TryValidate(
        string? formula,
        IEnumerable<string> knownFieldNames,
        string ownFieldName,
        out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(formula))
        {
            error = "Type a formula, for example [Quantity] times [Price per piece].";
            return false;
        }

        List<Token> tokens;
        try
        {
            tokens = Tokenize(formula);
        }
        catch (FormulaException exception)
        {
            error = exception.Message;
            return false;
        }

        var known = knownFieldNames.ToList();
        var references = ReferencedFields(formula);

        if (references.Count == 0)
        {
            error = "The formula does not use any field. Put a field name in square brackets, " +
                    "for example [Quantity].";
            return false;
        }

        foreach (var reference in references)
        {
            if (reference.Equals(ownFieldName, StringComparison.OrdinalIgnoreCase))
            {
                error = $"The formula refers to [{reference}], which is this field itself. " +
                        "A field cannot be worked out from its own value.";
                return false;
            }

            if (!known.Any(name => name.Equals(reference, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"There is no field called [{reference}] on this record type. " +
                        "Available fields are: " + string.Join(", ", known) + ".";
                return false;
            }
        }

        // Check the shape parses, using 1 for every field so division by zero cannot
        // mask a syntax problem.
        var probe = references.ToDictionary(name => name, _ => 1.0, StringComparer.OrdinalIgnoreCase);
        if (!TryEvaluate(formula, probe, out _, out var evaluationError))
        {
            error = evaluationError;
            return false;
        }

        return true;
    }

    /// <summary>A formula written out in words, for the screen reader.</summary>
    public static string Describe(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return string.Empty;

        var spoken = new StringBuilder();

        foreach (var character in formula)
        {
            spoken.Append(character switch
            {
                '*' => " times ",
                '/' => " divided by ",
                '+' => " plus ",
                '-' => " minus ",
                '(' => " open bracket ",
                ')' => " close bracket ",
                '[' => "",
                ']' => "",
                _ => character.ToString()
            });
        }

        return System.Text.RegularExpressions.Regex.Replace(spoken.ToString(), @"\s+", " ").Trim();
    }

    // ================================================================= tokenizing

    private enum TokenKind { Number, Field, Plus, Minus, Multiply, Divide, OpenParen, CloseParen }

    private readonly record struct Token(TokenKind Kind, string Text, double Number);

    private static List<Token> Tokenize(string formula)
    {
        var tokens = new List<Token>();
        var index = 0;

        while (index < formula.Length)
        {
            var character = formula[index];

            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            switch (character)
            {
                case '+': tokens.Add(new Token(TokenKind.Plus, "+", 0)); index++; continue;
                case '-': tokens.Add(new Token(TokenKind.Minus, "-", 0)); index++; continue;
                case '*': tokens.Add(new Token(TokenKind.Multiply, "*", 0)); index++; continue;
                case 'x' or 'X' when NextIsOperand(formula, index + 1):
                    // "x" is how people write multiplication by hand.
                    tokens.Add(new Token(TokenKind.Multiply, "*", 0)); index++; continue;
                case '/': tokens.Add(new Token(TokenKind.Divide, "/", 0)); index++; continue;
                case '(': tokens.Add(new Token(TokenKind.OpenParen, "(", 0)); index++; continue;
                case ')': tokens.Add(new Token(TokenKind.CloseParen, ")", 0)); index++; continue;
            }

            if (character == '[')
            {
                var close = formula.IndexOf(']', index + 1);
                if (close < 0)
                {
                    throw new FormulaException(
                        "A field name was opened with a square bracket but never closed. " +
                        "Field names look like [Quantity].");
                }

                var name = formula[(index + 1)..close].Trim();
                if (name.Length == 0)
                    throw new FormulaException("There is an empty pair of square brackets in the formula.");

                tokens.Add(new Token(TokenKind.Field, name, 0));
                index = close + 1;
                continue;
            }

            if (char.IsDigit(character) || character == '.')
            {
                var start = index;
                while (index < formula.Length && (char.IsDigit(formula[index]) || formula[index] == '.')) index++;

                var text = formula[start..index];
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new FormulaException($"\"{text}\" is not a number the formula can use.");

                tokens.Add(new Token(TokenKind.Number, text, number));
                continue;
            }

            throw new FormulaException(
                $"The formula contains \"{character}\", which is not something it understands. " +
                "Use field names in square brackets, numbers, and plus, minus, times or divide.");
        }

        if (tokens.Count == 0) throw new FormulaException("The formula is empty.");

        return tokens;
    }

    private static bool NextIsOperand(string formula, int index)
    {
        while (index < formula.Length && char.IsWhiteSpace(formula[index])) index++;
        return index < formula.Length && (formula[index] == '[' || formula[index] == '(' ||
                                          char.IsDigit(formula[index]));
    }

    // ================================================================= parsing

    /// <summary>
    /// Recursive-descent parser over the token list, evaluating as it goes.
    /// Multiply and divide bind tighter than plus and minus, as expected.
    /// </summary>
    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly IReadOnlyDictionary<string, double> _values;
        private int _position;

        public Parser(List<Token> tokens, IReadOnlyDictionary<string, double> values)
        {
            _tokens = tokens;
            _values = values;
        }

        public double ParseExpression()
        {
            var value = ParseTerm();

            while (TryTake(TokenKind.Plus, TokenKind.Minus, out var op))
            {
                var right = ParseTerm();
                value = op == TokenKind.Plus ? value + right : value - right;
            }

            return value;
        }

        private double ParseTerm()
        {
            var value = ParseUnary();

            while (TryTake(TokenKind.Multiply, TokenKind.Divide, out var op))
            {
                var right = ParseUnary();

                if (op == TokenKind.Divide)
                {
                    if (right == 0) throw new FormulaException("The formula divides by zero.");
                    value /= right;
                }
                else
                {
                    value *= right;
                }
            }

            return value;
        }

        private double ParseUnary()
        {
            if (TryTake(TokenKind.Minus, TokenKind.Minus, out _)) return -ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            if (_position >= _tokens.Count)
                throw new FormulaException("The formula ends too early. Something is missing from the end.");

            var token = _tokens[_position++];

            switch (token.Kind)
            {
                case TokenKind.Number:
                    return token.Number;

                case TokenKind.Field:
                    if (_values.TryGetValue(token.Text, out var value)) return value;
                    throw new FormulaException($"{token.Text} is empty, so this cannot be worked out yet.");

                case TokenKind.OpenParen:
                    var inner = ParseExpression();
                    if (_position >= _tokens.Count || _tokens[_position].Kind != TokenKind.CloseParen)
                        throw new FormulaException("A bracket was opened but never closed.");
                    _position++;
                    return inner;

                default:
                    throw new FormulaException(
                        $"\"{token.Text}\" is in the wrong place in the formula.");
            }
        }

        public void ExpectEnd()
        {
            if (_position < _tokens.Count)
            {
                throw new FormulaException(
                    $"\"{_tokens[_position].Text}\" is left over at the end of the formula.");
            }
        }

        private bool TryTake(TokenKind first, TokenKind second, out TokenKind taken)
        {
            if (_position < _tokens.Count &&
                (_tokens[_position].Kind == first || _tokens[_position].Kind == second))
            {
                taken = _tokens[_position++].Kind;
                return true;
            }

            taken = default;
            return false;
        }
    }

    private sealed class FormulaException : Exception
    {
        public FormulaException(string message) : base(message) { }
    }
}
