using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Analysis;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace WabiInsight.Infrastructure.Reports;

/// <summary>
/// DOCX export with the Open XML SDK (specification §20): title block, executive summary, key findings,
/// numbered sections with KPI tables, chart images (server-rendered PNG) and row tables, and recommendations;
/// bidirectional paragraphs and right-to-left tables for Arabic; logo and page numbers in the header and footer.
/// Every number comes from the panel data. Business content only: no methodology, query or row-count details.
/// </summary>
public sealed class DocxExporter : IAnswerExporter
{
    private const int MaxTableRows = 100;
    private const string LatinFont = "Inter";
    private const string ArabicFont = "IBM Plex Sans Arabic";

    public ExportFormat Format => ExportFormat.Docx;

    public string ContentType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public string Extension => "docx";

    public byte[] Export(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rtl = request.Language == "ar";
        var font = rtl ? ArabicFont : LatinFont;
        var content = request.Content;
        var culture = rtl ? CultureInfo.GetCultureInfo("ar-AE") : CultureInfo.InvariantCulture;
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;
            AddStyles(main, font);

            body.Append(Paragraph(content.Title.Length > 0 ? content.Title : "Report", rtl, "Title"));
            if (content.Subtitle.Length > 0)
            {
                body.Append(Paragraph(content.Subtitle, rtl, "Subtitle"));
            }

            body.Append(Paragraph($"{(rtl ? "أُعدّ لـ" : "Prepared for")} {request.OrganizationName} · {(rtl ? "المصدر" : "Source")}: {request.ApplicationName} · {(rtl ? "أُنشئ في" : "Generated")} {request.GeneratedAt:yyyy-MM-dd HH:mm} UTC", rtl, "Muted"));

            if (content.Summary.Length > 0)
            {
                body.Append(Paragraph(rtl ? "الملخص التنفيذي" : "Executive summary", rtl, "Heading1"));
                Paragraphs(body, content.Summary, rtl);
            }

            if (content.KeyFindings.Count > 0)
            {
                body.Append(Paragraph(rtl ? "أبرز النتائج" : "Key findings", rtl, "Heading1"));
                foreach (var finding in content.KeyFindings)
                {
                    body.Append(Paragraph("• " + finding, rtl, "Normal"));
                }
            }

            if (content.Introduction.Length > 0)
            {
                body.Append(Paragraph(rtl ? "المقدمة" : "Introduction", rtl, "Heading1"));
                Paragraphs(body, content.Introduction, rtl);
            }

            var number = 0;
            foreach (var section in content.Sections)
            {
                number++;
                body.Append(Paragraph($"{number.ToString(culture)}. {QuestPdfReportRenderer.StripNumbering(section.Title)}", rtl, "Heading1"));
                Paragraphs(body, section.Body, rtl);
                var kpis = section.Panels.Where(i => i < request.Answer.Panels.Count && request.Answer.Panels[i].Plan.Visualization?.Type == "kpi" && request.Answer.Panels[i].Result.Rows.Count > 0).ToList();
                if (kpis.Count > 0)
                {
                    body.Append(KpiTable(kpis.Select(i => request.Answer.Panels[i]).ToList(), rtl, culture));
                }

                foreach (var i in section.Panels.Where(i => i < request.Answer.Panels.Count && !kpis.Contains(i)))
                {
                    var panel = request.Answer.Panels[i];
                    var type = panel.Plan.Visualization?.Type ?? "table";
                    var isChart = type is "bar" or "line" or "pie" && panel.Result.Series.Count > 0;
                    body.Append(Paragraph($"{(rtl ? (isChart ? "شكل" : "جدول") : (isChart ? "Figure" : "Table"))} {number.ToString(culture)}.{(section.Panels.IndexOf(i) + 1).ToString(culture)} — {(panel.Title.Length > 0 ? panel.Title : panel.Plan.Objective)}", rtl, "Caption"));
                    if (isChart)
                    {
                        body.Append(Image(main, ChartImageRenderer.RenderImage(type, panel.Result.Series, culture: culture), rtl));
                    }
                    else
                    {
                        body.Append(RowsTable(panel, rtl, culture));
                    }
                }
            }

            if (content.Recommendations.Count > 0)
            {
                body.Append(Paragraph(rtl ? "التوصيات" : "Recommendations", rtl, "Heading1"));
                var n = 0;
                foreach (var rec in content.Recommendations)
                {
                    n++;
                    body.Append(Paragraph($"{n.ToString(culture)}. {rec.Title}", rtl, "Heading2"));
                    Paragraphs(body, rec.Detail, rtl);
                }
            }

            AddHeaderFooter(main, request, content.Title, rtl, font);
            body.Append(SectionProperties(main, rtl));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static void Paragraphs(Body body, string text, bool rtl)
    {
        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            body.Append(Paragraph(paragraph, rtl, "Normal"));
        }
    }

