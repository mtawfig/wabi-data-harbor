using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Analysis;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Email;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Support;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Security;
using WabiInsight.Core.Notifications;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Storage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Authentication;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Analysis;

/// <summary>
/// Sharing (specification §24, review B11). Owners share their own analyses with active colleagues of the same
/// organization (VIEW, optional expiry, revocable); recipients read stored results and files through the share and
/// nothing else. Email delivery classifies every address as internal or external and applies the classification
/// rules: Restricted never leaves the organization, Confidential needs the owner's explicit disclosure approval,
/// Internal needs the standard confirmation. Every send or block is an append-only <see cref="EmailDraftEvent"/>.
/// </summary>
public sealed class SharingService : ISharingService
{
    private const int MaxRecipientsPerShare = 20;
    private const int MaxEmailRecipients = 10;
    private const int MaxMessageLength = 1000;
    private const int MaxSubjectLength = 200;
    private const int MaxBodyLength = 8000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IAnalysisHistoryService _history;
    private readonly IArtifactStorage _storage;
    private readonly INotificationDispatcher _notifications;
    private readonly IEmailOutbox _outbox;
    private readonly IAuditWriter _audit;
    private readonly IUsageEventWriter _usage;
    private readonly IClock _clock;
    private readonly PlatformUrlOptions _urls;
    private readonly EmailOptions _email;
    private readonly Reports.WholeListExcel? _wholeLists;
    private readonly Core.Ai.IAnswerExporter? _excel;
    private readonly Core.Monitoring.IAccessFactRecorder? _accessFacts;

    public SharingService(ConfigurationDbContext context, ITenantContext tenantContext, IAnalysisHistoryService history, IArtifactStorage storage, INotificationDispatcher notifications, IEmailOutbox outbox, IAuditWriter audit, IUsageEventWriter usage, IClock clock, IOptions<PlatformUrlOptions> urls, IOptions<EmailOptions> email, Reports.WholeListExcel? wholeLists = null, IEnumerable<Core.Ai.IAnswerExporter>? exporters = null, Core.Monitoring.IAccessFactRecorder? accessFacts = null)
    {
        _wholeLists = wholeLists;
        _excel = exporters?.FirstOrDefault(e => e.Format == ExportFormat.Xlsx);
        _accessFacts = accessFacts;
        _context = context;
        _tenantContext = tenantContext;
        _history = history;
        _storage = storage;
        _notifications = notifications;
        _outbox = outbox;
        _audit = audit;
        _usage = usage;
        _clock = clock;
        _urls = urls.Value;
        _email = email.Value;
    }

    private Guid OrganizationId => _tenantContext.RequireOrganizationId();

    private Guid UserId => _tenantContext.Principal.UserId ?? throw new TenantContextException("Sharing belongs to organization users.");

    private IQueryable<AnalysisHistory> Mine => _context.AnalysisHistories.Where(h => h.OwnerUserId == UserId && !h.IsDeleted);

    /// <summary>Shares the caller may read through right now: addressed to them, not revoked, not expired, on a live analysis.</summary>
    private IQueryable<AnalysisShare> Received
    {
        get
        {
            var now = _clock.UtcNow;
            return _context.AnalysisShares.Where(s => s.RecipientUserId == UserId && s.RevokedAt == null && (s.ExpiresAt == null || s.ExpiresAt > now) && !s.History!.IsDeleted);
        }
    }

    public async Task<IReadOnlyList<ShareRecipientDto>> SearchRecipientsAsync(string? search, CancellationToken cancellationToken = default)
    {
        var me = UserId;
        var users = _context.OrganizationUsers.AsNoTracking().Where(u => u.Status == OrganizationUserStatus.Active && u.UserId != me);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            users = users.Where(u => u.FullName.Contains(term) || u.Email.Contains(term));
        }

