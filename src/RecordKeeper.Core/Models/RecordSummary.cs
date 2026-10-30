using System.Globalization;

namespace RecordKeeper.Core.Models;

/// <summary>One field and what a set of records adds up to in it.</summary>
public sealed class FieldTotal
{
    public required FieldDef Field { get; init; }
    public required decimal Sum { get; init; }

    /// <summary>How many of the records had anything in this field.</summary>
    public required int Count { get; init; }

    /// <summary>The sum in the canonical stored form, ready to be formatted.</summary>
    public string Canonical => Field.DataType == FieldDataType.Money
        ? Math.Round(Sum, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)
        : Sum.ToString("0.##########", CultureInfo.InvariantCulture);

    public string Display => FieldValueFormat.ToDisplay(Field.DataType, Canonical);

    public string Spoken => FieldValueFormat.ToSpoken(Field.DataType, Canonical);
}

/// <summary>
/// Adds up a set of records that is already in hand.
///
/// The repository can total far more records than an export ever holds, straight in
/// the database. This is for the exports, which must describe exactly the records
/// they printed — including when those were narrowed by a search or a period — rather
/// than ask the database a second, subtly different question.
/// </summary>
public static class RecordSummary
{
    public static List<FieldTotal> Totals(IReadOnlyList<RecordItem> records, RecordType type)
    {
        var totals = new List<FieldTotal>();

        foreach (var field in type.Fields.Where(f => f.CountsInListTotals))
        {
            decimal sum = 0;
            var count = 0;

            foreach (var record in records)
            {
                var value = record.GetValue(field.Id);
                if (value.Length == 0) continue;

                if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                    continue;

                sum += amount;
                count++;
            }

            // A field nothing was entered in has no total. Printing zero would say the
            // records add up to nothing, which is not the same as nothing being there.
            if (count > 0) totals.Add(new FieldTotal { Field = field, Sum = sum, Count = count });
        }

        return totals;
    }
}
