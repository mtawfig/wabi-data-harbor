using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Analysis;

public enum AnalysisType
{
    Answer = 1,
    Dashboard = 2,
    Report = 3,
}

public enum AnalysisHistoryStatus
{
    Completed = 1,
    Failed = 2,
}

/// <summary>
/// A saved analysis (specification §23): the prompt, its application and languages, and the current version.
/// Owned by the user who asked; tenant-owned; soft-deleted so audit references stay valid.
/// </summary>
public class AnalysisHistory : AuditableEntity, ITenantOwned
{
    public Guid HistoryId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid ApplicationId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string OriginalPrompt { get; set; } = string.Empty;

    public string PromptLanguage { get; set; } = "en";

    public string OutputLanguage { get; set; } = "en";

    public int CurrentVersionNumber { get; set; }

    public AnalysisType AnalysisType { get; set; } = AnalysisType.Answer;

    public AnalysisHistoryStatus Status { get; set; } = AnalysisHistoryStatus.Completed;

    /// <summary>The highest classification among the columns the stored result was computed from.</summary>
    public DataClassification DataClassification { get; set; } = DataClassification.Internal;

    public bool IsFavorite { get; set; }

    public bool IsArchived { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<AnalysisHistoryVersion> Versions { get; set; } = [];
}

/// <summary>One run of a saved analysis: the plan, the redacted SQL, a result summary and the stored artifacts. Never overwritten; a re-run adds a version.</summary>
public class AnalysisHistoryVersion : ITenantOwned
{
    public Guid VersionId { get; set; }

    public Guid HistoryId { get; set; }

    public Guid OrganizationId { get; set; }

    public int VersionNumber { get; set; }

    /// <summary>The analysis plan the model produced (objective, queries, assumptions, deliverables), as JSON.</summary>
    public string QueryPlanJson { get; set; } = "{}";

    /// <summary>Every panel's parameter-redacted SQL, one per line block.</summary>
    public string CompiledSqlPreview { get; set; } = string.Empty;

    /// <summary>Narrative and highlights as JSON for lists and previews; the full result lives in the result artifact.</summary>
    public string ResultSummary { get; set; } = "{}";

    public Guid? ResultArtifactId { get; set; }

    public Guid? ReportArtifactId { get; set; }

    public Guid? MetricId { get; set; }

    public Guid? ModelProviderId { get; set; }

    public string? ModelName { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public decimal EstimatedCost { get; set; }

    public double DurationMs { get; set; }

    public int PanelCount { get; set; }

    public int RowCount { get; set; }

    public AnalysisHistoryStatus Status { get; set; } = AnalysisHistoryStatus.Completed;

    public string? FailureStage { get; set; }

    public string? FailureCode { get; set; }

    public Guid RunByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public AnalysisHistory? History { get; set; }
}

public enum AnalysisArtifactKind
{
    ResultJson = 1,
    ReportPdf = 2,
    ExportPdf = 3,
    ExportDocx = 4,
    ExportXlsx = 5,
}

public enum ExportFormat
{
    Pdf = 1,
    Docx = 2,
    Xlsx = 3,
}

public enum ReportGenerationStatus
{
    Completed = 1,
    Failed = 2,
}

/// <summary>Append-only record of every report or export produced (specification §20): who, which version, which format, how big, how long.</summary>
public class ReportGenerationEvent : ITenantOwned
{
    public Guid EventId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid UserId { get; set; }

    public Guid HistoryId { get; set; }

    public Guid VersionId { get; set; }

    public Guid? ArtifactId { get; set; }

    public ExportFormat Format { get; set; }

    public ReportGenerationStatus Status { get; set; }

    public string Language { get; set; } = "en";

    public long SizeBytes { get; set; }

    public double DurationMs { get; set; }

    public string? FailureCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A stored file of a version (result set as JSON, rendered report) kept through IArtifactStorage with its organization.</summary>
public class AnalysisArtifact : ITenantOwned
{
    public Guid ArtifactId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid HistoryId { get; set; }

    public Guid VersionId { get; set; }

    public AnalysisArtifactKind Kind { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A user's named group of saved analyses.</summary>
public class AnalysisCollectionRecord : AuditableEntity, ITenantOwned
{
    public Guid CollectionId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ICollection<AnalysisCollectionItem> Items { get; set; } = [];
}

public class AnalysisCollectionItem : ITenantOwned
{
    public Guid CollectionId { get; set; }

    public Guid HistoryId { get; set; }

    public Guid OrganizationId { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public AnalysisCollectionRecord? Collection { get; set; }

    public AnalysisHistory? History { get; set; }
}