        return await users.OrderBy(u => u.FullName).Take(20)
            .Select(u => new ShareRecipientDto(u.UserId, u.FullName, u.Email,
                _context.Departments.Where(d => d.DepartmentId == u.DepartmentId).Select(d => d.DepartmentNameEn).FirstOrDefault(),
                _context.Departments.Where(d => d.DepartmentId == u.DepartmentId).Select(d => d.DepartmentNameAr).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShareDto>> ListSharesOfAsync(Guid historyId, CancellationToken cancellationToken = default)
    {
        await OwnedAsync(historyId, cancellationToken);
        return await ProjectShares(_context.AnalysisShares.Where(s => s.HistoryId == historyId).OrderByDescending(s => s.CreatedAt)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShareDto>> ShareAsync(Guid historyId, CreateShareCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var entry = await OwnedAsync(historyId, cancellationToken);
        var recipientIds = command.RecipientUserIds.Distinct().Where(id => id != UserId).ToList();
        if (recipientIds.Count == 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "share_recipients_required");
        }

        if (recipientIds.Count > MaxRecipientsPerShare)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "share_too_many_recipients");
        }

        var now = _clock.UtcNow;
        if (command.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "share_expiry_in_past");
        }

        var message = PromptBuilder.Sanitize(command.Message, MaxMessageLength);
        // Tenant filter + explicit status check: a recipient must be an active user of this organization.
        var recipients = await _context.OrganizationUsers.AsNoTracking().Where(u => recipientIds.Contains(u.UserId) && u.Status == OrganizationUserStatus.Active).ToListAsync(cancellationToken);
        if (recipients.Count != recipientIds.Count)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "share_recipient_not_active");
        }

        var existing = await _context.AnalysisShares.Where(s => s.HistoryId == historyId && recipientIds.Contains(s.RecipientUserId) && s.RevokedAt == null).ToListAsync(cancellationToken);
        var created = new List<AnalysisShare>();
        foreach (var recipient in recipients)
        {
            var share = existing.FirstOrDefault(s => s.RecipientUserId == recipient.UserId);
            if (share is null)
            {
                share = new AnalysisShare { ShareId = Ids.New(), OrganizationId = entry.OrganizationId, HistoryId = historyId, OwnerUserId = UserId, RecipientUserId = recipient.UserId, Permission = ShareAccessLevel.View, CreatedAt = now };
                _context.AnalysisShares.Add(share);
                created.Add(share);
            }

            // Sharing again with the same colleague refreshes the terms instead of duplicating the row.
            share.Message = message.Length == 0 ? null : message;
            share.ExpiresAt = command.ExpiresAt;
        }

        var organization = await _context.Organizations.AsNoTracking().Where(o => o.OrganizationId == entry.OrganizationId).Select(o => new { o.OrganizationName, o.OrganizationNameAr }).SingleAsync(cancellationToken);
        var notified = recipients.Select(u => new NotificationRecipient(PrincipalType.OrganizationUser, u.UserId, u.OrganizationId, u.Email, u.FullName, u.PreferredLanguage)).ToList();
        var data = new { historyId, title = entry.Title, actorName = _tenantContext.Principal.DisplayName, message = message.Length == 0 ? null : message, expiresAt = command.ExpiresAt, organizationName = organization.OrganizationName, organizationNameAr = organization.OrganizationNameAr };
        _notifications.Notify(notified, NotificationTypes.AnalysisShared, "AnalysisHistory", historyId.ToString(), data, entry.OrganizationId);
        _audit.Append(AuditEvents.ShareCreated, "AnalysisHistory", historyId.ToString(), new { entry.Title, recipients = recipients.Select(r => r.UserId), command.ExpiresAt, classification = entry.DataClassification.ToString() }, entry.OrganizationId);
        _usage.Append(UsageEventTypes.AnalysisShared, "AnalysisHistory", historyId.ToString(), quantity: recipients.Count, organizationId: entry.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);

        var owner = _tenantContext.Principal.DisplayName;
        var intro = message.Length == 0 ? string.Empty : $"\n\n\"{message}\"";
        await _notifications.EmailAsync(notified, EmailTemplateCodes.AnalysisShared,
            $"{owner} shared an analysis with you: {entry.Title}",
            $"شارك {owner} تحليلًا معك: {entry.Title}",
            $"{owner} shared the analysis \"{entry.Title}\" with you in Wabi Insight.{intro}\n\nOpen it from Shared With Me; the stored results are shown exactly as the owner saw them.",
            $"شارك {owner} التحليل \"{entry.Title}\" معك في وابي إنسايت.{intro}\n\nافتحه من «مشترك معي»؛ تظهر النتائج المحفوظة كما رآها المالك تمامًا.",
            $"app/shared-with-me/{historyId}", entry.OrganizationId, cancellationToken);

        var ids = recipients.Select(r => r.UserId).ToList();
        return await ProjectShares(_context.AnalysisShares.Where(s => s.HistoryId == historyId && ids.Contains(s.RecipientUserId) && s.RevokedAt == null)).ToListAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid historyId, Guid shareId, CancellationToken cancellationToken = default)
    {
        var entry = await OwnedAsync(historyId, cancellationToken);
        var share = await _context.AnalysisShares.SingleOrDefaultAsync(s => s.ShareId == shareId && s.HistoryId == historyId && s.OwnerUserId == UserId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "share_not_found");
        if (share.RevokedAt is not null)
        {
            return;
        }

        share.RevokedAt = _clock.UtcNow;
        var recipient = await _context.OrganizationUsers.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == share.RecipientUserId, cancellationToken);
        if (recipient is not null && recipient.Status == OrganizationUserStatus.Active)
        {
            var organization = await _context.Organizations.AsNoTracking().Where(o => o.OrganizationId == entry.OrganizationId).Select(o => new { o.OrganizationName, o.OrganizationNameAr }).SingleAsync(cancellationToken);
            _notifications.Notify([new NotificationRecipient(PrincipalType.OrganizationUser, recipient.UserId, recipient.OrganizationId, recipient.Email, recipient.FullName, recipient.PreferredLanguage)],
                NotificationTypes.AnalysisShareRevoked, "AnalysisHistory", historyId.ToString(),
                new { historyId, title = entry.Title, actorName = _tenantContext.Principal.DisplayName, organizationName = organization.OrganizationName, organizationNameAr = organization.OrganizationNameAr }, entry.OrganizationId);
        }

        _audit.Append(AuditEvents.ShareRevoked, "AnalysisHistory", historyId.ToString(), new { shareId, share.RecipientUserId, entry.Title }, entry.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<ShareDto>> ListSharedByMeAsync(int page, int pageSize, string? search, bool includeInactive, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var now = _clock.UtcNow;
        var shares = _context.AnalysisShares.Where(s => s.OwnerUserId == UserId && !s.History!.IsDeleted);
        if (!includeInactive)
        {
            shares = shares.Where(s => s.RevokedAt == null && (s.ExpiresAt == null || s.ExpiresAt > now));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            shares = shares.Where(s => s.History!.Title.Contains(term) || _context.OrganizationUsers.Any(u => u.UserId == s.RecipientUserId && (u.FullName.Contains(term) || u.Email.Contains(term))));
        }

        var total = await shares.CountAsync(cancellationToken);
        var items = await ProjectShares(shares.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<ShareDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<SharedAnalysisDto>> ListSharedWithMeAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var shares = Received;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            shares = shares.Where(s => s.History!.Title.Contains(term) || s.History.OriginalPrompt.Contains(term));
        }

        var total = await shares.CountAsync(cancellationToken);
        var items = await ProjectShared(shares.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<SharedAnalysisDto>(items, page, pageSize, total);
    }

    public async Task<SharedAnalysisDetailDto?> GetSharedAsync(Guid historyId, CancellationToken cancellationToken = default)
    {
        var share = await Received.SingleOrDefaultAsync(s => s.HistoryId == historyId, cancellationToken);
        if (share is null)
        {
            return null;
        }

        share.LastViewedAt = _clock.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        var summary = await ProjectShared(_context.AnalysisShares.Where(s => s.ShareId == share.ShareId)).SingleAsync(cancellationToken);
        var entry = await _context.AnalysisHistories.AsNoTracking().SingleAsync(h => h.HistoryId == historyId, cancellationToken);
        var versions = await _context.AnalysisHistoryVersions.AsNoTracking().Where(v => v.HistoryId == historyId && v.Status == AnalysisHistoryStatus.Completed).OrderByDescending(v => v.VersionNumber).ToListAsync(cancellationToken);
        var reportNames = await _context.AnalysisArtifacts.AsNoTracking().Where(a => a.HistoryId == historyId && a.Kind == AnalysisArtifactKind.ReportPdf).ToDictionaryAsync(a => a.VersionId, a => a.FileName, cancellationToken);
        // "View all in Excel" is offered to a recipient who may read this data themselves; nothing is queried to find out.
        var canViewAll = _wholeLists is not null && versions.Count > 0 && await _wholeLists.PermittedAsync(entry.ApplicationId, versions[0].QueryPlanJson, cancellationToken);
        return new SharedAnalysisDetailDto(summary, entry.OriginalPrompt, entry.PromptLanguage,
            // A recipient never sees the sharer's model, tokens or cost (F5).
            versions.Select(v => new HistoryVersionDto(v.VersionId, v.VersionNumber, v.Status, v.FailureStage, v.FailureCode, null, null, null, null, v.DurationMs, v.PanelCount, v.RowCount, v.ResultArtifactId != null, v.ReportArtifactId != null, reportNames.GetValueOrDefault(v.VersionId), v.CreatedAt)).ToList(), canViewAll);
    }

    public async Task<AskResponse?> GetSharedVersionResultAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default)
    {
        var artifact = await SharedArtifactAsync(historyId, versionNumber, AnalysisArtifactKind.ResultJson, cancellationToken);
        if (artifact is null)
        {
            return null;
        }

        await using var stream = await _storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
        return stream is null ? null : (await JsonSerializer.DeserializeAsync<AskResponse>(stream, JsonOptions, cancellationToken))?.WithoutTechnicalFields();
    }

    public async Task<ArtifactDownload?> ExportSharedWholeListAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default)
    {
        var answer = await GetSharedVersionResultAsync(historyId, versionNumber, cancellationToken);
        if (answer is null || _wholeLists is null || _excel is null)
        {
            return null;
        }

        var entry = await _context.AnalysisHistories.AsNoTracking().SingleAsync(h => h.HistoryId == historyId, cancellationToken);
        var version = await _context.AnalysisHistoryVersions.AsNoTracking().SingleAsync(v => v.HistoryId == historyId && v.VersionNumber == versionNumber, cancellationToken);
        // The recipient's own permissions decide, for every answer: one they may not read themselves is not exported.
        if (!await _wholeLists.PermittedAsync(entry.ApplicationId, version.QueryPlanJson, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "export_not_permitted");
        }

        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == entry.OrganizationId, cancellationToken);
        var application = await _context.Applications.AsNoTracking().SingleAsync(a => a.ApplicationId == entry.ApplicationId, cancellationToken);
        var rtl = entry.OutputLanguage == "ar";
        var content = Reports.AnswerReportBuilder.Build(answer, entry.Title, entry.OriginalPrompt, entry.OutputLanguage);
        var workbook = _excel.Export(new Core.Ai.ExportRequest(answer, content, entry.OutputLanguage, rtl ? organization.OrganizationNameAr : organization.OrganizationName,
            rtl ? application.ApplicationNameAr : application.ApplicationName, entry.OriginalPrompt, new Core.Ai.ReportBranding(null, null), _clock.UtcNow));
        var file = await _wholeLists.BuildAsync(workbook, answer, application, version.QueryPlanJson, rtl, cancellationToken);
        try
        {
            if (_accessFacts is not null)
            {
                // Phase 14 ledger: the rows that left the platform as a file, under the recipient's name.
                await _accessFacts.RecordAsync(new Core.Monitoring.AccessFactRecording(entry.ApplicationId,
                    answer.Panels.Select((p, i) => new Core.Monitoring.PlanExecution(p.Plan, (int)Math.Min(int.MaxValue, file.PanelRows[i]))).ToList(),
                    Core.Entities.Monitoring.AccessOutcome.Succeeded, version.VersionId, version.MetricId, IsExport: true, ExportFormat: ExportFormat.Xlsx), cancellationToken);
            }

            _audit.Append(AuditEvents.ShareExported, "AnalysisHistory", historyId.ToString(), new { versionNumber, format = "Xlsx", shared = true, rows = file.PanelRows.Sum() }, entry.OrganizationId);
            await _context.SaveChangesAsync(cancellationToken);
            return new ArtifactDownload(file.Content, $"{Slug(entry.Title)}-v{versionNumber}-{_clock.UtcNow:yyyyMMdd-HHmm}.xlsx", _excel.ContentType);
        }
        catch
        {
            await file.Content.DisposeAsync();
            throw;
        }
    }

    private static string Slug(string title) => Core.Common.FileNames.Slug(title, "analysis");

    public async Task<ArtifactDownload?> OpenSharedVersionReportAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default)
    {
        var artifact = await SharedArtifactAsync(historyId, versionNumber, AnalysisArtifactKind.ReportPdf, cancellationToken);
        if (artifact is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
        return stream is null ? null : new ArtifactDownload(stream, artifact.FileName, artifact.ContentType);
    }

    public async Task<ShareEmailResult> SendEmailAsync(Guid historyId, SendShareEmailCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var entry = await OwnedAsync(historyId, cancellationToken);
        var version = await _context.AnalysisHistoryVersions.AsNoTracking().SingleOrDefaultAsync(v => v.HistoryId == historyId && v.VersionNumber == command.VersionNumber, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "history_version_not_found");
        if (version.Status != AnalysisHistoryStatus.Completed)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "history_version_has_no_result");
        }

        var addresses = NormalizeRecipients(command.Recipients);
        var subject = PromptBuilder.Sanitize(command.Subject, MaxSubjectLength);
        var body = PromptBuilder.SanitizeText(command.Body, MaxBodyLength);
        if (subject.Length == 0 || body.Length == 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "share_email_content_required");
        }

        // Internal = an active user of this organization (tenant filter); everyone else is external.
        var normalized = addresses.Select(EmailNormalizer.Normalize).ToList();
        var internalUsers = await _context.OrganizationUsers.AsNoTracking().Where(u => u.Status == OrganizationUserStatus.Active && normalized.Contains(u.NormalizedEmail)).ToListAsync(cancellationToken);
        var internalEmails = internalUsers.Select(u => u.NormalizedEmail).ToHashSet(StringComparer.Ordinal);
        var externalCount = normalized.Count(e => !internalEmails.Contains(e));
        var now = _clock.UtcNow;
        var evt = new EmailDraftEvent
        {
            EventId = Ids.New(),
            OrganizationId = entry.OrganizationId,
            UserId = UserId,
            HistoryId = historyId,
            VersionId = version.VersionId,
            DataClassification = entry.DataClassification,
            InternalRecipientCount = internalUsers.Count,
            ExternalRecipientCount = externalCount,
            Recipients = string.Join(", ", addresses),
            AttachedReport = command.AttachReport,
            DisclosureApproved = command.DisclosureApproved,
            CreatedAt = now,
        };

        // Review B11: Restricted data never leaves the organization; Confidential needs the owner's explicit, logged approval.
        string? block = null;
        if (entry.DataClassification == DataClassification.Restricted && externalCount > 0)
        {
            block = "share_restricted_external_blocked";
        }
        else if (entry.DataClassification == DataClassification.Confidential && !command.DisclosureApproved)
        {
            block = "share_disclosure_approval_required";
        }

        if (block is not null)
        {
            evt.Kind = EmailDraftEventKind.Blocked;
            evt.BlockReason = block;
            _context.EmailDraftEvents.Add(evt);
            _audit.Append(AuditEvents.ShareEmailBlocked, "AnalysisHistory", historyId.ToString(), new { entry.Title, version.VersionNumber, classification = entry.DataClassification.ToString(), reason = block, recipients = addresses, externalCount }, entry.OrganizationId);
            await _context.SaveChangesAsync(cancellationToken);
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, block);
        }

        // Attachment: the study PDF of the version when it has one, otherwise the answer rendered to PDF (stored in My Reports).
        EmailAttachment? attachment = null;
        if (command.AttachReport)
        {
            var download = await _history.OpenVersionReportAsync(historyId, command.VersionNumber, cancellationToken)
                ?? await _history.ExportAsync(historyId, command.VersionNumber, ExportFormat.Pdf, cancellationToken);
            await using var stream = download.Content;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            attachment = new EmailAttachment(download.FileName, download.ContentType, buffer.ToArray());
        }

        // Internal recipients also get an in-app share so the link in the email opens the stored analysis.
        var inAppShares = 0;
        var existing = await _context.AnalysisShares.Where(s => s.HistoryId == historyId && s.RevokedAt == null).Select(s => s.RecipientUserId).ToListAsync(cancellationToken);
        foreach (var user in internalUsers.Where(u => u.UserId != UserId && !existing.Contains(u.UserId)))
        {
            _context.AnalysisShares.Add(new AnalysisShare { ShareId = Ids.New(), OrganizationId = entry.OrganizationId, HistoryId = historyId, OwnerUserId = UserId, RecipientUserId = user.UserId, Permission = ShareAccessLevel.View, CreatedAt = now });
            inAppShares++;
        }

        evt.Kind = EmailDraftEventKind.Sent;
        _context.EmailDraftEvents.Add(evt);
        _audit.Append(AuditEvents.ShareEmailSent, "AnalysisHistory", historyId.ToString(), new { entry.Title, version.VersionNumber, classification = entry.DataClassification.ToString(), command.DisclosureApproved, recipients = addresses, internalCount = internalUsers.Count, externalCount, attachedReport = attachment is not null, attachment?.FileName }, entry.OrganizationId);
        _usage.Append(UsageEventTypes.AnalysisEmailed, "AnalysisHistory", historyId.ToString(), quantity: addresses.Count, metadata: new { externalCount, attached = attachment is not null }, organizationId: entry.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);

        var owner = _tenantContext.Principal.DisplayName;
        var language = entry.OutputLanguage;
        foreach (var address in addresses)
        {
            var user = internalUsers.FirstOrDefault(u => string.Equals(u.NormalizedEmail, EmailNormalizer.Normalize(address), StringComparison.Ordinal));
            var link = user is null ? null : $"{_urls.BaseUrl.TrimEnd('/')}/app/shared-with-me/{historyId}";
            var message = EmailTemplates.SharedAnalysis(address, user?.FullName ?? address, user?.PreferredLanguage ?? language, subject, body, owner, entry.Title, link, _email.ReplyTo) with { Attachments = attachment is null ? [] : [attachment] };
            await _outbox.EnqueueAsync(message, EmailTemplateCodes.AnalysisEmailed, entry.OrganizationId, cancellationToken);
        }

        return new ShareEmailResult(addresses.Count, internalUsers.Count, externalCount, attachment is not null, attachment?.FileName, inAppShares);
    }

    private static List<string> NormalizeRecipients(IReadOnlyList<string> recipients)
    {
        var addresses = new List<string>();
        foreach (var raw in recipients ?? [])
        {
            var candidate = raw?.Trim() ?? string.Empty;
            if (candidate.Length == 0)
            {
                continue;
            }

            if (candidate.Length > 254 || !MailAddress.TryCreate(candidate, out var parsed) || parsed.Address != candidate || !candidate.Contains('.', StringComparison.Ordinal))
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "share_email_recipient_invalid");
            }

            if (!addresses.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                addresses.Add(candidate);
            }
        }

        if (addresses.Count == 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "share_recipients_required");
        }

        if (addresses.Count > MaxEmailRecipients)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "share_too_many_recipients");
        }

        return addresses;
    }

    private async Task<AnalysisHistory> OwnedAsync(Guid historyId, CancellationToken cancellationToken)
        => await Mine.SingleOrDefaultAsync(h => h.HistoryId == historyId, cancellationToken) ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "history_not_found");

    private async Task<AnalysisArtifact?> SharedArtifactAsync(Guid historyId, int versionNumber, AnalysisArtifactKind kind, CancellationToken cancellationToken)
    {
        if (!await Received.AnyAsync(s => s.HistoryId == historyId, cancellationToken))
        {
            return null;
        }

        var version = await _context.AnalysisHistoryVersions.AsNoTracking().SingleOrDefaultAsync(v => v.HistoryId == historyId && v.VersionNumber == versionNumber && v.Status == AnalysisHistoryStatus.Completed, cancellationToken);
        if (version is null)
        {
            return null;
        }

        return await _context.AnalysisArtifacts.AsNoTracking().SingleOrDefaultAsync(a => a.VersionId == version.VersionId && a.Kind == kind, cancellationToken);
    }

    private IQueryable<ShareDto> ProjectShares(IQueryable<AnalysisShare> shares)
    {
        var now = _clock.UtcNow;
        return shares.Join(_context.OrganizationUsers, s => s.RecipientUserId, u => u.UserId, (s, u) => new { s, u })
            .Select(x => new ShareDto(
                x.s.ShareId,
                x.s.HistoryId,
                x.s.History!.Title,
                x.s.History.AnalysisType,
                x.s.History.DataClassification,
                x.u.UserId,
                x.u.FullName,
                x.u.Email,
                x.s.Permission,
                x.s.Message,
                x.s.RevokedAt != null ? ShareStatus.Revoked : x.s.ExpiresAt != null && x.s.ExpiresAt <= now ? ShareStatus.Expired : ShareStatus.Active,
                x.s.CreatedAt,
                x.s.ExpiresAt,
                x.s.RevokedAt,
                x.s.LastViewedAt));
    }

    private IQueryable<SharedAnalysisDto> ProjectShared(IQueryable<AnalysisShare> shares)
        => shares.Join(_context.OrganizationUsers, s => s.OwnerUserId, u => u.UserId, (s, u) => new { s, u })
            .Join(_context.Applications, x => x.s.History!.ApplicationId, a => a.ApplicationId, (x, a) => new { x.s, x.u, a })
            .Select(x => new SharedAnalysisDto(
                x.s.ShareId,
                x.s.HistoryId,
                x.s.History!.Title,
                x.s.History.OriginalPrompt.Length > 160 ? x.s.History.OriginalPrompt.Substring(0, 160) : x.s.History.OriginalPrompt,
                x.a.ApplicationId,
                x.a.ApplicationName,
                x.a.ApplicationNameAr,
                x.s.History.AnalysisType,
                x.s.History.DataClassification,
                x.s.History.OutputLanguage,
                x.s.History.CurrentVersionNumber,
                _context.AnalysisArtifacts.Any(ar => ar.HistoryId == x.s.HistoryId && ar.Kind == AnalysisArtifactKind.ReportPdf),
                x.u.UserId,
                x.u.FullName,
                x.s.Message,
                x.s.CreatedAt,
                x.s.ExpiresAt));
}
