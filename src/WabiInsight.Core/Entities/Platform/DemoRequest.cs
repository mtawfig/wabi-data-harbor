namespace WabiInsight.Core.Entities.Platform;

/// <summary>
/// A request for a product demo, sent from the public landing page by someone who has no account (P13.52). It belongs
/// to the platform, never to an organization: only Platform Administrators read it. The first block is what the
/// visitor sent; the second is the follow-up; the third is the demo as it was performed, with the key person who
/// attended (one person, kept in this row by the owner's decision).
/// </summary>
public class DemoRequest
{
    public Guid DemoRequestId { get; set; }

    // What the visitor sent.

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>Upper-cased address, to find an earlier request of the same person.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;

    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 code, upper case.</summary>
    public string CountryCode { get; set; } = string.Empty;

    public string? JobTitle { get; set; }

    public string? Phone { get; set; }

    /// <summary>One of <see cref="DemoIndustries"/>.</summary>
    public string? Industry { get; set; }

    /// <summary>One of <see cref="DemoOrganizationSizes"/>.</summary>
    public string? OrganizationSize { get; set; }

    public string? DatabaseSystems { get; set; }

    /// <summary>The language the visitor wants the demo in: "en" or "ar".</summary>
    public string DemoLanguage { get; set; } = "en";

    public string? PreferredTime { get; set; }

    public string? Message { get; set; }

    /// <summary>The language of the page the form was sent from; the confirmation e-mail uses it.</summary>
    public string PageLanguage { get; set; } = "en";

    /// <summary>When the visitor agreed to be contacted about the request. A request is never stored without it.</summary>
    public DateTimeOffset ConsentAt { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>How many times the same address sent the form again while this request was open and recent.</summary>
    public int RepeatCount { get; set; }

    public DateTimeOffset? LastRepeatedAt { get; set; }

    // Follow-up by Platform Administrators.

    /// <summary>One of <see cref="DemoRequestStatuses"/>.</summary>
    public string Status { get; set; } = DemoRequestStatuses.New;

    public string? Remark { get; set; }

    public Guid? AssignedAdministratorId { get; set; }

    public DateTimeOffset? ScheduledAt { get; set; }

    /// <summary>Meeting link or place.</summary>
    public string? MeetingLocation { get; set; }

    // The demo as performed.

    public DateTimeOffset? PerformedAt { get; set; }

    public string? PresentedBy { get; set; }

    public string? AttendeeName { get; set; }

    public string? AttendeePosition { get; set; }

    public string? AttendeeEmail { get; set; }

    public string? AttendeePhone { get; set; }

    public string? AttendeeMobile { get; set; }

    /// <summary>One of <see cref="DemoImpressions"/>.</summary>
    public string? Impression { get; set; }

    public string? PerformedRemark { get; set; }

    public string? NextStep { get; set; }

    public DateOnly? FollowUpDate { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedByAdministratorId { get; set; }

    /// <summary>When the personal details were erased at the end of the retention period; the row stays for the counts.</summary>
    public DateTimeOffset? ErasedAt { get; set; }
}

/// <summary>Append-only history of a demo request: the submission, repeats, status changes and remarks, with who and when.</summary>
public class DemoRequestEvent
{
    public Guid EventId { get; set; }

    public Guid DemoRequestId { get; set; }

    /// <summary>One of <see cref="DemoRequestEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;

    public string? PreviousStatus { get; set; }

    public string? NewStatus { get; set; }

    public string? Note { get; set; }

    /// <summary>Null for what the visitor did.</summary>
    public Guid? ActorAdministratorId { get; set; }

    public string? ActorDisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Stable status codes; the labels are localized in the web application.</summary>
public static class DemoRequestStatuses
{
    public const string New = "New";
    public const string Contacted = "Contacted";
    public const string Scheduled = "Scheduled";
    public const string Performed = "Performed";
    public const string Converted = "Converted";
    public const string NotInterested = "NotInterested";
    public const string Cancelled = "Cancelled";
    public const string Spam = "Spam";

    public static readonly IReadOnlyList<string> All = [New, Contacted, Scheduled, Performed, Converted, NotInterested, Cancelled, Spam];

    /// <summary>Statuses in which the request still waits for the demo: the same address sending again adds to it.</summary>
    public static readonly IReadOnlyList<string> Open = [New, Contacted, Scheduled];
}

public static class DemoImpressions
{
    public const string VeryPositive = "VeryPositive";
    public const string Positive = "Positive";
    public const string Neutral = "Neutral";
    public const string Negative = "Negative";

    public static readonly IReadOnlyList<string> All = [VeryPositive, Positive, Neutral, Negative];
}

public static class DemoIndustries
{
    public static readonly IReadOnlyList<string> All =
    [
        "Aviation", "Banking", "Government", "Healthcare", "Education", "Energy", "Retail", "Logistics", "Telecom",
        "Manufacturing", "RealEstate", "Hospitality", "Technology", "Other",
    ];
}

public static class DemoOrganizationSizes
{
    public static readonly IReadOnlyList<string> All = ["1-50", "51-200", "201-1000", "1001-5000", "5000+"];
}

public static class DemoRequestEventTypes
{
    public const string Submitted = "Submitted";
    public const string Repeated = "Repeated";
    public const string StatusChanged = "StatusChanged";
    public const string Updated = "Updated";
}
