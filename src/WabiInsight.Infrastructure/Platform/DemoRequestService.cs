using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Demo;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Platform;

/// <summary>
/// Demo requests from the public landing page (P13.52). The rules that keep a public form safe: a request is stored
/// only with the visitor's consent; the answer to the visitor is the same whether the request was stored, counted on
/// an earlier one or dropped, so nothing is learnt about anyone's address; one client address stores only so many a
/// day; the trap field drops robots without a word; every value is trimmed, bounded and free of control characters
/// before it reaches the database or an e-mail. Requests belong to the platform: only Platform Administrators read
/// them, and every change they make is in the request's history and in the audit log.
/// </summary>
public sealed class DemoRequestService : IDemoRequestService
{
    private const string EntityType = "DemoRequest";
    private const string Erased = "(erased)";

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly INotificationDispatcher _notifications;
    private readonly IEmailOutbox _outbox;
    private readonly IClock _clock;
    private readonly DemoRequestOptions _options;
    private readonly PlatformUrlOptions _urls;
    private readonly EmailOptions _email;

    public DemoRequestService(
        ConfigurationDbContext context,
        ITenantContext tenantContext,
        IAuditWriter audit,
        INotificationDispatcher notifications,
        IEmailOutbox outbox,
        IClock clock,
        IOptions<DemoRequestOptions> options,
        IOptions<PlatformUrlOptions> urls,
        IOptions<EmailOptions> email)
    {
        _context = context;
        _tenantContext = tenantContext;
        _audit = audit;
        _notifications = notifications;
        _outbox = outbox;
        _clock = clock;
        _options = options.Value;
        _urls = urls.Value;
        _email = email.Value;
    }

