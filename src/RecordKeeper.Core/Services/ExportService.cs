using System.Globalization;
using System.Text;
using RecordKeeper.Core.Data;
using RecordKeeper.Core.Models;

namespace RecordKeeper.Core.Services;

/// <summary>
/// Writes records out to files the user can keep, print or send.
///
/// Three formats are offered on purpose. PDF is for printing and sharing. Plain text
/// is the format a screen reader handles best, so it is always available alongside
/// the PDF. CSV is for moving the data into a spreadsheet.
/// </summary>
public sealed class ExportService
{
    private readonly AttachmentRepository _attachments;

    public ExportService(AttachmentRepository attachments) => _attachments = attachments;

    /// <summary>
    /// Writes a plain-text report. Unlike the PDF this carries no layout, which is
    /// exactly why a screen reader reads it cleanly from top to bottom.
    /// </summary>
    public string ExportText(IReadOnlyList<RecordItem> records, RecordType type, string filePath)
    {
        var text = new StringBuilder();

        // The same letterhead as the PDF, in words, since this is the format a screen
        // reader handles best and it should still say where it came from.
        text.AppendLine(Brand.FullName);
        text.AppendLine();

        text.AppendLine(type.Name + " records");
        text.AppendLine(new string('=', type.Name.Length + 8));
        text.AppendLine();
        text.AppendLine($"Exported {DateTime.Now.ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.CurrentCulture)}");
        text.AppendLine($"{records.Count} {(records.Count == 1 ? "record" : "records")}");
        text.AppendLine();

        // The totals go at the top, not the foot: this is the format a screen reader
        // reads straight through, and the answer should not be at the end of a
        // hundred records.
        var totals = RecordSummary.Totals(records, type);
        if (totals.Count > 0)
        {
            text.AppendLine("Totals");
            text.AppendLine(new string('-', 40));

            foreach (var total in totals)
            {
                var coverage = total.Count == records.Count
                    ? string.Empty
                    : $"  (from {total.Count} of {records.Count} records)";

                text.AppendLine($"{total.Field.Name}: {total.Display}{coverage}");
            }

            text.AppendLine();
        }

        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];

            text.AppendLine($"Record {i + 1} of {records.Count}: {record.Title}");
            text.AppendLine(new string('-', 40));

            foreach (var field in type.Fields)
            {
                var value = FieldValueFormat.ToDisplay(field.DataType, record.GetValue(field.Id));
                if (value.Length == 0) continue;
                text.AppendLine($"{field.Name}: {value}");
            }

            if (record.Tags.Count > 0)
                text.AppendLine("Tags: " + string.Join(", ", record.Tags));

            if (!string.IsNullOrWhiteSpace(record.Notes))
            {
                text.AppendLine("Notes:");
                foreach (var line in record.Notes.Split('\n'))
                    text.AppendLine("  " + line.TrimEnd());
            }

            var attachments = _attachments.GetForRecord(record.Id);
            if (attachments.Count > 0)
            {
                text.AppendLine("Attached files:");
                foreach (var attachment in attachments)
                    text.AppendLine($"  {attachment.FileName} ({attachment.SizeSpoken})");
            }

            text.AppendLine();
        }

        File.WriteAllText(filePath, text.ToString(), Encoding.UTF8);
        return filePath;
    }

    /// <summary>Writes a spreadsheet-ready CSV with one row per record.</summary>
    public string ExportCsv(IReadOnlyList<RecordItem> records, RecordType type, string filePath)
    {
        var csv = new StringBuilder();

        var headers = new List<string> { type.EffectiveTitleLabel };
        headers.AddRange(type.Fields.Select(f => f.Name));
        headers.Add("Tags");
        headers.Add("Notes");
        headers.Add("Created");
        headers.Add("Last changed");
        csv.AppendLine(string.Join(",", headers.Select(Quote)));

        foreach (var record in records)
        {
            var cells = new List<string> { record.Title };
            cells.AddRange(type.Fields.Select(f =>
                FieldValueFormat.ToDisplay(f.DataType, record.GetValue(f.Id))));
            cells.Add(string.Join("; ", record.Tags));
            cells.Add(record.Notes ?? string.Empty);
            cells.Add(record.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            cells.Add(record.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            csv.AppendLine(string.Join(",", cells.Select(Quote)));
        }

        // The byte-order mark makes Excel open the file as UTF-8 without asking.
        File.WriteAllText(filePath, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return filePath;
    }

    private static string Quote(string? value)
    {
        var text = (value ?? string.Empty).Replace("\r\n", " ").Replace("\n", " ").Replace("\"", "\"\"");
        return "\"" + text + "\"";
    }

    /// <summary>Builds a sensible, collision-free file name inside the exports folder.</summary>
    public static string SuggestPath(string typeName, string extension)
    {
        Directory.CreateDirectory(AppPaths.ExportsDir);

        var safeName = string.Join("_", typeName.Split(Path.GetInvalidFileNameChars(),
            StringSplitOptions.RemoveEmptyEntries));
        if (safeName.Length == 0) safeName = "records";

        var stem = $"{safeName}-{DateTime.Now:yyyy-MM-dd-HHmm}";
        var path = Path.Combine(AppPaths.ExportsDir, stem + extension);

        for (var counter = 2; File.Exists(path); counter++)
            path = Path.Combine(AppPaths.ExportsDir, $"{stem} ({counter}){extension}");

        return path;
    }
}
