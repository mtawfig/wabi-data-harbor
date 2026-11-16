using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Analysis;

/// <summary>
/// An answer someone in the organization marked as correct or not correct (P13.53), waiting for an Organization
/// Administrator's review or already reviewed. Only <see cref="VerifiedAnswerStatuses.Approved"/> answers are used: their
/// question and plan are given to the model as worked examples when a similar question is asked about the same
/// application. One row per saved answer version; the feedback itself is kept in the append-only <see cref="AnswerFeedback"/>.
/// </summary>
public class VerifiedAnswer : AuditableEntity, ITenantOwned
{
    public Guid VerifiedAnswerId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ApplicationId { get; set; }

    public Guid HistoryId { get; set; }

    public int VersionNumber { get; set; }

    /// <summary>The person who asked (the owner of the saved answer).</summary>
    public Guid AskedByUserId { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    /// <summary>The analysis plan of the version, as it was validated and run.</summary>
    public string PlanJson { get; set; } = "{}";

    /// <summary>The question's normalized words, space separated, used to find similar questions.</summary>
    public string Terms { get; set; } = string.Empty;

    /// <summary>One of <see cref="VerifiedAnswerStatuses"/>.</summary>
    public string Status { get; set; } = VerifiedAnswerStatuses.Pending;

    /// <summary>The asker's latest verdict: one of <see cref="AnswerVerdicts"/>.</summary>
    public string LatestVerdict { get; set; } = AnswerVerdicts.Correct;

    public string? LatestReason { get; set; }

    public DateTimeOffset LatestFeedbackAt { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewNote { get; set; }

    /// <summary>Why an approved answer was set aside: the tables or columns it uses changed or were removed.</summary>
    public string? SetAsideReason { get; set; }

    /// <summary>How many times it was given to the model as an example.</summary>
    public int UseCount { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>What the asker said about one answer, with who and when. Append-only: a change of mind adds a row.</summary>
public class AnswerFeedback : ITenantOwned
{
    public Guid FeedbackId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid HistoryId { get; set; }

    public int VersionNumber { get; set; }

    public Guid UserId { get; set; }

    /// <summary>One of <see cref="AnswerVerdicts"/>.</summary>
    public string Verdict { get; set; } = AnswerVerdicts.Correct;

    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class AnswerVerdicts
{
    public const string Correct = "Correct";
    public const string NotCorrect = "NotCorrect";

    public static readonly IReadOnlyList<string> All = [Correct, NotCorrect];
}

/// <summary>Stable status codes of a verified answer; the labels are localized in the web application.</summary>
public static class VerifiedAnswerStatuses
{
    /// <summary>Marked correct by the asker, not reviewed yet.</summary>
    public const string Pending = "Pending";

    /// <summary>Reported not correct by the asker, not reviewed yet.</summary>
    public const string Reported = "Reported";

    /// <summary>Approved by an Organization Administrator: used as an example.</summary>
    public const string Approved = "Approved";

    public const string Rejected = "Rejected";

    /// <summary>Approved earlier and switched off by an administrator.</summary>
    public const string Disabled = "Disabled";

    /// <summary>Approved earlier; a table or column it uses changed or was removed, so it is no longer used.</summary>
    public const string SetAside = "SetAside";

    public static readonly IReadOnlyList<string> All = [Pending, Reported, Approved, Rejected, Disabled, SetAside];
}