    public async Task<DemoRequestSubmitResult> SubmitAsync(SubmitDemoRequestCommand command, string? ipAddress, string? userAgent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = _clock.UtcNow;
        var ip = Bounded(ipAddress, 64);

        // Housekeeping on every submission, whoever sends it: details past their retention go.
        await EraseExpiredAsync(now, cancellationToken);

        // People never see the trap field. Whoever fills it is answered like anyone else and nothing is stored.
        if (!string.IsNullOrWhiteSpace(command.Website))
        {
            await DropAsync("trap_field", ip, cancellationToken);
            return DemoRequestSubmitResult.Of(DemoRequestSubmitOutcome.Dropped);
        }

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var fullName = Required(command.FullName, 150, "fullName", errors);
        var organizationName = Required(command.OrganizationName, 200, "organizationName", errors);
        var email = Required(command.Email, 254, "email", errors);
        if (email is not null && !EmailNormalizer.IsPlausible(email))
        {
            errors["email"] = "invalid";
        }

        var country = (command.CountryCode ?? string.Empty).Trim().ToUpperInvariant();
        if (country.Length == 0)
        {
            errors["countryCode"] = "required";
        }
        else if (!CountryCodes.IsKnown(country))
        {
            errors["countryCode"] = "invalid";
        }

        if (!command.Consent)
        {
            errors["consent"] = "required";
        }

        var jobTitle = Optional(command.JobTitle, 150, "jobTitle", errors);
        var phone = Optional(command.Phone, 40, "phone", errors);
        if (phone is not null && !phone.All(c => char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')'))
        {
            errors["phone"] = "invalid";
        }

        var industry = OneOf(command.Industry, DemoIndustries.All, "industry", errors);
        var size = OneOf(command.OrganizationSize, DemoOrganizationSizes.All, "organizationSize", errors);
        var databases = Optional(command.DatabaseSystems, 200, "databaseSystems", errors);
        var preferredTime = Optional(command.PreferredTime, 200, "preferredTime", errors);
        var message = Optional(command.Message, 2000, "message", errors, multiline: true);
        var demoLanguage = Language(command.DemoLanguage) ?? Language(command.PageLanguage) ?? "en";
        var pageLanguage = Language(command.PageLanguage) ?? "en";
        if (errors.Count > 0)
        {
            return new DemoRequestSubmitResult(DemoRequestSubmitOutcome.Invalid, errors);
        }

        // One client address stores only so many requests a day, whatever names and addresses it types.
        var dayAgo = now.AddHours(-24);
        if (ip is not null && await _context.DemoRequests.CountAsync(r => r.IpAddress == ip && r.CreatedAt > dayAgo, cancellationToken) >= _options.MaxPerAddressPerDay)
        {
            await DropAsync("too_many_from_address", ip, cancellationToken);
            return DemoRequestSubmitResult.Of(DemoRequestSubmitOutcome.Dropped);
        }

        // The same person sending again while the first request is open and recent adds to it: one request, one answer.
        var normalized = EmailNormalizer.Normalize(email!);
        var since = now.AddHours(-_options.RepeatWindowHours);
        var open = await _context.DemoRequests
            .Where(r => r.NormalizedEmail == normalized && r.CreatedAt > since && DemoRequestStatuses.Open.Contains(r.Status))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (open is not null)
        {
            open.RepeatCount++;
            open.LastRepeatedAt = now;
            _context.DemoRequestEvents.Add(new DemoRequestEvent
            {
                EventId = Ids.New(),
                DemoRequestId = open.DemoRequestId,
                EventType = DemoRequestEventTypes.Repeated,
                Note = message,
                CreatedAt = now,
            });
            _audit.Append(AuditEvents.DemoRequestRepeated, EntityType, open.DemoRequestId.ToString(), new { ip, count = open.RepeatCount });
            using (_context.BeginSystemScope())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return DemoRequestSubmitResult.Of(DemoRequestSubmitOutcome.Repeated, open.DemoRequestId);
        }

        var request = new DemoRequest
        {
            DemoRequestId = Ids.New(),
            FullName = fullName!,
            Email = email!,
            NormalizedEmail = normalized,
            OrganizationName = organizationName!,
            CountryCode = country,
            JobTitle = jobTitle,
            Phone = phone,
            Industry = industry,
            OrganizationSize = size,
            DatabaseSystems = databases,
            DemoLanguage = demoLanguage,
            PreferredTime = preferredTime,
            Message = message,
            PageLanguage = pageLanguage,
            ConsentAt = now,
            IpAddress = ip,
            UserAgent = Bounded(userAgent, 400),
            CreatedAt = now,
            Status = DemoRequestStatuses.New,
        };
        _context.DemoRequests.Add(request);
        _context.DemoRequestEvents.Add(new DemoRequestEvent
        {
            EventId = Ids.New(),
            DemoRequestId = request.DemoRequestId,
            EventType = DemoRequestEventTypes.Submitted,
            NewStatus = DemoRequestStatuses.New,
            CreatedAt = now,
        });
        _audit.Append(AuditEvents.DemoRequestSubmitted, EntityType, request.DemoRequestId.ToString(), new { country, ip });

        // Every active Platform Administrator sees it in the notification centre.
        var administrators = await _context.Administrators.AsNoTracking().Where(a => a.IsActive)
            .Select(a => new NotificationRecipient(PrincipalType.PlatformAdministrator, a.AdministratorId, null, a.Email, a.FullName, a.PreferredLanguage))
            .ToListAsync(cancellationToken);
        _notifications.Notify(administrators, NotificationTypes.DemoRequestCreated, EntityType, request.DemoRequestId.ToString(),
            new { organizationName = request.OrganizationName, fullName = request.FullName, countryCode = request.CountryCode }, null);

        using (_context.BeginSystemScope())
        {
            await _context.SaveChangesAsync(cancellationToken);

            // The request is stored whatever happens to the e-mails: the outbox records each delivery and its failure.
            var openUrl = $"{_urls.BaseUrl.TrimEnd('/')}/platform/demo-requests/{request.DemoRequestId}";
            if (EmailNormalizer.IsPlausible(_options.NotifyEmail))
            {
                var details = new List<(string, string)>
                {
                    ("Name", request.FullName),
                    ("Work e-mail", request.Email),
                    ("Organization", request.OrganizationName),
                    ("Country", request.CountryCode),
                    ("Job title", request.JobTitle ?? string.Empty),
                    ("Phone", request.Phone ?? string.Empty),
                    ("Industry", request.Industry ?? string.Empty),
                    ("Organization size", request.OrganizationSize ?? string.Empty),
                    ("Database systems", request.DatabaseSystems ?? string.Empty),
                    ("Demo language", request.DemoLanguage == "ar" ? "Arabic" : "English"),
                    ("Preferred time", request.PreferredTime ?? string.Empty),
                    ("Message", request.Message ?? string.Empty),
                    ("Sent from", request.IpAddress ?? string.Empty),
                };
                await _outbox.EnqueueAsync(
                    EmailTemplates.DemoRequestReceived(_options.NotifyEmail.Trim(), request.FullName, request.OrganizationName, details, openUrl, request.Email),
                    EmailTemplateCodes.DemoRequestReceived, null, cancellationToken);
            }

            await _outbox.EnqueueAsync(
                EmailTemplates.DemoRequestConfirmation(request.Email, request.FullName, request.PageLanguage, _urls.BaseUrl.TrimEnd('/'), string.IsNullOrWhiteSpace(_options.NotifyEmail) ? _email.ReplyTo : _options.NotifyEmail.Trim()),
                EmailTemplateCodes.DemoRequestConfirmation, null, cancellationToken);
        }

        return DemoRequestSubmitResult.Of(DemoRequestSubmitOutcome.Created, request.DemoRequestId);
    }

