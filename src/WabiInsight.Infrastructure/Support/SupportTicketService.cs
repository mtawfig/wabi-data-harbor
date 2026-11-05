using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Security;
using WabiInsight.Core.Storage;
using WabiInsight.Core.Support;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Support;

/// <summary>
/// Support desk implementation (specification §15). Visibility is resolved from the tenant context only;
/// every state change goes through <see cref="TicketStateMachine"/> and lands on the append-only timeline.
/// </summary>
public sealed class SupportTicketService : ISupportTicketService
{
    private const string AttachmentCategory = "support-attachments";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IAuditWriter _audit;
    private readonly IUsageEventWriter _usage;
    private readonly INotificationDispatcher _notifications;
    private readonly IArtifactStorage _storage;
    private readonly IAttachmentScanner _scanner;
    private readonly SupportDeskOptions _options;

    public SupportTicketService(
        ConfigurationDbContext context,
        ITenantContext tenantContext,
        IClock clock,
        IAuditWriter audit,
        IUsageEventWriter usage,
        INotificationDispatcher notifications,
        IArtifactStorage storage,
        IAttachmentScanner scanner,
        IOptions<SupportDeskOptions> options)
    {
        _context = context;
        _tenantContext = tenantContext;
        _clock = clock;
        _audit = audit;
        _usage = usage;
        _notifications = notifications;
        _storage = storage;
        _scanner = scanner;
        _options = options.Value;
    }

    public async Task<TicketDetailDto> CreateAsync(CreateTicketCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = _tenantContext.RequireOrganizationId();
        var userId = _tenantContext.Principal.UserId!.Value;
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(command.Subject) || command.Subject.Trim().Length is < 3 or > 200)
        {
            errors.Add("ticket_subject_invalid");
        }

        if (string.IsNullOrWhiteSpace(command.Description) || command.Description.Trim().Length > 8000)
        {
            errors.Add("ticket_description_invalid");
        }

        if (command.Category is { Length: > 100 })
        {
            errors.Add("ticket_category_invalid");
        }

        if (!Enum.IsDefined(command.TicketType) || !Enum.IsDefined(command.Impact))
        {
            errors.Add("ticket_type_or_impact_invalid");
        }

