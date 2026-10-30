using System.Globalization;

namespace RecordKeeper.Core.Models;

/// <summary>
/// Works out every calculated field on a record from the values of the others.
///
/// This is the authoritative version, applied by the repository on every save. The
/// editor also recalculates as the user types, but that is for live feedback only —
/// having the rule here means a total cannot be stored stale no matter which code
/// path saved the record.
/// </summary>
public static class RecordCalculator
{
    /// <summary>
    /// Rewrites the calculated entries of <paramref name="values"/> in place.
    /// A field whose formula cannot be worked out yet, because something it depends on
    /// is empty, has its entry removed rather than being stored as zero: zero is a real
    /// amount and would be a lie.
    /// </summary>
    public static void ApplyFormulas(IReadOnlyList<FieldDef> fields, Dictionary<long, string> values)
    {
        var calculated = fields.Where(f => f.IsCalculated).ToList();
        if (calculated.Count == 0) return;

        var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields.Where(f => !f.IsCalculated && FieldDataTypeInfo.IsNumeric(f.DataType)))
        {
            if (!values.TryGetValue(field.Id, out var canonical) || canonical.Length == 0) continue;

            if (double.TryParse(canonical, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                byName[field.Name] = number;
        }

        // Several passes so one calculated field may be built on another. Stopping as
        // soon as a pass changes nothing also stops a circular formula looping.
        for (var pass = 0; pass < FormulaEvaluator.MaxDependencyPasses; pass++)
        {
            var changed = false;

            foreach (var field in calculated)
            {
                if (FormulaEvaluator.TryEvaluate(field.Formula, byName, out var result, out _))
                {
                    var canonical = Canonicalize(field.DataType, result);
                    if (!values.TryGetValue(field.Id, out var previous) || previous != canonical)
                    {
                        values[field.Id] = canonical;
                        changed = true;
                    }

                    if (!byName.TryGetValue(field.Name, out var seen) || Math.Abs(seen - result) > 1e-9)
                    {
                        byName[field.Name] = result;
                        changed = true;
                    }
                }
                else
                {
                    if (values.Remove(field.Id)) changed = true;
                    if (byName.Remove(field.Name)) changed = true;
                }
            }

            if (!changed) break;
        }
    }

    private static string Canonicalize(FieldDataType type, double value) =>
        type == FieldDataType.Money
            ? Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)
            : value.ToString("0.##########", CultureInfo.InvariantCulture);
}