    public async Task<PagedResult<DemoRequestListItem>> ListAsync(DemoRequestListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireAdministrator();
        await EraseExpiredAsync(_clock.UtcNow, cancellationToken);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var requests = _context.DemoRequests.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            requests = requests.Where(r => r.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.CountryCode))
        {
            var country = query.CountryCode.Trim().ToUpperInvariant();
            requests = requests.Where(r => r.CountryCode == country);
        }

        if (query.From is { } from)
        {
            requests = requests.Where(r => r.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            requests = requests.Where(r => r.CreatedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            requests = requests.Where(r => r.FullName.Contains(term) || r.Email.Contains(term) || r.OrganizationName.Contains(term));
        }

        var total = await requests.CountAsync(cancellationToken);
        var items = await requests.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new DemoRequestListItem(
                r.DemoRequestId, r.FullName, r.Email, r.OrganizationName, r.CountryCode, r.JobTitle, r.Status, r.CreatedAt, r.ScheduledAt, r.PerformedAt,
                _context.Administrators.Where(a => a.AdministratorId == r.AssignedAdministratorId).Select(a => a.FullName).FirstOrDefault(),
                r.RepeatCount, r.ErasedAt != null))
            .ToListAsync(cancellationToken);
        return new PagedResult<DemoRequestListItem>(items, page, pageSize, total);
    }

    public async Task<DemoRequestSummary> SummaryAsync(CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var counts = await _context.DemoRequests.AsNoTracking().GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return new DemoRequestSummary(
            counts.Where(c => c.Status == DemoRequestStatuses.New).Sum(c => c.Count),
            counts.Where(c => DemoRequestStatuses.Open.Contains(c.Status)).Sum(c => c.Count),
            counts.Sum(c => c.Count));
    }

    public async Task<DemoRequestDetail?> GetAsync(Guid demoRequestId, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var request = await _context.DemoRequests.AsNoTracking().SingleOrDefaultAsync(r => r.DemoRequestId == demoRequestId, cancellationToken);
        return request is null ? null : await ToDetailAsync(request, cancellationToken);
    }