        if (errors.Count > 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, [.. errors]);
        }

        var now = _clock.UtcNow;
        var ticket = new SupportTicket
        {
            TicketId = Ids.New(),
            OrganizationId = organizationId,
            CreatedByUserId = userId,
            TicketType = command.TicketType,
            Impact = command.Impact,
            Category = string.IsNullOrWhiteSpace(command.Category) ? null : command.Category.Trim(),
            Subject = command.Subject.Trim(),
            Description = command.Description.Trim(),
            Status = TicketStatus.New,
            RequiresSupportAccess = command.RequiresSupportAccess,
            CreatedAt = now,
            LastActivityAt = now,
        };

        var administrators = await ActiveAdministratorsAsync(cancellationToken);
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            ticket.ReferenceNumber = await ReserveReferenceAsync(now.Year, cancellationToken);
            _context.SupportTickets.Add(ticket);
            AddEvent(ticket, TicketEventTypes.Created, null, TicketStatus.New, new { ticket.ReferenceNumber, ticket.TicketType, ticket.Impact });
            _audit.Append(AuditEvents.TicketCreated, "SupportTicket", ticket.TicketId.ToString(), new { ticket.ReferenceNumber, ticket.TicketType, ticket.Impact }, organizationId);
            _usage.Append(UsageEventTypes.TicketCreated, "SupportTicket", ticket.TicketId.ToString(), metadata: new { ticket.TicketType, ticket.Impact }, organizationId: organizationId);
            _notifications.Notify(administrators, NotificationTypes.TicketCreated, "SupportTicket", ticket.TicketId.ToString(), await NotificationDataAsync(ticket, cancellationToken), organizationId);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        await _notifications.EmailAsync(
            administrators,
            EmailTemplateCodes.SupportNotification,
            $"[{ticket.ReferenceNumber}] New support ticket: {ticket.Subject}",
            $"[{ticket.ReferenceNumber}] تذكرة دعم جديدة: {ticket.Subject}",
            $"{_tenantContext.Principal.DisplayName} opened a {ticket.Impact} {ticket.TicketType} ticket: {ticket.Subject}",
            $"فتح {_tenantContext.Principal.DisplayName} تذكرة جديدة ({ticket.TicketType} / {ticket.Impact}): {ticket.Subject}",
            $"platform/support/{ticket.TicketId}",
            organizationId,
            cancellationToken);

        return await GetAsync(ticket.TicketId, cancellationToken);
    }

    public async Task<PagedResult<TicketSummaryDto>> ListAsync(TicketListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var tickets = VisibleTickets().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            tickets = tickets.Where(t => t.ReferenceNumber.Contains(term) || t.Subject.Contains(term) || (t.Category != null && t.Category.Contains(term)));
        }

        if (query.Status is { } status)
        {
            tickets = tickets.Where(t => t.Status == status);
        }

        if (query.Open is { } open)
        {
            tickets = open
                ? tickets.Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Cancelled)
                : tickets.Where(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Cancelled);
        }

        if (query.TicketType is { } type)
        {
            tickets = tickets.Where(t => t.TicketType == type);
        }

        if (query.Impact is { } impact)
        {
            tickets = tickets.Where(t => t.Impact == impact);
        }

        if (query.OrganizationId is { } organizationId && _tenantContext.IsPlatformAdministrator)
        {
            tickets = tickets.Where(t => t.OrganizationId == organizationId);
        }

        if (query.AssignedAdministratorId is { } assigned)
        {
            tickets = tickets.Where(t => t.AssignedAdministratorId == assigned);
        }

        if (query.Unassigned == true)
        {
            tickets = tickets.Where(t => t.AssignedAdministratorId == null);
        }

        if (query.From is { } from)
        {
            tickets = tickets.Where(t => t.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            tickets = tickets.Where(t => t.CreatedAt <= to);
        }

        tickets = (query.Sort?.ToUpperInvariant(), query.Descending) switch
        {
            ("CREATEDAT", true) => tickets.OrderByDescending(t => t.CreatedAt),
            ("CREATEDAT", false) => tickets.OrderBy(t => t.CreatedAt),
            ("IMPACT", true) => tickets.OrderByDescending(t => t.Impact).ThenByDescending(t => t.LastActivityAt),
            ("IMPACT", false) => tickets.OrderBy(t => t.Impact).ThenByDescending(t => t.LastActivityAt),
            ("STATUS", true) => tickets.OrderByDescending(t => t.Status).ThenByDescending(t => t.LastActivityAt),
            ("STATUS", false) => tickets.OrderBy(t => t.Status).ThenByDescending(t => t.LastActivityAt),
            ("REFERENCE", true) => tickets.OrderByDescending(t => t.ReferenceNumber),
            ("REFERENCE", false) => tickets.OrderBy(t => t.ReferenceNumber),
            (_, false) => tickets.OrderBy(t => t.LastActivityAt),
            _ => tickets.OrderByDescending(t => t.LastActivityAt),
        };

        var total = await tickets.CountAsync(cancellationToken);
        var items = await ProjectSummary(tickets.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<TicketSummaryDto>(items, page, pageSize, total);
    }

    public async Task<TicketDetailDto> GetAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadVisibleAsync(ticketId, cancellationToken);
        if (_tenantContext.IsPlatformAdministrator)
        {
            if (ticket.FirstOpenedAt is null)
            {
                // First platform view: recorded once, never overwritten (specification §15.9).
                var now = _clock.UtcNow;
                ticket.FirstOpenedByAdministratorId = _tenantContext.Principal.AdministratorId;
                ticket.FirstOpenedAt = now;
                var previous = ticket.Status;
                if (ticket.Status == TicketStatus.New)
                {
                    ticket.Status = TicketStatus.Read;
                }

                AddEvent(ticket, TicketEventTypes.Viewed, previous, ticket.Status, null);
                _audit.Append(AuditEvents.TicketViewedByPlatform, "SupportTicket", ticket.TicketId.ToString(), new { ticket.ReferenceNumber, first = true }, ticket.OrganizationId);
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await _audit.AppendAndSaveAsync(AuditEvents.TicketViewedByPlatform, "SupportTicket", ticket.TicketId.ToString(), new { ticket.ReferenceNumber, first = false }, ticket.OrganizationId, cancellationToken);
            }
        }

        return await DetailAsync(ticket, cancellationToken);
    }

    public async Task<IReadOnlyList<TicketEventDto>> ListEventsAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadVisibleAsync(ticketId, cancellationToken);
        return await Scoped(_context.SupportTicketEvents).AsNoTracking()
            .Where(e => e.TicketId == ticket.TicketId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new TicketEventDto(e.EventId, e.ActorType, e.ActorId, e.ActorDisplayName, e.EventType, e.PreviousStatus, e.NewStatus, e.DetailsJson, e.Note, e.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TicketCommentDto>> ListCommentsAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadVisibleAsync(ticketId, cancellationToken);
        return await Scoped(_context.SupportTicketComments).AsNoTracking()
            .Where(c => c.TicketId == ticket.TicketId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new TicketCommentDto(c.CommentId, c.AuthorType, c.AuthorId, c.AuthorDisplayName, c.Body, c.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<TicketCommentDto> AddCommentAsync(Guid ticketId, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > 8000)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ticket_comment_invalid");
        }

        var ticket = await LoadVisibleAsync(ticketId, cancellationToken);
        RequireOpen(ticket);
        var actor = ActorRole(ticket);
        var principal = _tenantContext.Principal;
        var now = _clock.UtcNow;
        var comment = new SupportTicketComment
        {
            CommentId = Ids.New(),
            TicketId = ticket.TicketId,
            OrganizationId = ticket.OrganizationId,
            AuthorType = principal.PrincipalType,
            AuthorId = principal.AdministratorId ?? principal.UserId,
            AuthorDisplayName = principal.DisplayName,
            Body = body.Trim(),
            CreatedAt = now,
        };
        _context.SupportTicketComments.Add(comment);
        AddEvent(ticket, TicketEventTypes.CommentAdded, null, null, new { comment.CommentId, length = comment.Body.Length });

        // A reply from the organization side ends the wait for the user's response (specification §15.3).
        if (ticket.Status == TicketStatus.PendingUserResponse && actor is TicketActorRole.Requester or TicketActorRole.OrganizationAdmin)
        {
            ApplyStatus(ticket, TicketStatus.Started, "auto_reply", now);
        }

        ticket.LastActivityAt = now;
        _audit.Append(AuditEvents.TicketCommentAdded, "SupportTicket", ticket.TicketId.ToString(), new { ticket.ReferenceNumber, comment.CommentId }, ticket.OrganizationId);
        _usage.Append(UsageEventTypes.TicketCommentAdded, "SupportTicket", ticket.TicketId.ToString(), organizationId: ticket.OrganizationId);

        var recipients = actor == TicketActorRole.PlatformAdministrator
            ? await RequesterAsync(ticket, cancellationToken)
            : await HandlersAsync(ticket, cancellationToken);
        var data = await NotificationDataAsync(ticket, cancellationToken);
        _notifications.Notify(recipients, NotificationTypes.TicketCommentAdded, "SupportTicket", ticket.TicketId.ToString(), data, ticket.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);

        await _notifications.EmailAsync(
            recipients,
            EmailTemplateCodes.SupportNotification,
            $"[{ticket.ReferenceNumber}] New reply: {ticket.Subject}",
            $"[{ticket.ReferenceNumber}] رد جديد: {ticket.Subject}",
            $"{principal.DisplayName} replied on ticket {ticket.ReferenceNumber}.",
            $"أضاف {principal.DisplayName} ردًا على التذكرة {ticket.ReferenceNumber}.",
            LinkFor(actor == TicketActorRole.PlatformAdministrator ? "app" : "platform", ticket),
            ticket.OrganizationId,
            cancellationToken);

        return new TicketCommentDto(comment.CommentId, comment.AuthorType, comment.AuthorId, comment.AuthorDisplayName, comment.Body, comment.CreatedAt);
    }

    public async Task<TicketDetailDto> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Note is { Length: > 4000 })
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ticket_note_invalid");
        }

        var ticket = await LoadVisibleAsync(ticketId, cancellationToken);
        var actor = ActorRole(ticket);
        var now = _clock.UtcNow;
        var hasNote = !string.IsNullOrWhiteSpace(command.Note);
        var error = TicketStateMachine.Validate(ticket.Status, command.Status, actor, hasNote, ticket.ResolvedAt, now, _options.ReopenWindowDays);
        if (error is not null)
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, error);
        }

        var previous = ticket.Status;
        var reopened = previous == TicketStatus.Resolved && command.Status == TicketStatus.Started;
        ApplyStatus(ticket, command.Status, hasNote ? command.Note!.Trim() : null, now, reopened);
        if (command.Status == TicketStatus.Resolved)
        {
            ticket.ResolutionSummary = command.Note!.Trim();
            _usage.Append(UsageEventTypes.TicketResolved, "SupportTicket", ticket.TicketId.ToString(), organizationId: ticket.OrganizationId);
        }
        else if (command.Status == TicketStatus.Cancelled)
        {
            _usage.Append(UsageEventTypes.TicketCancelled, "SupportTicket", ticket.TicketId.ToString(), organizationId: ticket.OrganizationId);
        }

        ticket.LastActivityAt = now;
        _audit.Append(AuditEvents.TicketStatusChanged, "SupportTicket", ticket.TicketId.ToString(), new { ticket.ReferenceNumber, previous, next = command.Status }, ticket.OrganizationId);

        var recipients = actor == TicketActorRole.PlatformAdministrator
            ? await RequesterAsync(ticket, cancellationToken)
            : await HandlersAsync(ticket, cancellationToken);
        var data = await NotificationDataAsync(ticket, cancellationToken, previous, command.Status);
        _notifications.Notify(recipients, NotificationTypes.TicketStatusChanged, "SupportTicket", ticket.TicketId.ToString(), data, ticket.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);

        await _notifications.EmailAsync(
            recipients,
            EmailTemplateCodes.SupportNotification,
            $"[{ticket.ReferenceNumber}] Status: {command.Status}",
            $"[{ticket.ReferenceNumber}] الحالة: {command.Status}",
            $"Ticket {ticket.ReferenceNumber} \"{ticket.Subject}\" moved from {previous} to {command.Status}.",
            $"انتقلت التذكرة {ticket.ReferenceNumber} \"{ticket.Subject}\" من {previous} إلى {command.Status}.",
            LinkFor(actor == TicketActorRole.PlatformAdministrator ? "app" : "platform", ticket),
            ticket.OrganizationId,
            cancellationToken);

        return await DetailAsync(ticket, cancellationToken);
    }

    public async Task<TicketDetailDto> AssignAsync(Guid ticketId, Guid? administratorId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var ticket = await LoadVisibleAsync(ticketId, cancellationToken);
        RequireOpen(ticket);
        Administrator? target = null;
        if (administratorId is { } id)
        {
            target = await _context.Administrators.AsNoTracking().SingleOrDefaultAsync(a => a.AdministratorId == id && a.IsActive, cancellationToken)
                ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "administrator_not_found");
        }

        if (ticket.AssignedAdministratorId == administratorId)
        {
            return await DetailAsync(ticket, cancellationToken);
        }

        var previous = ticket.AssignedAdministratorId;
        ticket.AssignedAdministratorId = administratorId;
        ticket.LastActivityAt = _clock.UtcNow;
        AddEvent(ticket, administratorId is null ? TicketEventTypes.Unassigned : TicketEventTypes.Assigned, null, null, new { previous, assignedTo = administratorId, assignedToName = target?.FullName });
        _audit.Append(AuditEvents.TicketAssigned, "SupportTicket", ticket.TicketId.ToString(), new { ticket.ReferenceNumber, previous, assignedTo = administratorId }, ticket.OrganizationId);

        var recipients = target is null || target.AdministratorId == _tenantContext.Principal.AdministratorId
            ? []
            : new List<NotificationRecipient> { Recipient(target) };
        _notifications.Notify(recipients, NotificationTypes.TicketAssigned, "SupportTicket", ticket.TicketId.ToString(), await NotificationDataAsync(ticket, cancellationToken), ticket.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
        await _notifications.EmailAsync(
            recipients,
            EmailTemplateCodes.SupportNotification,
            $"[{ticket.ReferenceNumber}] Assigned to you: {ticket.Subject}",
            $"[{ticket.ReferenceNumber}] أُسندت إليك: {ticket.Subject}",
            $"{_tenantContext.Principal.DisplayName} assigned ticket {ticket.ReferenceNumber} to you.",
            $"أسند {_tenantContext.Principal.DisplayName} التذكرة {ticket.ReferenceNumber} إليك.",
            LinkFor("platform", ticket),
            ticket.OrganizationId,
            cancellationToken);
        return await DetailAsync(ticket, cancellationToken);
    }

    public async Task<IReadOnlyList<TicketAttachmentDto>> ListAttachmentsAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadVisibleAsync(ticketId, cancellationToken);
        return await Scoped(_context.SupportTicketAttachments).AsNoTracking()
            .Where(a => a.TicketId == ticket.TicketId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new TicketAttachmentDto(a.AttachmentId, a.CommentId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedByType, a.UploadedByDisplayName, a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<TicketAttachmentDto> AddAttachmentAsync(Guid ticketId, AttachmentUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        var ticket = await LoadVisibleAsync(ticketId, cancellationToken);
        RequireOpen(ticket);

        var fileName = Path.GetFileName(upload.FileName ?? string.Empty).Trim();
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (fileName.Length is 0 or > 255 || extension.Length == 0 || !_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "attachment_type_not_allowed");
        }

        if (upload.Length <= 0 || upload.Length > _options.MaxAttachmentBytes)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "attachment_too_large");
        }

        // Buffer (≤ 10 MB) so the hash, the scan and the store see the same bytes, and the declared length is enforced.
        using var buffer = new MemoryStream();
        await CopyBoundedAsync(upload.Content, buffer, _options.MaxAttachmentBytes, cancellationToken);
        buffer.Position = 0;
        var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, cancellationToken));
        buffer.Position = 0;
        var scan = await _scanner.ScanAsync(fileName, buffer, cancellationToken);
        if (!scan.IsClean)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "attachment_rejected_by_scan");
        }

        buffer.Position = 0;
        var key = await _storage.SaveAsync(AttachmentCategory, buffer, cancellationToken);
        var principal = _tenantContext.Principal;
        var now = _clock.UtcNow;
        var attachment = new SupportTicketAttachment
        {
            AttachmentId = Ids.New(),
            TicketId = ticket.TicketId,
            OrganizationId = ticket.OrganizationId,
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType.Trim(),
            SizeBytes = buffer.Length,
            StorageKey = key,
            Sha256 = sha256,
            ScanStatus = AttachmentScanStatus.Clean,
            UploadedByType = principal.PrincipalType,
            UploadedById = principal.AdministratorId ?? principal.UserId,
            UploadedByDisplayName = principal.DisplayName,
            CreatedAt = now,
        };

        try
        {
            _context.SupportTicketAttachments.Add(attachment);
            AddEvent(ticket, TicketEventTypes.AttachmentAdded, null, null, new { attachment.AttachmentId, attachment.FileName, attachment.SizeBytes });
            ticket.LastActivityAt = now;
            _audit.Append(AuditEvents.TicketAttachmentAdded, "SupportTicket", ticket.TicketId.ToString(), new { ticket.ReferenceNumber, attachment.AttachmentId, attachment.FileName, attachment.SizeBytes }, ticket.OrganizationId);
            _usage.Append(UsageEventTypes.TicketAttachmentAdded, "SupportTicket", ticket.TicketId.ToString(), quantity: attachment.SizeBytes, organizationId: ticket.OrganizationId);
            var actor = ActorRole(ticket);
            var recipients = actor == TicketActorRole.PlatformAdministrator ? await RequesterAsync(ticket, cancellationToken) : await HandlersAsync(ticket, cancellationToken);
            _notifications.Notify(recipients, NotificationTypes.TicketAttachmentAdded, "SupportTicket", ticket.TicketId.ToString(), await NotificationDataAsync(ticket, cancellationToken), ticket.OrganizationId);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        return new TicketAttachmentDto(attachment.AttachmentId, attachment.CommentId, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.UploadedByType, attachment.UploadedByDisplayName, attachment.CreatedAt);
    }

    public async Task<AttachmentDownload?> OpenAttachmentAsync(Guid ticketId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var ticket = await LoadVisibleAsync(ticketId, cancellationToken);
        var attachment = await Scoped(_context.SupportTicketAttachments).AsNoTracking()
            .SingleOrDefaultAsync(a => a.TicketId == ticket.TicketId && a.AttachmentId == attachmentId, cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        if (_tenantContext.IsPlatformAdministrator)
        {
            // Review C6: platform access to organization-provided content is allowed for support and always logged.
            await _audit.AppendAndSaveAsync(AuditEvents.TicketAttachmentDownloaded, "SupportTicket", ticket.TicketId.ToString(), new { ticket.ReferenceNumber, attachment.AttachmentId, attachment.FileName }, ticket.OrganizationId, cancellationToken);
        }

        return new AttachmentDownload(
            new TicketAttachmentDto(attachment.AttachmentId, attachment.CommentId, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.UploadedByType, attachment.UploadedByDisplayName, attachment.CreatedAt),
            stream);
    }

    // ---- visibility and helpers -------------------------------------------------------------

    private IQueryable<SupportTicket> VisibleTickets()
    {
        if (_tenantContext.IsPlatformAdministrator)
        {
            return _context.SupportTickets.AsPlatformScope(_tenantContext);
        }

        var userId = _tenantContext.Principal.UserId ?? throw new TenantContextException("Support tickets require an authenticated principal.");
        var tickets = _context.SupportTickets; // tenant filter applies
        return _tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin) ? tickets : tickets.Where(t => t.CreatedByUserId == userId);
    }

    private IQueryable<T> Scoped<T>(IQueryable<T> query)
        where T : class, ITenantOwned
        => _tenantContext.IsPlatformAdministrator ? query.AsPlatformScope(_tenantContext) : query;

    private async Task<SupportTicket> LoadVisibleAsync(Guid ticketId, CancellationToken cancellationToken)
        => await VisibleTickets().SingleOrDefaultAsync(t => t.TicketId == ticketId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "ticket_not_found");

    private TicketActorRole ActorRole(SupportTicket ticket)
    {
        var principal = _tenantContext.Principal;
        if (principal.PrincipalType == PrincipalType.PlatformAdministrator)
        {
            return TicketActorRole.PlatformAdministrator;
        }

        if (principal.PrincipalType != PrincipalType.OrganizationUser || principal.OrganizationId != ticket.OrganizationId)
        {
            return TicketActorRole.None;
        }

        if (principal.UserId == ticket.CreatedByUserId)
        {
            return TicketActorRole.Requester;
        }

        return principal.Roles.Contains(SystemRoles.OrganizationAdmin) ? TicketActorRole.OrganizationAdmin : TicketActorRole.None;
    }

    private void RequirePlatformAdministrator()
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Only platform administrators can assign tickets.");
        }
    }

    private static void RequireOpen(SupportTicket ticket)
    {
        if (!TicketStateMachine.IsOpen(ticket.Status))
        {
            throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "ticket_closed");
        }
    }

    private void ApplyStatus(SupportTicket ticket, TicketStatus next, string? note, DateTimeOffset now, bool reopened = false)
    {
        var previous = ticket.Status;
        ticket.Status = next;
        switch (next)
        {
            case TicketStatus.Started:
                ticket.StartedAt ??= now;
                if (reopened)
                {
                    ticket.ReopenedCount++;
                    ticket.ResolvedAt = null;
                }

                break;
            case TicketStatus.PendingUserResponse:
                ticket.PendingResponseAt = now;
                break;
            case TicketStatus.Resolved:
                ticket.ResolvedAt = now;
                break;
            case TicketStatus.Cancelled:
                ticket.CancelledAt = now;
                break;
        }

        AddEvent(ticket, reopened ? TicketEventTypes.Reopened : TicketEventTypes.StatusChanged, previous, next, null, note);
    }

    private void AddEvent(SupportTicket ticket, string eventType, TicketStatus? previous, TicketStatus? next, object? details, string? note = null)
    {
        var principal = _tenantContext.Principal;
        _context.SupportTicketEvents.Add(new SupportTicketEvent
        {
            EventId = Ids.New(),
            TicketId = ticket.TicketId,
            OrganizationId = ticket.OrganizationId,
            ActorType = principal.PrincipalType,
            ActorId = principal.AdministratorId ?? principal.UserId,
            ActorDisplayName = principal.DisplayName,
            EventType = eventType,
            PreviousStatus = previous,
            NewStatus = next,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details, JsonOptions),
            Note = note,
            CreatedAt = _clock.UtcNow,
        });
    }

    /// <summary>UPDATE … OUTPUT on the year's counter row inside the open transaction; no MAX+1 (review B7).</summary>
    private async Task<string> ReserveReferenceAsync(int year, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction() ?? throw new InvalidOperationException("Ticket references are reserved inside the create transaction.");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE dbo.TicketSequences SET NextValue = NextValue + 1 OUTPUT inserted.NextValue - 1 WHERE [Year] = @year";
                var parameter = update.CreateParameter();
                parameter.ParameterName = "@year";
                parameter.DbType = DbType.Int32;
                parameter.Value = year;
                update.Parameters.Add(parameter);
                var value = await update.ExecuteScalarAsync(cancellationToken);
                if (value is long number)
                {
                    return Format(year, number);
                }
            }

            try
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO dbo.TicketSequences ([Year], NextValue) VALUES (@year, 2)";
                var parameter = insert.CreateParameter();
                parameter.ParameterName = "@year";
                parameter.DbType = DbType.Int32;
                parameter.Value = year;
                insert.Parameters.Add(parameter);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                return Format(year, 1);
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            {
                // Another transaction created the year row first; the UPDATE wins on the next attempt.
            }
        }

        throw new InvalidOperationException("Could not reserve a ticket reference.");
    }

    public static string Format(int year, long number) => $"WI-{year:D4}-{number:D6}";

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "attachment_too_large");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private IQueryable<TicketSummaryDto> ProjectSummary(IQueryable<SupportTicket> tickets)
    {
        var users = _tenantContext.IsPlatformAdministrator ? _context.OrganizationUsers.IgnoreQueryFilters() : _context.OrganizationUsers;
        return tickets.Select(t => new TicketSummaryDto(
            t.TicketId,
            t.ReferenceNumber,
            t.OrganizationId,
            _context.Organizations.Where(o => o.OrganizationId == t.OrganizationId).Select(o => o.OrganizationCode).First(),
            _context.Organizations.Where(o => o.OrganizationId == t.OrganizationId).Select(o => o.OrganizationName).First(),
            _context.Organizations.Where(o => o.OrganizationId == t.OrganizationId).Select(o => o.OrganizationNameAr).First(),
            t.TicketType,
            t.Impact,
            t.Category,
            t.Subject,
            t.Status,
            t.CreatedByUserId,
            users.Where(u => u.UserId == t.CreatedByUserId).Select(u => u.FullName).FirstOrDefault() ?? string.Empty,
            t.AssignedAdministratorId,
            _context.Administrators.Where(a => a.AdministratorId == t.AssignedAdministratorId).Select(a => a.FullName).FirstOrDefault(),
            t.RequiresSupportAccess,
            t.CreatedAt,
            t.LastActivityAt,
            _context.SupportTicketComments.Count(c => c.TicketId == t.TicketId),
            _context.SupportTicketAttachments.Count(a => a.TicketId == t.TicketId)));
    }

    private async Task<TicketDetailDto> DetailAsync(SupportTicket ticket, CancellationToken cancellationToken)
    {
        var summary = await ProjectSummary(Scoped(_context.SupportTickets).AsNoTracking().Where(t => t.TicketId == ticket.TicketId)).SingleAsync(cancellationToken);
        var actor = ActorRole(ticket);
        var now = _clock.UtcNow;
        var allowed = Enum.GetValues<TicketStatus>()
            .Where(s => TicketStateMachine.Validate(ticket.Status, s, actor, hasReason: true, ticket.ResolvedAt, now, _options.ReopenWindowDays) is null)
            .ToList();
        return new TicketDetailDto(
            summary,
            ticket.Description,
            ticket.FirstOpenedByAdministratorId,
            ticket.FirstOpenedAt,
            ticket.StartedAt,
            ticket.PendingResponseAt,
            ticket.ResolvedAt,
            ticket.CancelledAt,
            ticket.ReopenedCount,
            ticket.ResolutionSummary,
            ticket.UpdatedAt,
            allowed,
            actor != TicketActorRole.None && TicketStateMachine.IsOpen(ticket.Status),
            actor == TicketActorRole.PlatformAdministrator && TicketStateMachine.IsOpen(ticket.Status));
    }

    private async Task<object> NotificationDataAsync(SupportTicket ticket, CancellationToken cancellationToken, TicketStatus? previous = null, TicketStatus? next = null)
    {
        var organization = await _context.Organizations.AsNoTracking().Where(o => o.OrganizationId == ticket.OrganizationId).Select(o => new { o.OrganizationName, o.OrganizationNameAr }).SingleAsync(cancellationToken);
        return new
        {
            ticketId = ticket.TicketId,
            reference = ticket.ReferenceNumber,
            subject = ticket.Subject,
            organizationName = organization.OrganizationName,
            organizationNameAr = organization.OrganizationNameAr,
            actorName = _tenantContext.Principal.DisplayName,
            previousStatus = previous,
            newStatus = next,
        };
    }

    private async Task<List<NotificationRecipient>> ActiveAdministratorsAsync(CancellationToken cancellationToken)
        => await _context.Administrators.AsNoTracking().Where(a => a.IsActive)
            .Select(a => new NotificationRecipient(PrincipalType.PlatformAdministrator, a.AdministratorId, null, a.Email, a.FullName, a.PreferredLanguage))
            .ToListAsync(cancellationToken);

    /// <summary>The assigned administrator, or every active administrator when the ticket is unassigned.</summary>
    private async Task<List<NotificationRecipient>> HandlersAsync(SupportTicket ticket, CancellationToken cancellationToken)
    {
        if (ticket.AssignedAdministratorId is { } assigned)
        {
            var administrator = await _context.Administrators.AsNoTracking().SingleOrDefaultAsync(a => a.AdministratorId == assigned && a.IsActive, cancellationToken);
            if (administrator is not null)
            {
                return [Recipient(administrator)];
            }
        }

        return await ActiveAdministratorsAsync(cancellationToken);
    }

    private async Task<List<NotificationRecipient>> RequesterAsync(SupportTicket ticket, CancellationToken cancellationToken)
    {
        var users = _tenantContext.IsPlatformAdministrator ? _context.OrganizationUsers.AsPlatformScope(_tenantContext) : _context.OrganizationUsers;
        return await users.AsNoTracking()
            .Where(u => u.OrganizationId == ticket.OrganizationId && u.UserId == ticket.CreatedByUserId && u.Status == OrganizationUserStatus.Active)
            .Select(u => new NotificationRecipient(PrincipalType.OrganizationUser, u.UserId, u.OrganizationId, u.Email, u.FullName, u.PreferredLanguage))
            .ToListAsync(cancellationToken);
    }

    private static NotificationRecipient Recipient(Administrator administrator)
        => new(PrincipalType.PlatformAdministrator, administrator.AdministratorId, null, administrator.Email, administrator.FullName, administrator.PreferredLanguage);

    private static string LinkFor(string area, SupportTicket ticket) => $"{area}/support/{ticket.TicketId}";
}
