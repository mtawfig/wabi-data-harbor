using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Ai.Compilers;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Organizations;
using WabiInsight.Infrastructure.Ai;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>An Excel file on its way to the reader: the content (a temporary file removed when closed, or memory) and how many rows each panel's sheet holds.</summary>
public sealed record WholeListExcelFile(Stream Content, IReadOnlyList<long> PanelRows);

/// <summary>
/// "View all in Excel" (P13.50). The workbook of an answer whose list stopped at the size limit gets every row of
/// that list: the saved plan is validated and authorized again for the person exporting (their permissions and row
/// scope today, never those of whoever asked), compiled without a row limit, and read from the source row by row
/// straight into the sheet, so a list of any length never sits in memory. A sheet holds what Excel allows:
/// 1,048,575 rows under its header. Sheets of panels that were whole are left as the answer saved them.
/// </summary>
public sealed class WholeListExcel
{
    private readonly IQueryPlanner _planner;
    private readonly ISqlDialectCompilerFactory _compilers;
    private readonly IQueryExecutor _executor;

    public WholeListExcel(IQueryPlanner planner, ISqlDialectCompilerFactory compilers, IQueryExecutor executor)
    {
        _planner = planner;
        _compilers = compilers;
        _executor = executor;
    }

    /// <summary>True when the answer has a list longer than the rows saved with it.</summary>
    public static bool Needed(AskResponse answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer.Panels.Any(p => p.Result.Truncated);
    }

    /// <summary>Whether the signed-in person may read the lists of this saved plan today. No source is queried.</summary>
    public async Task<bool> PermittedAsync(Guid applicationId, string planJson, CancellationToken cancellationToken)
    {
        try
        {
            await AuthorizeAsync(applicationId, planJson, cancellationToken);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    /// <param name="workbook">The workbook as the Excel exporter made it from the saved answer.</param>
    public async Task<WholeListExcelFile> BuildAsync(byte[] workbook, AskResponse answer, Application application, string planJson, bool rtl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(application);
        var counts = answer.Panels.Select(p => (long)p.Result.RowCount).ToList();
        if (!Needed(answer))
        {
            return new WholeListExcelFile(new MemoryStream(workbook, writable: false), counts);
        }

        var authorized = await AuthorizeAsync(application.ApplicationId, planJson, cancellationToken);
        if (authorized.Count != answer.Panels.Count)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "export_plan_changed");
        }

        var compiler = _compilers.Resolve(application.DatabaseType);
        var names = XlsxExporter.PanelSheetNames(answer, rtl);
        var file = new FileStream(Path.Combine(Path.GetTempPath(), $"wi-export-{Guid.NewGuid():N}.xlsx"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await file.WriteAsync(workbook, cancellationToken);
            file.Position = 0;
            using (var document = SpreadsheetDocument.Open(file, true))
            {
                var book = document.WorkbookPart ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "export_failed");
                var root = book.Workbook ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "export_failed");
                for (var i = 0; i < answer.Panels.Count; i++)
                {
                    var panel = answer.Panels[i];
                    if (!panel.Result.Truncated)
                    {
                        continue;
                    }

                    var sheet = root.Sheets?.Elements<Sheet>().FirstOrDefault(s => s.Name == names[i]);
                    if (sheet?.Id?.Value is not { } id || book.GetPartById(id) is not WorksheetPart part)
                    {
                        continue;
                    }

                    var headerStyle = HeaderStyle(part);
                    var headers = ColumnHeaders.For(panel, rtl);
                    var saved = panel.Result.Columns;
                    var width = saved.Count;
                    long written;
                    using (var writer = OpenXmlWriter.Create(part))
                    {
                        IReadOnlyList<ResultColumn> columns = saved;
                        written = await _executor.StreamAsync(application.ApplicationId, compiler.CompileAll(authorized[i]), AnalysisResult.ExcelRows,
                            read =>
                            {
                                // The columns the source gives now; their headings are the answer's own where the names still match.
                                columns = read;
                                width = read.Count;
                                writer.WriteStartElement(new Worksheet());
                                writer.WriteStartElement(new SheetViews());
                                writer.WriteStartElement(new SheetView { RightToLeft = rtl, WorkbookViewId = 0 });
                                writer.WriteElement(new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });
                                writer.WriteEndElement();
                                writer.WriteEndElement();
                                writer.WriteStartElement(new Columns());
                                writer.WriteElement(new Column { Min = 1, Max = (uint)Math.Max(1, read.Count), Width = 22, CustomWidth = true });
                                writer.WriteEndElement();
                                writer.WriteStartElement(new SheetData());
                                writer.WriteStartElement(new Row());
                                foreach (var column in read)
                                {
                                    writer.WriteElement(TextCell(ColumnHeaders.Header(headers, column.Name), headerStyle));
                                }

                                writer.WriteEndElement();
                                return ValueTask.CompletedTask;
                            },
                            row =>
                            {
                                writer.WriteStartElement(new Row());
                                for (var c = 0; c < row.Length; c++)
                                {
                                    writer.WriteElement(ValueCell(row[c], c < columns.Count && columns[c].IsNumeric));
                                }

                                writer.WriteEndElement();
                                return ValueTask.CompletedTask;
                            },
                            cancellationToken);
                        writer.WriteEndElement(); // sheetData
                        var range = $"A1:{ColumnLetters(Math.Max(1, width))}{(written + 1).ToString(CultureInfo.InvariantCulture)}";
                        writer.WriteElement(new AutoFilter { Reference = range });
                        writer.WriteEndElement(); // worksheet
                        FilterName(root, sheet, names[i], range);
                    }

                    counts[i] = written;
                }

                root.Save();
            }

