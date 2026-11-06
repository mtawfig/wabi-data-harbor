using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Core.Analysis;

public sealed record HistoryListQuery(
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    Guid? ApplicationId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool? Favorite = null,
    bool Archived = false,
    Guid? CollectionId = null,
    AnalysisType? AnalysisType = null);

public sealed record HistorySummaryDto(
    Guid HistoryId,
    string Title,
    string PromptExcerpt,
    Guid ApplicationId,
    string ApplicationCode,
    string ApplicationName,
    string ApplicationNameAr,
    AnalysisType AnalysisType,
    AnalysisHistoryStatus Status,
    DataClassification DataClassification,
    string OutputLanguage,
    int CurrentVersionNumber,
    bool IsFavorite,
    bool IsArchived,
    bool HasReport,
    decimal EstimatedCost,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record HistoryVersionDto(
    Guid VersionId,
    int VersionNumber,
    AnalysisHistoryStatus Status,
    string? FailureStage,
    string? FailureCode,
    string? ModelName,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCost,
    double DurationMs,
    int PanelCount,
    int RowCount,
    bool HasResult,
    bool HasReport,
    string? ReportFileName,
    DateTimeOffset CreatedAt);

public sealed record HistoryDetailDto(
    HistorySummaryDto Summary,
    string OriginalPrompt,
    string PromptLanguage,
    IReadOnlyList<HistoryVersionDto> Versions,
    IReadOnlyList<CollectionSummaryDto> Collections);

public sealed record CollectionSummaryDto(Guid CollectionId, string Name, string? Description, int ItemCount, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record SaveCollectionCommand(string Name, string? Description);

public sealed record ArtifactDownload(Stream Content, string FileName, string ContentType);

public sealed record ReportListQuery(int Page = 1, int PageSize = 25, string? Search = null, ExportFormat? Format = null, Guid? HistoryId = null);

/// <summary>One file in the reports library: a generated study PDF or an export of a version.</summary>
public sealed record ReportFileDto(Guid ArtifactId, Guid HistoryId, int VersionNumber, string HistoryTitle, AnalysisArtifactKind Kind, ExportFormat Format, string FileName, string ContentType, long SizeBytes, string Language, DateTimeOffset CreatedAt);

/// <summary>
/// Persistent analysis history (specification §23). Every read is tenant-filtered and owner-checked
/// (shares arrive in P10.1). Reopening a version returns the stored result; it never re-queries the source.
/// </summary>
public interface IAnalysisHistoryService
{
    Task<PagedResult<HistorySummaryDto>> ListAsync(HistoryListQuery query, CancellationToken cancellationToken = default);

    Task<HistoryDetailDto?> GetAsync(Guid historyId, CancellationToken cancellationToken = default);

    /// <summary>The stored answer of a version (what the Ask page showed), or null when it is not the caller's or has no stored result.</summary>
    Task<AskResponse?> GetVersionResultAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default);

    Task<ArtifactDownload?> OpenVersionReportAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default);

    Task<HistorySummaryDto> RenameAsync(Guid historyId, string title, CancellationToken cancellationToken = default);

    Task<HistorySummaryDto> SetFavoriteAsync(Guid historyId, bool isFavorite, CancellationToken cancellationToken = default);

    Task<HistorySummaryDto> SetArchivedAsync(Guid historyId, bool isArchived, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid historyId, CancellationToken cancellationToken = default);

    /// <summary>Runs the original prompt again under the caller's current permissions and stores the outcome as a new version.</summary>
    Task<AskResponse> RerunAsync(Guid historyId, CancellationToken cancellationToken = default);

    /// <summary>Renders a version of the caller's analysis to PDF, DOCX or XLSX, stores it as an artifact, records a generation event and returns it.</summary>
    Task<ArtifactDownload> ExportAsync(Guid historyId, int versionNumber, ExportFormat format, CancellationToken cancellationToken = default);

    /// <summary>The caller's generated files (study PDFs and exports), newest first.</summary>
    Task<PagedResult<ReportFileDto>> ListReportsAsync(ReportListQuery query, CancellationToken cancellationToken = default);

    Task<ArtifactDownload?> OpenArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectionSummaryDto>> ListCollectionsAsync(CancellationToken cancellationToken = default);

    Task<CollectionSummaryDto> CreateCollectionAsync(SaveCollectionCommand command, CancellationToken cancellationToken = default);

    Task<CollectionSummaryDto> UpdateCollectionAsync(Guid collectionId, SaveCollectionCommand command, CancellationToken cancellationToken = default);

    Task DeleteCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default);

    Task AddToCollectionAsync(Guid collectionId, Guid historyId, CancellationToken cancellationToken = default);

    Task RemoveFromCollectionAsync(Guid collectionId, Guid historyId, CancellationToken cancellationToken = default);
}
