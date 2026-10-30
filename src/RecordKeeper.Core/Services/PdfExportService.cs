using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RecordKeeper.Core.Data;
using RecordKeeper.Core.Models;

namespace RecordKeeper.Core.Services;

/// <summary>
/// Produces printable PDF reports.
///
/// A note on accessibility: this writes a visual PDF, not a tagged PDF/UA document,
/// so a screen reader will read it but without heading structure. The plain-text
/// export in <see cref="ExportService"/> is the screen-reader-friendly companion and
/// the app always offers both.
/// </summary>
public sealed class PdfExportService
{
    private readonly AttachmentRepository _attachments;
    private readonly Func<byte[]?>? _logoProvider;

    /// <param name="logoProvider">
    /// Supplies the acvaysoft logo as a PNG. A function rather than the bytes, so the
    /// logo is only rendered if a report is actually exported, and so this project
    /// stays free of any dependency on a UI framework. Null simply omits the logo.
    /// </param>
    public PdfExportService(AttachmentRepository attachments, Func<byte[]?>? logoProvider = null)
    {
        _attachments = attachments;
        _logoProvider = logoProvider;

        // QuestPDF requires the licence kind to be declared once before first use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>A table-style report listing many records, one row each.</summary>
    public string ExportList(IReadOnlyList<RecordItem> records, RecordType type, string filePath)
    {
        var columns = type.Fields.Where(f => f.ShowInList).Take(5).ToList();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontSize(10).FontFamily(Fonts.Calibri));

                ComposeHeader(page, $"{type.Name} records", $"{records.Count} {(records.Count == 1 ? "record" : "records")}");

                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(definition =>
                    {
                        definition.RelativeColumn(3);
                        foreach (var _ in columns) definition.RelativeColumn(2);
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header.Cell(), type.EffectiveTitleLabel);
                        foreach (var column in columns) HeaderCell(header.Cell(), column.Name);
                    });

                    foreach (var record in records)
                    {
                        BodyCell(table.Cell(), record.Title, bold: true);
                        foreach (var column in columns)
                            BodyCell(table.Cell(), FieldValueFormat.ToDisplay(column.DataType, record.GetValue(column.Id)));
                    }

                    // A totals row, for the columns that have one. A printed list of
                    // amounts that leaves the reader to add them up by hand is only
                    // half a report.
                    var totals = RecordSummary.Totals(records, type)
                        .ToDictionary(t => t.Field.Id);

                    if (columns.Any(c => totals.ContainsKey(c.Id)))
                    {
                        TotalCell(table.Cell(), "Total");

                        foreach (var column in columns)
                        {
                            TotalCell(table.Cell(),
                                totals.TryGetValue(column.Id, out var total) ? total.Display : string.Empty);
                        }
                    }
                });

                ComposeFooter(page);
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    /// <summary>A detailed report giving each record its own block of field-by-field rows.</summary>
    public string ExportDetails(IReadOnlyList<RecordItem> records, RecordType type, string filePath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontSize(11).FontFamily(Fonts.Calibri));

                ComposeHeader(page, type.Name,
                    $"{records.Count} {(records.Count == 1 ? "record" : "records")}");

                page.Content().PaddingVertical(12).Column(column =>
                {
                    column.Spacing(18);

                    foreach (var record in records)
                    {
                        column.Item().Column(block =>
                        {
                            block.Item().Text(record.Title).FontSize(14).SemiBold();
                            block.Item().PaddingTop(2).LineHorizontal(0.75f).LineColor(Colors.Grey.Medium);

                            block.Item().PaddingTop(6).Table(table =>
                            {
                                table.ColumnsDefinition(definition =>
                                {
                                    definition.ConstantColumn(150);
                                    definition.RelativeColumn();
                                });

                                foreach (var field in type.Fields)
                                {
                                    var value = FieldValueFormat.ToDisplay(field.DataType, record.GetValue(field.Id));
                                    if (value.Length == 0) continue;

                                    table.Cell().PaddingVertical(2).Text(field.Name).SemiBold();
                                    table.Cell().PaddingVertical(2).Text(value);
                                }

                                if (record.Tags.Count > 0)
                                {
                                    table.Cell().PaddingVertical(2).Text("Tags").SemiBold();
                                    table.Cell().PaddingVertical(2).Text(string.Join(", ", record.Tags));
                                }

                                var files = _attachments.GetForRecord(record.Id);
                                if (files.Count > 0)
                                {
                                    table.Cell().PaddingVertical(2).Text("Attached files").SemiBold();
                                    table.Cell().PaddingVertical(2)
                                        .Text(string.Join(", ", files.Select(f => f.FileName)));
                                }
                            });

                            if (!string.IsNullOrWhiteSpace(record.Notes))
                            {
                                block.Item().PaddingTop(6).Text("Notes").SemiBold();
                                block.Item().Text(record.Notes);
                            }
                        });
                    }
                });

                ComposeFooter(page);
            });
        }).GeneratePdf(filePath);

        return filePath;
    }

    /// <summary>
    /// The band at the top of every page: the acvaysoft mark and wordmark, then the
    /// report's own title. Repeated on each page, because a page of a report that has
    /// been separated from page one should still say where it came from.
    /// </summary>
    private void ComposeHeader(PageDescriptor page, string title, string subtitle)
    {
        var logo = TryGetLogo();

        page.Header().Column(column =>
        {
            column.Item().Row(row =>
            {
                if (logo is not null)
                {
                    row.ConstantItem(26).Height(26).Image(logo);
                    row.ConstantItem(9);
                }

                row.AutoItem().AlignMiddle().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(14));
                    text.Span(Brand.CompanyStrong).Bold().FontColor(Brand.DeepBlue);
                    text.Span(Brand.CompanyLight).Light().FontColor(Brand.MidBlue);
                });

                row.RelativeItem();

                row.AutoItem().AlignMiddle()
                    .Text(Brand.Product).FontSize(10).FontColor(Brand.MidBlue);
            });

            column.Item().PaddingTop(10).Text(title)
                .FontSize(18).Bold().FontColor(Brand.DeepBlue);

            column.Item().Text(subtitle).FontSize(10).FontColor(Colors.Grey.Darken2);

            column.Item().PaddingTop(5).LineHorizontal(1.5f).LineColor(Brand.Cyan);
        });
    }

    private static void ComposeFooter(PageDescriptor page)
    {
        page.Footer().PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.Grey.Darken2));
                text.Span(Brand.PdfFooterNote);
                text.Span("  ·  ");
                text.Span(DateTime.Now.ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.CurrentCulture));
            });

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.Grey.Darken2));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    /// <summary>
    /// Fetches the logo, swallowing any failure. A report is worth more than its
    /// letterhead, so a logo that cannot be produced is simply left out.
    /// </summary>
    private byte[]? TryGetLogo()
    {
        try { return _logoProvider?.Invoke(); }
        catch (Exception) { return null; }
    }

    private static void HeaderCell(IContainer cell, string text) =>
        cell.Background(Colors.Grey.Lighten3).Padding(5).Text(text).SemiBold();

    private static void BodyCell(IContainer cell, string text, bool bold = false)
    {
        var span = cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(text);
        if (bold) span.SemiBold();
    }

    /// <summary>The closing row of sums, marked off by a rule above it.</summary>
    private static void TotalCell(IContainer cell, string text) =>
        cell.BorderTop(1.2f).BorderColor(Colors.Grey.Darken1).Padding(5).Text(text).Bold();
}