            await file.FlushAsync(cancellationToken);
            file.Position = 0;
            return new WholeListExcelFile(file, counts);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private async Task<IReadOnlyList<AuthorizedPlan>> AuthorizeAsync(Guid applicationId, string planJson, CancellationToken cancellationToken)
    {
        try
        {
            return await _planner.AuthorizeStoredAsync(applicationId, QueryPlanner.Parse(planJson), cancellationToken);
        }
        catch (Exception ex) when (ex is PlanRejectedException or System.Text.Json.JsonException || (ex is DomainException domain && domain.Message == "ai_no_permitted_objects"))
        {
            // The plan no longer passes for this person (a permission was removed, or it never was theirs): nothing is read.
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "export_not_permitted");
        }
    }

    /// <summary>The style of the sheet's first cell (the bold, shaded heading the exporter gave it), read without loading the sheet.</summary>
    private static uint? HeaderStyle(WorksheetPart part)
    {
        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            if (reader.ElementType == typeof(Cell) && reader.IsStartElement)
            {
                var style = reader.Attributes.FirstOrDefault(a => a.LocalName == "s").Value;
                return uint.TryParse(style, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;
            }
        }

        return null;
    }

    /// <summary>The filter Excel keeps for the sheet is a named range in the workbook: it follows the new last row.</summary>
    private static void FilterName(Workbook book, Sheet sheet, string sheetName, string range)
    {
        var sheets = book.Sheets?.Elements<Sheet>().ToList() ?? [];
        var index = sheets.IndexOf(sheet);
        var name = book.DefinedNames?.Elements<DefinedName>().FirstOrDefault(d => d.Name == "_xlnm._FilterDatabase" && d.LocalSheetId?.Value == (uint)index);
        if (name is null)
        {
            return;
        }

        var parts = range.Split(':');
        static string Absolute(string cell)
        {
            var letters = new string(cell.TakeWhile(char.IsLetter).ToArray());
            return $"${letters}${cell[letters.Length..]}";
        }

        name.Text = $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'!{Absolute(parts[0])}:{Absolute(parts[1])}";
    }

    private static Cell ValueCell(object? value, bool numeric)
    {
        switch (value)
        {
            case null:
                return new Cell();
            case bool flag:
                return new Cell { DataType = CellValues.Boolean, CellValue = new CellValue(flag ? "1" : "0") };
            case DateTime or DateTimeOffset or DateOnly:
                return TextCell(ResultCalculator.Format(value), null);
        }

        if (value is not string && ResultCalculator.ToDouble(value) is { } number && (numeric || value is int or long or short or byte or decimal or double or float) && !double.IsNaN(number) && !double.IsInfinity(number))
        {
            return new Cell { CellValue = new CellValue(value is decimal exact ? exact.ToString(CultureInfo.InvariantCulture) : number.ToString("R", CultureInfo.InvariantCulture)) };
        }

        return TextCell(ResultCalculator.Format(value), null);
    }

    private static Cell TextCell(string text, uint? style)
    {
        var cell = new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(Clean(text)) { Space = SpaceProcessingModeValues.Preserve }) };
        if (style is { } index)
        {
            cell.StyleIndex = index;
        }

        return cell;
    }

    /// <summary>A cell holds at most 32,767 characters, and only characters XML allows.</summary>
    private static string Clean(string text)
    {
        if (text.Length > 32767)
        {
            text = text[..32767];
        }

        if (text.All(c => System.Xml.XmlConvert.IsXmlChar(c)))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (System.Xml.XmlConvert.IsXmlChar(text[i]))
            {
                builder.Append(text[i]);
            }
            else if (i + 1 < text.Length && System.Xml.XmlConvert.IsXmlSurrogatePair(text[i + 1], text[i]))
            {
                builder.Append(text[i]).Append(text[++i]);
            }
        }

        return builder.ToString();
    }

    private static string ColumnLetters(int column)
    {
        var letters = string.Empty;
        while (column > 0)
        {
            var remainder = (column - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            column = (column - 1) / 26;
        }

        return letters;
    }
}
