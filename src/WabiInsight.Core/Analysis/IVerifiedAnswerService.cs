using WabiInsight.Core.Ai;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Permissions;

namespace WabiInsight.Core.Analysis;

/// <summary>What the asker said about one answer, as the answer page shows it.</summary>
public sealed record AnswerFeedbackState(Guid HistoryId, int VersionNumber, string? Verdict, string? Reason, DateTimeOffset? At);

public sealed record SubmitAnswerFeedbackCommand(string Verdict, string? Reason);

public sealed record VerifiedAnswerListQuery(string? Status, Guid? ApplicationId, int Page = 1, int PageSize = 25);

public sealed record VerifiedAnswerListItem(
    Guid VerifiedAnswerId,
    Guid ApplicationId,
    string ApplicationName,
    string ApplicationNameAr,
    string Question,
    string Language,
    string AskedByName,
    string Status,
    string LatestVerdict,
    string? LatestReason,
    DateTimeOffset LatestFeedbackAt,
    DateTimeOffset? ReviewedAt,
    int UseCount);

/// <summary>Counts per status, for the filter chips and the menu badge (answers waiting for a review).</summary>
public sealed record VerifiedAnswerCounts(int Pending, int Reported, int Approved, int Rejected, int Disabled, int SetAside)
{
    public int Waiting => Pending + Reported;
}

public sealed record AnswerFeedbackEntry(string Verdict, string? Reason, string UserName, DateTimeOffset CreatedAt);

/// <summary>
/// One verified answer for the reviewing administrator. <see cref="Result"/> is the stored answer, given only when the
/// administrator may read every table and column it uses with no row limit; otherwise <see cref="ResultHidden"/> is true and
/// the plan alone explains how it was computed.
/// </summary>
public sealed record VerifiedAnswerDetail(
    Guid VerifiedAnswerId,
    Guid ApplicationId,
    string ApplicationName,
    string ApplicationNameAr,
    Guid HistoryId,
    int VersionNumber,
    string Question,
    string Language,
    string AskedByName,
    string Status,
    string LatestVerdict,
    string? LatestReason,
    DateTimeOffset LatestFeedbackAt,
    string? ReviewedByName,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote,
    string? SetAsideReason,
    int UseCount,
    DateTimeOffset? LastUsedAt,
    AnalysisPlan? Plan,
    AskResponse? Result,
    bool ResultHidden,
    IReadOnlyList<AnswerFeedbackEntry> Feedback,
    IReadOnlyList<IReadOnlyDictionary<string, DisplayLabel>?>? PanelLabels = null);

public static class VerifiedAnswerActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Disable = "disable";
    public const string Enable = "enable";

    public static readonly IReadOnlyList<string> All = [Approve, Reject, Disable, Enable];
}

public sealed record ReviewVerifiedAnswerCommand(string Action, string? Note);

/// <summary>
/// Verified answers (P13.53). The asker marks an answer correct or not correct; Organization Administrators approve,
/// reject, disable or delete; approved answers become examples for similar questions. Everything stays inside the
/// asker's organization, and Platform Administrators have no access.
/// </summary>
public interface IVerifiedAnswerService
{
    /// <summary>The asker's own verdict on a saved answer version (owner only).</summary>
    Task<AnswerFeedbackState> GetFeedbackAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default);

    /// <summary>Records the asker's verdict (owner only). Earlier verdicts are kept; the latest one counts.</summary>
    Task<AnswerFeedbackState> SubmitFeedbackAsync(Guid historyId, int versionNumber, SubmitAnswerFeedbackCommand command, CancellationToken cancellationToken = default);

    Task<PagedResult<VerifiedAnswerListItem>> ListAsync(VerifiedAnswerListQuery query, CancellationToken cancellationToken = default);

    Task<VerifiedAnswerCounts> CountsAsync(CancellationToken cancellationToken = default);

    Task<VerifiedAnswerDetail?> GetAsync(Guid verifiedAnswerId, CancellationToken cancellationToken = default);

    Task<VerifiedAnswerDetail> ReviewAsync(Guid verifiedAnswerId, ReviewVerifiedAnswerCommand command, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid verifiedAnswerId, CancellationToken cancellationToken = default);
}

/// <summary>Finds the approved answers to give the planner for one question, under the asker's own permissions.</summary>
public interface IVerifiedExampleSource
{
    Task<IReadOnlyList<VerifiedExample>> FindAsync(Guid applicationId, string question, EffectivePermissions permissions, IReadOnlyDictionary<Guid, RowMappingColumns> mappings, DateOnly today, CancellationToken cancellationToken = default);
}
