using WabiInsight.Core.Ai;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Core.Analysis;

/// <summary>A colleague the owner may share with: an active user of the same organization, never the owner.</summary>
public sealed record ShareRecipientDto(Guid UserId, string FullName, string Email, string? DepartmentNameEn, string? DepartmentNameAr);

public enum ShareStatus
{
    Active = 1,
    Expired = 2,
    Revoked = 3,
}

/// <summary>One share row as the owner sees it on an analysis or in Shared By Me.</summary>
public sealed record ShareDto(
    Guid ShareId,
    Guid HistoryId,
    string HistoryTitle,
    AnalysisType AnalysisType,
    DataClassification DataClassification,
    Guid RecipientUserId,
    string RecipientName,
    string RecipientEmail,
    ShareAccessLevel Permission,
    string? Message,
    ShareStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastViewedAt);

/// <summary>An analysis someone shared with the caller (Shared With Me).</summary>
public sealed record SharedAnalysisDto(
    Guid ShareId,
    Guid HistoryId,
    string Title,
    string PromptExcerpt,
    Guid ApplicationId,
    string ApplicationName,
    string ApplicationNameAr,
    AnalysisType AnalysisType,
    DataClassification DataClassification,
    string OutputLanguage,
    int CurrentVersionNumber,
    bool HasReport,
    Guid OwnerUserId,
    string OwnerName,
    string? Message,
    DateTimeOffset SharedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>Read-only detail for a recipient: the analysis, its completed versions and the share terms. No re-run, export, rename or delete.</summary>
/// <param name="CanViewAllInExcel">
/// True when the recipient may read the data of this analysis themselves: "View all in Excel" is then offered, and the
/// export runs under the recipient's own permissions and row scope (P13.50). Otherwise they have the rows shared with them.
/// </param>
public sealed record SharedAnalysisDetailDto(SharedAnalysisDto Share, string OriginalPrompt, string PromptLanguage, IReadOnlyList<HistoryVersionDto> Versions, bool CanViewAllInExcel = false);

public sealed record CreateShareCommand(IReadOnlyList<Guid> RecipientUserIds, string? Message, DateTimeOffset? ExpiresAt);

/// <summary>
/// Email delivery of a version (specification §24). Recipients are free addresses; each is classified as internal
/// (an active user of the organization) or external. <paramref name="DisclosureApproved"/> is the owner's explicit
/// per-send approval that Confidential data requires (review B11).
/// </summary>
public sealed record SendShareEmailCommand(int VersionNumber, IReadOnlyList<string> Recipients, string Subject, string Body, bool AttachReport, bool DisclosureApproved);

public sealed record ShareEmailResult(int Recipients, int InternalRecipients, int ExternalRecipients, bool AttachedReport, string? AttachmentFileName, int InAppSharesCreated);

/// <summary>
/// In-app and email sharing (specification §24, review B11). Every operation is tenant-filtered; owners act on their
/// own analyses only, recipients read through an unexpired, unrevoked share only. Cross-organization sharing is impossible.
/// </summary>
public interface ISharingService
{
    Task<IReadOnlyList<ShareRecipientDto>> SearchRecipientsAsync(string? search, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShareDto>> ListSharesOfAsync(Guid historyId, CancellationToken cancellationToken = default);

    /// <summary>Creates (or refreshes) a VIEW share per recipient, notifies them in-app and by email, and audits the action.</summary>
    Task<IReadOnlyList<ShareDto>> ShareAsync(Guid historyId, CreateShareCommand command, CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid historyId, Guid shareId, CancellationToken cancellationToken = default);

    Task<PagedResult<ShareDto>> ListSharedByMeAsync(int page, int pageSize, string? search, bool includeInactive, CancellationToken cancellationToken = default);

    Task<PagedResult<SharedAnalysisDto>> ListSharedWithMeAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);

    Task<SharedAnalysisDetailDto?> GetSharedAsync(Guid historyId, CancellationToken cancellationToken = default);

    Task<AskResponse?> GetSharedVersionResultAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default);

    Task<ArtifactDownload?> OpenSharedVersionReportAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// "View all in Excel" for a recipient (P13.50): the Excel workbook of a shared version with every row of its lists,
    /// read under the recipient's own permissions and row scope. Null when the share is not theirs; refused with
    /// <c>export_not_permitted</c> when they may not read that data themselves.
    /// </summary>
    Task<ArtifactDownload?> ExportSharedWholeListAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a version by email under the classification rules; records an EmailDraftEvent (Sent or Blocked) and an audit entry.</summary>
    Task<ShareEmailResult> SendEmailAsync(Guid historyId, SendShareEmailCommand command, CancellationToken cancellationToken = default);
}
