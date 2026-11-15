using System.ComponentModel.DataAnnotations;
using WabiInsight.Core.Organizations;

namespace WabiInsight.Core.Demo;

/// <summary>Settings of the public demo-request form (P13.52).</summary>
public sealed class DemoRequestOptions
{
    public const string SectionName = "DemoRequests";

    /// <summary>The mailbox told about every new request. Empty sends no e-mail to the team (the in-app notification stays).</summary>
    public string NotifyEmail { get; set; } = "support@wabitex.com";

    /// <summary>The same address sending the form again within this time adds to its open request instead of opening a new one.</summary>
    [Range(1, 720)]
    public int RepeatWindowHours { get; set; } = 24;

    /// <summary>Requests stored from one client address in 24 hours; more are dropped without a word.</summary>
    [Range(1, 1000)]
    public int MaxPerAddressPerDay { get; set; } = 10;

    /// <summary>Personal details of a request that did not become a customer are erased after this many months.</summary>
    [Range(1, 120)]
    public int RetentionMonths { get; set; } = 24;
}

/// <summary>What the public form sends. <see cref="Website"/> is the trap field: people never see it, robots fill it.</summary>
public sealed record SubmitDemoRequestCommand(
    string FullName,
    string Email,
    string OrganizationName,
    string CountryCode,
    bool Consent,
    string? JobTitle,
    string? Phone,
    string? Industry,
    string? OrganizationSize,
    string? DatabaseSystems,
    string? DemoLanguage,
    string? PreferredTime,
    string? Message,
    string? PageLanguage,
    string? Website);

public enum DemoRequestSubmitOutcome
{
    /// <summary>Stored as a new request; the e-mails and notifications went out.</summary>
    Created,

    /// <summary>The same address already has an open, recent request: counted on it.</summary>
    Repeated,

    /// <summary>Dropped without a word (trap field filled, or too many from one address).</summary>
    Dropped,

    /// <summary>A required field is missing or a value is not acceptable; <see cref="DemoRequestSubmitResult.Errors"/> says which.</summary>
    Invalid,
}

public sealed record DemoRequestSubmitResult(DemoRequestSubmitOutcome Outcome, IReadOnlyDictionary<string, string> Errors, Guid? DemoRequestId = null)
{
    public static DemoRequestSubmitResult Of(DemoRequestSubmitOutcome outcome, Guid? id = null) => new(outcome, new Dictionary<string, string>(), id);
}

public sealed record DemoRequestListQuery(string? Status, string? CountryCode, DateTimeOffset? From, DateTimeOffset? To, string? Search, int Page = 1, int PageSize = 25);

public sealed record DemoRequestListItem(
    Guid DemoRequestId,
    string FullName,
    string Email,
    string OrganizationName,
    string CountryCode,
    string? JobTitle,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? PerformedAt,
    string? AssignedAdministratorName,
    int RepeatCount,
    bool Erased);

public sealed record DemoRequestEventDto(Guid EventId, string EventType, string? PreviousStatus, string? NewStatus, string? Note, string? ActorDisplayName, DateTimeOffset CreatedAt);

public sealed record DemoRequestDetail(
    Guid DemoRequestId,
    string FullName,
    string Email,
    string OrganizationName,
    string CountryCode,
    string? JobTitle,
    string? Phone,
    string? Industry,
    string? OrganizationSize,
    string? DatabaseSystems,
    string DemoLanguage,
    string? PreferredTime,
    string? Message,
    string PageLanguage,
    DateTimeOffset ConsentAt,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    int RepeatCount,
    DateTimeOffset? LastRepeatedAt,
    string Status,
    string? Remark,
    Guid? AssignedAdministratorId,
    string? AssignedAdministratorName,
    DateTimeOffset? ScheduledAt,
    string? MeetingLocation,
    DateTimeOffset? PerformedAt,
    string? PresentedBy,
    string? AttendeeName,
    string? AttendeePosition,
    string? AttendeeEmail,
    string? AttendeePhone,
    string? AttendeeMobile,
    string? Impression,
    string? PerformedRemark,
    string? NextStep,
    DateOnly? FollowUpDate,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ErasedAt,
    IReadOnlyList<DemoRequestEventDto> Events);

/// <summary>The follow-up as the administrator saves it: every field of the two lower blocks, plus an optional note for the history.</summary>
public sealed record UpdateDemoRequestCommand(
    string Status,
    string? Remark,
    Guid? AssignedAdministratorId,
    DateTimeOffset? ScheduledAt,
    string? MeetingLocation,
    DateTimeOffset? PerformedAt,
    string? PresentedBy,
    string? AttendeeName,
    string? AttendeePosition,
    string? AttendeeEmail,
    string? AttendeePhone,
    string? AttendeeMobile,
    string? Impression,
    string? PerformedRemark,
    string? NextStep,
    DateOnly? FollowUpDate,
    string? Note);

public sealed record DemoRequestSummary(int New, int Open, int Total);

public sealed record DemoAssigneeDto(Guid AdministratorId, string FullName);

/// <summary>
/// Demo requests from the public landing page. Submitting needs no account; everything else is for Platform
/// Administrators only, and no organization user ever reads a request.
/// </summary>
public interface IDemoRequestService
{
    Task<DemoRequestSubmitResult> SubmitAsync(SubmitDemoRequestCommand command, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default);

    Task<PagedResult<DemoRequestListItem>> ListAsync(DemoRequestListQuery query, CancellationToken cancellationToken = default);

    Task<DemoRequestSummary> SummaryAsync(CancellationToken cancellationToken = default);

    Task<DemoRequestDetail?> GetAsync(Guid demoRequestId, CancellationToken cancellationToken = default);

    Task<DemoRequestDetail> UpdateAsync(Guid demoRequestId, UpdateDemoRequestCommand command, CancellationToken cancellationToken = default);

    /// <summary>Active Platform Administrators a request can be assigned to.</summary>
    Task<IReadOnlyList<DemoAssigneeDto>> AssigneesAsync(CancellationToken cancellationToken = default);
}

/// <summary>ISO 3166-1 alpha-2 codes the form accepts; the names are shown by the browser in the visitor's language.</summary>
public static class CountryCodes
{
    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal)
    {
        "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ", "BA", "BB", "BD", "BE",
        "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS", "BT", "BV", "BW", "BY", "BZ", "CA", "CC", "CD",
        "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM",
        "DO", "DZ", "EC", "EE", "EG", "EH", "ER", "ES", "ET", "FI", "FJ", "FK", "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF",
        "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY", "HK", "HM", "HN", "HR", "HT", "HU",
        "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT", "JE", "JM", "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN",
        "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC", "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME",
        "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ", "NA",
        "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ", "OM", "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM",
        "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS", "RU", "RW", "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI",
        "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS", "ST", "SV", "SX", "SY", "SZ", "TC", "TD", "TF", "TG", "TH", "TJ", "TK",
        "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW", "TZ", "UA", "UG", "UM", "US", "UY", "UZ", "VA", "VC", "VE", "VG", "VI",
        "VN", "VU", "WF", "WS", "YE", "YT", "ZA", "ZM", "ZW",
    };

    public static bool IsKnown(string? code) => code is not null && Codes.Contains(code);
}
