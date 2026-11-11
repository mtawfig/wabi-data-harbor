using System.Text.Json.Serialization;

namespace WabiInsight.Core.Ai;

/// <summary>
/// The written part of a report, produced by the model from the verified panels (specification §20: executive
/// reports and professional email drafts). Every number it quotes comes from the RESULT block; the PDF renderer
/// draws the charts and tables itself from the panel data, never from model text.
/// </summary>
public sealed class ReportContent
{
    /// <summary>
    /// True when the written study was cut off or unreadable (E7): the document then carries the verified tables and
    /// charts with a short note instead of the model's broken text. Set by the server, never read from the model.
    /// </summary>
    [JsonIgnore]
    public bool Incomplete { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    [JsonPropertyName("introduction")]
    public string Introduction { get; set; } = string.Empty;

    [JsonPropertyName("sections")]
    public List<ReportSection> Sections { get; set; } = [];

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("keyFindings")]
    public List<string> KeyFindings { get; set; } = [];

    [JsonPropertyName("recommendations")]
    public List<ReportRecommendation> Recommendations { get; set; } = [];

    [JsonPropertyName("email")]
    public ReportEmail? Email { get; set; }
}

public sealed class ReportSection
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Paragraphs separated by blank lines.</summary>
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    /// <summary>Zero-based indexes of the panels to embed (chart or table) after the text.</summary>
    [JsonPropertyName("panels")]
    public List<int> Panels { get; set; } = [];
}

public sealed class ReportRecommendation
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;
}

public sealed class ReportEmail
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
}

public static class ReportSchema
{
    public const string Name = "wabi_report";

    public const int MaxSections = 12;
    public const int MaxRecommendations = 10;

    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["title", "subtitle", "introduction", "sections", "summary", "keyFindings", "recommendations", "email"],
          "properties": {
            "title": { "type": "string", "description": "Report title, in the requested language." },
            "subtitle": { "type": "string", "description": "One line: scope and period." },
            "introduction": { "type": "string", "description": "Two to four paragraphs: purpose, scope, data used, how to read the report. Paragraphs separated by a blank line." },
            "sections": {
              "type": "array",
              "description": "Standard analytical sections in reading order. Each has a title, a body of one to four paragraphs built on the verified numbers, and the panels (zero-based indexes) to show after the text.",
              "items": { "type": "object", "additionalProperties": false, "required": ["title", "body", "panels"], "properties": { "title": { "type": "string" }, "body": { "type": "string" }, "panels": { "type": "array", "items": { "type": "integer" } } } }
            },
            "summary": { "type": "string", "description": "Executive summary: the answer in one to three paragraphs." },
            "keyFindings": { "type": "array", "items": { "type": "string" }, "description": "Four to eight one-line facts taken directly from the data." },
            "recommendations": {
              "type": "array",
              "description": "Practical recommendations: operational actions and, where useful, systems or tools that would address the issues found. Each with a short title and a detail paragraph.",
              "items": { "type": "object", "additionalProperties": false, "required": ["title", "detail"], "properties": { "title": { "type": "string" }, "detail": { "type": "string" } } }
            },
            "email": { "anyOf": [ { "type": "object", "additionalProperties": false, "required": ["subject", "body"], "properties": { "subject": { "type": "string" }, "body": { "type": "string", "description": "A professional email that introduces the attached report and its main findings. Plain text with blank lines between paragraphs; greeting and sign-off included; no placeholders in square brackets." } } }, { "type": "null" } ] }
          }
        }
        """;
}

/// <summary>Branding stamped on every page of a report: the organization's logo when one is uploaded.</summary>
public sealed record ReportBranding(byte[]? Logo, string? LogoContentType);

public sealed record ReportRenderRequest(ReportContent Content, IReadOnlyList<AnalysisPanel> Panels, ReportBranding Branding, string Language, string OrganizationName, string ApplicationName, string PreparedFor, DateTimeOffset GeneratedAt);

/// <summary>Renders a report to PDF with the charts and tables drawn from the verified panel data.</summary>
public interface IReportRenderer
{
    byte[] RenderPdf(ReportRenderRequest request);
}

/// <summary>A rendered report available for download through the analysis it belongs to.</summary>
public sealed record GeneratedReport(string Title, string Subtitle, string FileName, long SizeBytes, string Introduction, IReadOnlyList<ReportSection> Sections, string Summary, IReadOnlyList<string> KeyFindings, IReadOnlyList<ReportRecommendation> Recommendations);

public sealed record EmailDraft(string Subject, string Body, string? AttachmentFileName);

public sealed record ReportDownload(Stream Content, string FileName, string ContentType);

/// <summary>Everything an exporter needs to turn a stored answer into a file: the answer, the written report when the answer had one, the context and the branding.</summary>
public sealed record ExportRequest(AskResponse Answer, ReportContent Content, string Language, string OrganizationName, string ApplicationName, string Prompt, ReportBranding Branding, DateTimeOffset GeneratedAt);

/// <summary>Renders a stored answer to DOCX or XLSX (PDF goes through <see cref="IReportRenderer"/>). Charts are drawn from the panel series, tables from the panel rows.</summary>
public interface IAnswerExporter
{
    Entities.Analysis.ExportFormat Format { get; }

    string ContentType { get; }

    string Extension { get; }

    byte[] Export(ExportRequest request);
}