    private static Paragraph Paragraph(string text, bool rtl, string style)
    {
        var properties = new ParagraphProperties(new ParagraphStyleId { Val = style });
        if (rtl)
        {
            properties.Append(new BiDi());
            properties.Append(new Justification { Val = JustificationValues.Right });
        }

        var paragraph = new Paragraph(properties);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var run = new Run();
            if (rtl)
            {
                run.Append(new RunProperties(new RightToLeftText()));
            }

            if (i > 0)
            {
                run.Append(new Break());
            }

            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(run);
        }

        return paragraph;
    }

    private static Table KpiTable(List<AnalysisPanel> panels, bool rtl, CultureInfo culture)
    {
        var table = TableShell(rtl, panels.Count);
        var values = new TableRow();
        var labels = new TableRow();
        foreach (var panel in panels)
        {
            var numeric = panel.Result.Columns.ToList().FindIndex(c => c.IsNumeric);
            var value = QuestPdfReportRenderer.FormatValue(panel.Result.Rows[0][numeric >= 0 ? numeric : 0], true, culture);
            values.Append(Cell(value, rtl, bold: true, shade: "F1F7FF", center: true));
            labels.Append(Cell(panel.Title.Length > 0 ? panel.Title : panel.Plan.Objective, rtl, bold: false, shade: "F1F7FF", center: true));
        }

        table.Append(values, labels);
        return table;
    }

    /// <summary>The width a table may use: A4 (11906 twips) less the 1134-twip side margins, in points, less the borders.</summary>
    private const float TableWidthPoints = 480f;

    private static Table RowsTable(AnalysisPanel panel, bool rtl, CultureInfo culture)
    {
        var columns = panel.Result.Columns;
        var headers = columns.Select(c => QuestPdfReportRenderer.Label(c.Name)).ToList();
        var rows = panel.Result.Rows.Take(MaxTableRows)
            .Select(r => (IReadOnlyList<string>)columns.Select((c, i) => QuestPdfReportRenderer.FormatValue(i < r.Count ? r[i] : null, c.IsNumeric, culture)).ToList())
            .ToList();

        // The same content-based column widths as the PDF, fixed in the document so every word processor keeps them.
        var layout = QuestPdfReportRenderer.LayoutTable(headers, columns.Select(c => c.IsNumeric).ToList(), rows, TableWidthPoints);
        var widths = layout.Widths.Select(w => (int)Math.Floor(w * 20)).ToList();
        var font = (int)Math.Floor(layout.FontSize * 2);
        var headerFont = (int)Math.Floor(layout.HeaderFontSize * 2);
        var margin = (int)Math.Round(layout.PaddingX * 20);
        var table = TableShell(rtl, widths);
        var header = new TableRow(new TableRowProperties(new TableHeader()));
        for (var i = 0; i < columns.Count; i++)
        {
            header.Append(Cell(headers[i], rtl, bold: true, shade: "E8EFF8", center: false, right: columns[i].IsNumeric && !rtl, width: widths[i], fontHalfPoints: headerFont, marginTwips: margin));
        }

        table.Append(header);
        var index = 0;
        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            for (var i = 0; i < columns.Count; i++)
            {
                tableRow.Append(Cell(row[i], rtl, bold: false, shade: index % 2 == 1 ? "FBFCFE" : null, center: false, right: columns[i].IsNumeric && !rtl, noWrap: columns[i].IsNumeric, width: widths[i], fontHalfPoints: font, marginTwips: margin));
            }

            index++;
            table.Append(tableRow);
        }

        return table;
    }

    private static Table TableShell(bool rtl, int columns) => TableShell(rtl, Enumerable.Repeat(0, Math.Max(1, columns)).ToList());

    /// <summary>A bordered table; with column widths (twips) the layout is fixed to them, otherwise it fits the page width.</summary>
    private static Table TableShell(bool rtl, IReadOnlyList<int> widths)
    {
        var fixedWidths = widths.All(w => w > 0);
        // Table properties in schema order: bidiVisual, tblW, tblBorders, tblLayout, tblLook.
        var properties = new TableProperties();
        if (rtl)
        {
            properties.Append(new BiDiVisual());
        }

        properties.Append(fixedWidths ? new TableWidth { Width = widths.Sum().ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa } : new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct });
        properties.Append(new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
            new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
            new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
            new RightBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E2E8F0" }));
        if (fixedWidths)
        {
            properties.Append(new TableLayout { Type = TableLayoutValues.Fixed });
        }

        properties.Append(new TableLook { Val = "04A0" });
        var grid = new TableGrid();
        foreach (var width in widths)
        {
            grid.Append(fixedWidths ? new GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) } : new GridColumn());
        }

        return new Table(properties, grid);
    }

    private static TableCell Cell(string text, bool rtl, bool bold, string? shade, bool center, bool right = false, bool noWrap = false, int? width = null, int fontHalfPoints = 18, int marginTwips = 100)
    {
        // Cell properties in schema order (width, shading, no-wrap, margins). A figure never wraps.
        var properties = new TableCellProperties();
        if (width is { } w)
        {
            properties.Append(new TableCellWidth { Width = w.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa });
        }

        if (shade is not null)
        {
            properties.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = shade });
        }

        if (noWrap)
        {
            properties.Append(new NoWrap());
        }

        var side = marginTwips.ToString(CultureInfo.InvariantCulture);
        properties.Append(new TableCellMargin(new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa }, new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa }, new LeftMargin { Width = side, Type = TableWidthUnitValues.Dxa }, new RightMargin { Width = side, Type = TableWidthUnitValues.Dxa }));

        var paragraphProperties = new ParagraphProperties(new SpacingBetweenLines { After = "0" });
        if (rtl)
        {
            paragraphProperties.Append(new BiDi());
        }

        if (center)
        {
            paragraphProperties.Append(new Justification { Val = JustificationValues.Center });
        }
        else if (right)
        {
            paragraphProperties.Append(new Justification { Val = JustificationValues.Right });
        }

        // Run properties in schema order: bold, size, right-to-left.
        var runProperties = new RunProperties();
        if (bold)
        {
            runProperties.Append(new Bold());
        }

        runProperties.Append(new FontSize { Val = fontHalfPoints.ToString(CultureInfo.InvariantCulture) });
        if (rtl)
        {
            runProperties.Append(new RightToLeftText());
        }

        var run = new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return new TableCell(properties, new Paragraph(paragraphProperties, run));
    }

    private static Paragraph Image(MainDocumentPart main, ChartImage chart, bool rtl)
    {
        var part = main.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(chart.Png))
        {
            part.FeedData(stream);
        }

        const long cx = 6_000_000L;
        var cy = (long)(cx * (double)chart.Height / chart.Width);
        var drawing = InlineImage(main.GetIdOfPart(part), "chart.png", cx, cy);
        var properties = new ParagraphProperties(new Justification { Val = JustificationValues.Center });
        if (rtl)
        {
            properties.Append(new BiDi());
        }

        return new Paragraph(properties, new Run(drawing));
    }

    /// <summary>An inline picture (DrawingML) referencing an image part by relationship id.</summary>
    private static Drawing InlineImage(string relationshipId, string name, long cx, long cy)
    {
        var shapeProperties = new PIC.ShapeProperties(
            new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle });
        var picture = new PIC.Picture(
            new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = (UInt32Value)0U, Name = name }, new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
            shapeProperties);
        var graphic = new A.Graphic(new A.GraphicData(picture) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" });
        var inline = new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DW.DocProperties { Id = (UInt32Value)(uint)Random.Shared.Next(1, int.MaxValue), Name = name },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            graphic)
        {
            DistanceFromTop = (UInt32Value)0U,
            DistanceFromBottom = (UInt32Value)0U,
            DistanceFromLeft = (UInt32Value)0U,
            DistanceFromRight = (UInt32Value)0U,
        };
        return new Drawing(inline);
    }

    private static void AddHeaderFooter(MainDocumentPart main, ExportRequest request, string title, bool rtl, string font)
    {
        var headerPart = main.AddNewPart<HeaderPart>();
        var header = new Header();
        var headerParagraph = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Muted" }, new Justification { Val = rtl ? JustificationValues.Right : JustificationValues.Left }));
        if (request.Branding.Logo is { Length: > 0 } && !string.Equals(request.Branding.LogoContentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            var part = headerPart.AddImagePart(string.Equals(request.Branding.LogoContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ? ImagePartType.Jpeg : ImagePartType.Png);
            using var stream = new MemoryStream(request.Branding.Logo);
            part.FeedData(stream);
            headerParagraph.Append(new Run(InlineImage(headerPart.GetIdOfPart(part), "logo", 1_500_000L, 350_000L)));
            headerParagraph.Append(new Run(new TabChar()));
        }
        else
        {
            headerParagraph.Append(new Run(new RunProperties(new Bold()), new Text(request.OrganizationName) { Space = SpaceProcessingModeValues.Preserve }), new Run(new TabChar()));
        }

        headerParagraph.Append(new Run(new Text(title.Length > 0 ? title : "Report") { Space = SpaceProcessingModeValues.Preserve }));
        header.Append(headerParagraph);
        headerPart.Header = header;

        var footerPart = main.AddNewPart<FooterPart>();
        var footer = new Footer(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Muted" }, new Justification { Val = JustificationValues.Center }),
            new Run(new Text($"{(rtl ? "أُنشئ بواسطة Wabi Insight" : "Generated by Wabi Insight")} · {request.GeneratedAt:yyyy-MM-dd HH:mm} UTC · {(rtl ? "صفحة" : "Page")} ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }), new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }), new Run(new FieldChar { FieldCharType = FieldCharValues.End }),
            new Run(new Text(" / ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }), new Run(new FieldCode(" NUMPAGES ") { Space = SpaceProcessingModeValues.Preserve }), new Run(new FieldChar { FieldCharType = FieldCharValues.End })));
        footerPart.Footer = footer;
        _ = font;
    }

    private static SectionProperties SectionProperties(MainDocumentPart main, bool rtl)
    {
        var header = main.HeaderParts.First();
        var footer = main.FooterParts.First();
        var properties = new SectionProperties(
            new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(header) },
            new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) },
            new PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = 1134, Right = 1134, Bottom = 1134, Left = 1134, Header = 567, Footer = 567, Gutter = 0 });
        if (rtl)
        {
            properties.Append(new BiDi());
        }

        return properties;
    }

    private static void AddStyles(MainDocumentPart main, string font)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        var fonts = new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = ArabicFont, EastAsia = font };
        Style Make(string id, string name, int size, bool bold, string? color, int spacingBefore = 0, int spacingAfter = 120, int outline = -1)
        {
            var paragraph = new StyleParagraphProperties(new SpacingBetweenLines { Before = spacingBefore.ToString(CultureInfo.InvariantCulture), After = spacingAfter.ToString(CultureInfo.InvariantCulture), Line = "276", LineRule = LineSpacingRuleValues.Auto });
            if (outline >= 0)
            {
                paragraph.Append(new OutlineLevel { Val = outline });
            }

            var run = new StyleRunProperties((RunFonts)fonts.CloneNode(true), new FontSize { Val = (size * 2).ToString(CultureInfo.InvariantCulture) }, new FontSizeComplexScript { Val = (size * 2).ToString(CultureInfo.InvariantCulture) });
            if (bold)
            {
                run.Append(new Bold(), new BoldComplexScript());
            }

            if (color is not null)
            {
                run.Append(new Color { Val = color });
            }

            return new Style(new StyleName { Val = name }, paragraph, run) { Type = StyleValues.Paragraph, StyleId = id, CustomStyle = true };
        }

        part.Styles = new Styles(
            new DocDefaults(new RunPropertiesDefault(new RunPropertiesBaseStyle((RunFonts)fonts.CloneNode(true), new FontSize { Val = "21" }, new FontSizeComplexScript { Val = "21" }, new Color { Val = "1E293B" }))),
            Make("Normal", "Normal", 10, false, null),
            Make("Title", "Title", 22, true, "0B1F3A", 0, 60),
            Make("Subtitle", "Subtitle", 12, false, "64748B", 0, 60),
            Make("Muted", "Muted", 9, false, "64748B", 0, 120),
            Make("Caption", "Caption", 10, true, "0B1F3A", 160, 60),
            Make("Heading1", "heading 1", 15, true, "0B1F3A", 240, 80, 0),
            Make("Heading2", "heading 2", 12, true, "0B1F3A", 160, 40, 1));
    }
}