    public async Task<DemoRequestDetail> UpdateAsync(Guid demoRequestId, UpdateDemoRequestCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var administratorId = RequireAdministrator();
        var request = await _context.DemoRequests.SingleOrDefaultAsync(r => r.DemoRequestId == demoRequestId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "demo_request_not_found");
        if (request.ErasedAt is not null)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "demo_request_erased");
        }

        if (!DemoRequestStatuses.All.Contains(command.Status))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "demo_status_invalid");
        }

        if (command.Impression is not null && !DemoImpressions.All.Contains(command.Impression))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "demo_impression_invalid");
        }

        // A scheduled demo has a date; a performed one says when and who attended.
        if (command.Status == DemoRequestStatuses.Scheduled && command.ScheduledAt is null)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "demo_scheduled_date_required");
        }

        if (command.Status is DemoRequestStatuses.Performed or DemoRequestStatuses.Converted && (command.PerformedAt is null || string.IsNullOrWhiteSpace(command.AttendeeName)))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "demo_performed_details_required");
        }

        if (!string.IsNullOrWhiteSpace(command.AttendeeEmail) && !EmailNormalizer.IsPlausible(command.AttendeeEmail.Trim()))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "demo_attendee_email_invalid");
        }

        if (command.AssignedAdministratorId is { } assignee && !await _context.Administrators.AnyAsync(a => a.AdministratorId == assignee && a.IsActive, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "demo_assignee_invalid");
        }

        var now = _clock.UtcNow;
        var previous = request.Status;
        request.Status = command.Status;
        request.Remark = Text(command.Remark, 2000);
        request.AssignedAdministratorId = command.AssignedAdministratorId;
        request.ScheduledAt = command.ScheduledAt;
        request.MeetingLocation = Text(command.MeetingLocation, 400);
        request.PerformedAt = command.PerformedAt;
        request.PresentedBy = Text(command.PresentedBy, 150);
        request.AttendeeName = Text(command.AttendeeName, 150);
        request.AttendeePosition = Text(command.AttendeePosition, 150);
        request.AttendeeEmail = Text(command.AttendeeEmail, 254);
        request.AttendeePhone = Text(command.AttendeePhone, 40);
        request.AttendeeMobile = Text(command.AttendeeMobile, 40);
        request.Impression = command.Impression;
        request.PerformedRemark = Text(command.PerformedRemark, 2000);
        request.NextStep = Text(command.NextStep, 500);
        request.FollowUpDate = command.FollowUpDate;
        request.UpdatedAt = now;
        request.UpdatedByAdministratorId = administratorId;

        var statusChanged = !string.Equals(previous, request.Status, StringComparison.Ordinal);
        _context.DemoRequestEvents.Add(new DemoRequestEvent
        {
            EventId = Ids.New(),
            DemoRequestId = request.DemoRequestId,
            EventType = statusChanged ? DemoRequestEventTypes.StatusChanged : DemoRequestEventTypes.Updated,
            PreviousStatus = statusChanged ? previous : null,
            NewStatus = statusChanged ? request.Status : null,
            Note = Text(command.Note, 2000),
            ActorAdministratorId = administratorId,
            ActorDisplayName = _tenantContext.Principal.DisplayName,
            CreatedAt = now,
        });
        _audit.Append(AuditEvents.DemoRequestUpdated, EntityType, request.DemoRequestId.ToString(), new { from = previous, to = request.Status, scheduledAt = request.ScheduledAt, performedAt = request.PerformedAt });
        await _context.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<DemoAssigneeDto>> AssigneesAsync(CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        return await _context.Administrators.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.FullName)
            .Select(a => new DemoAssigneeDto(a.AdministratorId, a.FullName)).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// At the end of the retention period the personal details of a request that did not become a customer are erased:
    /// the row stays with its organization's country, status and dates (for the counts), the history goes.
    /// </summary>
    private async Task EraseExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddMonths(-_options.RetentionMonths);
        var expired = await _context.DemoRequests
            .Where(r => r.ErasedAt == null && r.CreatedAt < cutoff && r.Status != DemoRequestStatuses.Converted)
            .Select(r => r.DemoRequestId).Take(500).ToListAsync(cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        // The history is append-only: a set-based delete is the only sanctioned way to remove it.
        await _context.DemoRequestEvents.Where(e => expired.Contains(e.DemoRequestId)).ExecuteDeleteAsync(cancellationToken);
        await _context.DemoRequests.Where(r => expired.Contains(r.DemoRequestId)).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.FullName, Erased).SetProperty(r => r.Email, Erased).SetProperty(r => r.NormalizedEmail, r => "ERASED:" + r.DemoRequestId.ToString())
            .SetProperty(r => r.OrganizationName, Erased).SetProperty(r => r.JobTitle, (string?)null).SetProperty(r => r.Phone, (string?)null)
            .SetProperty(r => r.DatabaseSystems, (string?)null).SetProperty(r => r.PreferredTime, (string?)null).SetProperty(r => r.Message, (string?)null)
            .SetProperty(r => r.IpAddress, (string?)null).SetProperty(r => r.UserAgent, (string?)null).SetProperty(r => r.Remark, (string?)null)
            .SetProperty(r => r.MeetingLocation, (string?)null).SetProperty(r => r.PresentedBy, (string?)null).SetProperty(r => r.AttendeeName, (string?)null)
            .SetProperty(r => r.AttendeePosition, (string?)null).SetProperty(r => r.AttendeeEmail, (string?)null).SetProperty(r => r.AttendeePhone, (string?)null)
            .SetProperty(r => r.AttendeeMobile, (string?)null).SetProperty(r => r.PerformedRemark, (string?)null).SetProperty(r => r.NextStep, (string?)null)
            .SetProperty(r => r.ErasedAt, now), cancellationToken);
        _audit.Append(AuditEvents.DemoRequestsErased, EntityType, null, new { count = expired.Count, months = _options.RetentionMonths });
        using (_context.BeginSystemScope())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DropAsync(string reason, string? ip, CancellationToken cancellationToken)
    {
        _audit.Append(AuditEvents.DemoRequestDropped, EntityType, null, new { reason, ip });
        using (_context.BeginSystemScope())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<DemoRequestDetail> ToDetailAsync(DemoRequest r, CancellationToken cancellationToken)
    {
        var assignee = r.AssignedAdministratorId is { } id
            ? await _context.Administrators.AsNoTracking().Where(a => a.AdministratorId == id).Select(a => a.FullName).FirstOrDefaultAsync(cancellationToken)
            : null;
        var events = await _context.DemoRequestEvents.AsNoTracking().Where(e => e.DemoRequestId == r.DemoRequestId).OrderByDescending(e => e.CreatedAt)
            .Select(e => new DemoRequestEventDto(e.EventId, e.EventType, e.PreviousStatus, e.NewStatus, e.Note, e.ActorDisplayName, e.CreatedAt))
            .ToListAsync(cancellationToken);
        return new DemoRequestDetail(
            r.DemoRequestId, r.FullName, r.Email, r.OrganizationName, r.CountryCode, r.JobTitle, r.Phone, r.Industry, r.OrganizationSize, r.DatabaseSystems,
            r.DemoLanguage, r.PreferredTime, r.Message, r.PageLanguage, r.ConsentAt, r.IpAddress, r.UserAgent, r.CreatedAt, r.RepeatCount, r.LastRepeatedAt,
            r.Status, r.Remark, r.AssignedAdministratorId, assignee, r.ScheduledAt, r.MeetingLocation, r.PerformedAt, r.PresentedBy, r.AttendeeName,
            r.AttendeePosition, r.AttendeeEmail, r.AttendeePhone, r.AttendeeMobile, r.Impression, r.PerformedRemark, r.NextStep, r.FollowUpDate, r.UpdatedAt,
            r.ErasedAt, events);
    }

    /// <summary>Demo requests are the platform's: whoever reads or changes one is a Platform Administrator, checked here as well as at the endpoint.</summary>
    private Guid RequireAdministrator()
    {
        var principal = _tenantContext.Principal;
        if (principal.PrincipalType != PrincipalType.PlatformAdministrator || principal.AdministratorId is not { } id)
        {
            throw new TenantContextException("Demo requests are read by Platform Administrators only.");
        }

        return id;
    }

    private static string? Required(string? value, int max, string field, Dictionary<string, string> errors)
    {
        var text = Optional(value, max, field, errors);
        if (text is null && !errors.ContainsKey(field))
        {
            errors[field] = "required";
        }

        return text;
    }

    /// <summary>Trimmed, bounded, and free of control characters: the value goes into e-mails as well as the database.</summary>
    private static string? Optional(string? value, int max, string field, Dictionary<string, string> errors, bool multiline = false)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > max)
        {
            errors[field] = "too_long";
            return null;
        }

        if (text.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r')))
        {
            errors[field] = "invalid";
            return null;
        }

        return text;
    }

    private static string? OneOf(string? value, IReadOnlyList<string> allowed, string field, Dictionary<string, string> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (!allowed.Contains(text))
        {
            errors[field] = "invalid";
            return null;
        }

        return text;
    }

    private static string? Language(string? value)
        => value?.Trim().ToLowerInvariant() switch { "ar" => "ar", "en" => "en", _ => null };

    private static string? Bounded(string? value, int length)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= length ? text : text[..length];
    }

    /// <summary>An administrator's text: trimmed and cut to the column, empty stored as nothing.</summary>
    private static string? Text(string? value, int length) => Bounded(value, length);
}
