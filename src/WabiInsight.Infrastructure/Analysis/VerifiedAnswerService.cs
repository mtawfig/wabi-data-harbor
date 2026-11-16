using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Analysis;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Permissions;
using WabiInsight.Core.Storage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Analysis;

/// <summary>
/// Verified answers (P13.53). Feedback is given by the person who asked, on their own saved answer; review is for
/// Organization Administrators (the controller's policy) of the same organization. Every read and write runs under the
/// tenant filter of the signed-in organization user, so Platform Administrators, who have no organization, reach nothing.
/// </summary>
public sealed class VerifiedAnswerService : IVerifiedAnswerService
{
    private const int MaxReasonLength = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IQueryPlanner _planner;
    private readonly IArtifactStorage _storage;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public VerifiedAnswerService(ConfigurationDbContext context, ITenantContext tenantContext, IQueryPlanner planner, IArtifactStorage storage, IAuditWriter audit, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _planner = planner;
        _storage = storage;
        _audit = audit;
        _clock = clock;
    }

    private Guid OrganizationId => _tenantContext.RequireOrganizationId();

    private Guid UserId
    {
        get
        {
            _tenantContext.RequireOrganizationId();
            return _tenantContext.Principal.UserId ?? throw new TenantContextException("Verified answers belong to organization users.");
        }
    }

    public async Task<AnswerFeedbackState> GetFeedbackAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default)
    {
        await OwnVersionAsync(historyId, versionNumber, cancellationToken);
        var latest = await _context.AnswerFeedbacks.AsNoTracking()
            .Where(f => f.HistoryId == historyId && f.VersionNumber == versionNumber && f.UserId == UserId)
            .OrderByDescending(f => f.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return new AnswerFeedbackState(historyId, versionNumber, latest?.Verdict, latest?.Reason, latest?.CreatedAt);
    }

    public async Task<AnswerFeedbackState> SubmitFeedbackAsync(Guid historyId, int versionNumber, SubmitAnswerFeedbackCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var verdict = AnswerVerdicts.All.FirstOrDefault(v => string.Equals(v, command.Verdict?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "feedback_verdict_invalid");
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        if (reason is { Length: > MaxReasonLength })
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "feedback_reason_too_long");
        }

        var (history, version) = await OwnVersionAsync(historyId, versionNumber, cancellationToken);
        if (version.Status != AnalysisHistoryStatus.Completed)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "feedback_version_not_completed");
        }

        var now = _clock.UtcNow;
        _context.AnswerFeedbacks.Add(new AnswerFeedback
        {
            FeedbackId = Ids.New(),
            OrganizationId = OrganizationId,
            HistoryId = historyId,
            VersionNumber = versionNumber,
            UserId = UserId,
            Verdict = verdict,
            Reason = reason,
            CreatedAt = now,
        });

        var entry = await _context.VerifiedAnswers.SingleOrDefaultAsync(a => a.HistoryId == historyId && a.VersionNumber == versionNumber, cancellationToken);
        var waiting = verdict == AnswerVerdicts.Correct ? VerifiedAnswerStatuses.Pending : VerifiedAnswerStatuses.Reported;
        if (entry is null)
        {
            entry = new VerifiedAnswer
            {
                VerifiedAnswerId = Ids.New(),
                OrganizationId = OrganizationId,
                ApplicationId = history.ApplicationId,
                HistoryId = historyId,
                VersionNumber = versionNumber,
                AskedByUserId = history.OwnerUserId,
                Question = history.OriginalPrompt,
                Language = history.PromptLanguage,
                PlanJson = version.QueryPlanJson,
                Terms = string.Join(' ', VerifiedExamples.Terms(history.OriginalPrompt)),
                Status = waiting,
            };
            _context.VerifiedAnswers.Add(entry);
        }
        else if (entry.Status is VerifiedAnswerStatuses.Pending or VerifiedAnswerStatuses.Reported or VerifiedAnswerStatuses.Rejected)
        {
            // Not in use: the new verdict decides what the administrators see. An approved, disabled or set-aside answer keeps
            // its status (one person's click never changes how the platform answers); a later "not correct" is shown to them.
            entry.Status = waiting;
        }

        entry.LatestVerdict = verdict;
        entry.LatestReason = reason;
        entry.LatestFeedbackAt = now;
        _audit.Append(AuditEvents.AnswerFeedbackGiven, "AnalysisHistory", historyId.ToString(), new { versionNumber, verdict, hasReason = reason is not null });
        await _context.SaveChangesAsync(cancellationToken);
        return new AnswerFeedbackState(historyId, versionNumber, verdict, reason, now);
    }

    public async Task<PagedResult<VerifiedAnswerListItem>> ListAsync(VerifiedAnswerListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await SetAsideChangedAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var rows = _context.VerifiedAnswers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (query.Status == "Waiting")
            {
                rows = rows.Where(a => a.Status == VerifiedAnswerStatuses.Pending || a.Status == VerifiedAnswerStatuses.Reported || (a.Status == VerifiedAnswerStatuses.Approved && a.LatestVerdict == AnswerVerdicts.NotCorrect && (a.ReviewedAt == null || a.LatestFeedbackAt > a.ReviewedAt)));
            }
            else
            {
                if (!VerifiedAnswerStatuses.All.Contains(query.Status))
                {
                    throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "verified_answer_status_invalid");
                }

                rows = rows.Where(a => a.Status == query.Status);
            }
        }

        if (query.ApplicationId is { } applicationId)
        {
            rows = rows.Where(a => a.ApplicationId == applicationId);
        }

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows
            .OrderByDescending(a => a.LatestFeedbackAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.VerifiedAnswerId,
                a.ApplicationId,
                ApplicationName = _context.Applications.Where(p => p.ApplicationId == a.ApplicationId).Select(p => p.ApplicationName).FirstOrDefault(),
                ApplicationNameAr = _context.Applications.Where(p => p.ApplicationId == a.ApplicationId).Select(p => p.ApplicationNameAr).FirstOrDefault(),
                a.Question,
                a.Language,
                AskedByName = _context.OrganizationUsers.Where(u => u.UserId == a.AskedByUserId).Select(u => u.FullName).FirstOrDefault(),
                a.Status,
                a.LatestVerdict,
                a.LatestReason,
                a.LatestFeedbackAt,
                a.ReviewedAt,
                a.UseCount,
            })
            .ToListAsync(cancellationToken);
        return new PagedResult<VerifiedAnswerListItem>(
            items.Select(a => new VerifiedAnswerListItem(a.VerifiedAnswerId, a.ApplicationId, a.ApplicationName ?? string.Empty, a.ApplicationNameAr ?? string.Empty, a.Question, a.Language, a.AskedByName ?? string.Empty, a.Status, a.LatestVerdict, a.LatestReason, a.LatestFeedbackAt, a.ReviewedAt, a.UseCount)).ToList(),
            page, pageSize, total);
    }

    public async Task<VerifiedAnswerCounts> CountsAsync(CancellationToken cancellationToken = default)
    {
        _ = OrganizationId;
        var counts = await _context.VerifiedAnswers.AsNoTracking().GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var flagged = await _context.VerifiedAnswers.AsNoTracking().CountAsync(a => a.Status == VerifiedAnswerStatuses.Approved && a.LatestVerdict == AnswerVerdicts.NotCorrect && (a.ReviewedAt == null || a.LatestFeedbackAt > a.ReviewedAt), cancellationToken);
        int Of(string status) => counts.FirstOrDefault(c => c.Key == status)?.Count ?? 0;
        return new VerifiedAnswerCounts(Of(VerifiedAnswerStatuses.Pending), Of(VerifiedAnswerStatuses.Reported) + flagged, Of(VerifiedAnswerStatuses.Approved), Of(VerifiedAnswerStatuses.Rejected), Of(VerifiedAnswerStatuses.Disabled), Of(VerifiedAnswerStatuses.SetAside));
    }

    public async Task<VerifiedAnswerDetail?> GetAsync(Guid verifiedAnswerId, CancellationToken cancellationToken = default)
    {
        _ = OrganizationId;
        var entry = await _context.VerifiedAnswers.AsNoTracking().SingleOrDefaultAsync(a => a.VerifiedAnswerId == verifiedAnswerId, cancellationToken);
        return entry is null ? null : await DetailAsync(entry, cancellationToken);
    }

    public async Task<VerifiedAnswerDetail> ReviewAsync(Guid verifiedAnswerId, ReviewVerifiedAnswerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var action = VerifiedAnswerActions.All.FirstOrDefault(a => string.Equals(a, command.Action?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "verified_answer_action_invalid");
        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        if (note is { Length: > MaxReasonLength })
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "verified_answer_note_too_long");
        }

        var entry = await _context.VerifiedAnswers.SingleOrDefaultAsync(a => a.VerifiedAnswerId == verifiedAnswerId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "verified_answer_not_found");
        var previous = entry.Status;
        string next;
        switch (action)
        {
            case VerifiedAnswerActions.Approve or VerifiedAnswerActions.Enable:
                if (action == VerifiedAnswerActions.Enable && entry.Status is not (VerifiedAnswerStatuses.Disabled or VerifiedAnswerStatuses.SetAside))
                {
                    throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "verified_answer_not_disabled");
                }

                if (entry.Status != VerifiedAnswerStatuses.Approved)
                {
                    var approved = await _context.VerifiedAnswers.CountAsync(a => a.ApplicationId == entry.ApplicationId && a.Status == VerifiedAnswerStatuses.Approved, cancellationToken);
                    if (approved >= VerifiedExamples.MaxApprovedPerApplication)
                    {
                        throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "verified_answer_limit_reached");
                    }
                }

                var plan = VerifiedExamples.TryRead(entry.PlanJson) ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "verified_answer_plan_unreadable");
                var missing = await MissingReferencesAsync(entry.ApplicationId, plan, cancellationToken);
                if (missing.Count > 0)
                {
                    throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "verified_answer_references_missing");
                }

                next = VerifiedAnswerStatuses.Approved;
                entry.SetAsideReason = null;
                break;
            case VerifiedAnswerActions.Reject:
                next = VerifiedAnswerStatuses.Rejected;
                break;
            default:
                if (entry.Status != VerifiedAnswerStatuses.Approved)
                {
                    throw new DomainException(OrganizationOperationOutcome.InvalidStatusTransition, "verified_answer_not_approved");
                }

                next = VerifiedAnswerStatuses.Disabled;
                break;
        }

        entry.Status = next;
        entry.ReviewedByUserId = UserId;
        entry.ReviewedAt = _clock.UtcNow;
        entry.ReviewNote = note;
        _audit.Append(AuditEvents.VerifiedAnswerReviewed, "VerifiedAnswer", entry.VerifiedAnswerId.ToString(), new { action, previousStatus = previous, newStatus = next, entry.ApplicationId, entry.HistoryId, entry.VersionNumber, hasNote = note is not null });
        await _context.SaveChangesAsync(cancellationToken);
        return await DetailAsync(entry, cancellationToken);
    }

    public async Task DeleteAsync(Guid verifiedAnswerId, CancellationToken cancellationToken = default)
    {
        var entry = await _context.VerifiedAnswers.SingleOrDefaultAsync(a => a.VerifiedAnswerId == verifiedAnswerId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "verified_answer_not_found");
        _context.VerifiedAnswers.Remove(entry);
        _audit.Append(AuditEvents.VerifiedAnswerDeleted, "VerifiedAnswer", entry.VerifiedAnswerId.ToString(), new { previousStatus = entry.Status, entry.ApplicationId, entry.HistoryId, entry.VersionNumber });
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<(AnalysisHistory History, AnalysisHistoryVersion Version)> OwnVersionAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken)
    {
        var history = await _context.AnalysisHistories.AsNoTracking().SingleOrDefaultAsync(h => h.HistoryId == historyId && h.OwnerUserId == UserId && !h.IsDeleted, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "history_not_found");
        var version = await _context.AnalysisHistoryVersions.AsNoTracking().SingleOrDefaultAsync(v => v.HistoryId == historyId && v.VersionNumber == versionNumber, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "history_version_not_found");
        return (history, version);
    }

    private async Task<VerifiedAnswerDetail> DetailAsync(VerifiedAnswer entry, CancellationToken cancellationToken)
    {
        var application = await _context.Applications.AsNoTracking().Where(a => a.ApplicationId == entry.ApplicationId).Select(a => new { a.ApplicationName, a.ApplicationNameAr }).FirstOrDefaultAsync(cancellationToken);
        var userIds = new List<Guid> { entry.AskedByUserId };
        if (entry.ReviewedByUserId is { } reviewer)
        {
            userIds.Add(reviewer);
        }

        var feedback = await _context.AnswerFeedbacks.AsNoTracking().Where(f => f.HistoryId == entry.HistoryId && f.VersionNumber == entry.VersionNumber).OrderByDescending(f => f.CreatedAt).Take(50).ToListAsync(cancellationToken);
        userIds.AddRange(feedback.Select(f => f.UserId));
        var names = await _context.OrganizationUsers.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.FullName, cancellationToken);
        var plan = VerifiedExamples.TryRead(entry.PlanJson);
        var stored = await StoredAnswerAsync(entry, cancellationToken);
        var hidden = plan is null || !await MayReadEverythingAsync(entry, plan, cancellationToken);
        var result = hidden ? null : stored?.WithoutTechnicalFields();
        return new VerifiedAnswerDetail(
            entry.VerifiedAnswerId, entry.ApplicationId, application?.ApplicationName ?? string.Empty, application?.ApplicationNameAr ?? string.Empty,
            entry.HistoryId, entry.VersionNumber, entry.Question, entry.Language, names.GetValueOrDefault(entry.AskedByUserId) ?? string.Empty,
            entry.Status, entry.LatestVerdict, entry.LatestReason, entry.LatestFeedbackAt,
            entry.ReviewedByUserId is { } by ? names.GetValueOrDefault(by) : null, entry.ReviewedAt, entry.ReviewNote, entry.SetAsideReason,
            entry.UseCount, entry.LastUsedAt, plan, result, hidden,
            feedback.Select(f => new AnswerFeedbackEntry(f.Verdict, f.Reason, names.GetValueOrDefault(f.UserId) ?? string.Empty, f.CreatedAt)).ToList(),
            // The catalogue's display names (not business data) so "how this was computed" reads in the reviewer's language.
            stored?.Panels.Select(p => p.Labels).ToList());
    }

    /// <summary>
    /// Whether the reviewing administrator may read every table and column the answer uses with no row limit (their own
    /// permissions, as if they asked). Only then is the stored result shown; otherwise the plan explains the computation.
    /// </summary>
    private async Task<bool> MayReadEverythingAsync(VerifiedAnswer entry, AnalysisPlan plan, CancellationToken cancellationToken)
    {
        IReadOnlyList<AuthorizedPlan> authorized;
        try
        {
            authorized = await _planner.AuthorizeStoredAsync(entry.ApplicationId, plan, cancellationToken);
        }
        catch (PlanRejectedException)
        {
            return false;
        }
        catch (DomainException)
        {
            return false;
        }

        return !authorized.Any(a => a.Objects.Any(o => o.RowPredicates.Any(p => p.ParameterName != Core.Insights.InsightsSqlBuilder.TenantParameter)));
    }

    private async Task<AskResponse?> StoredAnswerAsync(VerifiedAnswer entry, CancellationToken cancellationToken)
    {
        var version = await _context.AnalysisHistoryVersions.AsNoTracking().SingleOrDefaultAsync(v => v.HistoryId == entry.HistoryId && v.VersionNumber == entry.VersionNumber, cancellationToken);
        if (version?.ResultArtifactId is not { } artifactId)
        {
            return null;
        }

        var artifact = await _context.AnalysisArtifacts.AsNoTracking().SingleOrDefaultAsync(a => a.ArtifactId == artifactId && a.Kind == AnalysisArtifactKind.ResultJson, cancellationToken);
        if (artifact is null)
        {
            return null;
        }

        await using var stream = await _storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
        return stream is null ? null : await JsonSerializer.DeserializeAsync<AskResponse>(stream, JsonOptions, cancellationToken);
    }

    /// <summary>Approved answers whose tables or columns changed or were removed are set aside, so they are no longer used.</summary>
    private async Task SetAsideChangedAsync(CancellationToken cancellationToken)
    {
        var approved = await _context.VerifiedAnswers.Where(a => a.Status == VerifiedAnswerStatuses.Approved).ToListAsync(cancellationToken);
        var changed = false;
        foreach (var entry in approved)
        {
            var plan = VerifiedExamples.TryRead(entry.PlanJson);
            var missing = plan is null ? ["plan"] : await MissingReferencesAsync(entry.ApplicationId, plan, cancellationToken);
            if (missing.Count == 0)
            {
                continue;
            }

            entry.Status = VerifiedAnswerStatuses.SetAside;
            entry.SetAsideReason = string.Join(", ", missing.Take(10));
            _audit.Append(AuditEvents.VerifiedAnswerSetAside, "VerifiedAnswer", entry.VerifiedAnswerId.ToString(), new { entry.ApplicationId, missing = missing.Take(10) });
            changed = true;
        }

        if (changed)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>The objects and columns the plan uses that are no longer approved and present in the application's catalogue.</summary>
    internal async Task<IReadOnlyList<string>> MissingReferencesAsync(Guid applicationId, AnalysisPlan plan, CancellationToken cancellationToken)
    {
        var builtIn = await _context.Applications.AsNoTracking().Where(a => a.ApplicationId == applicationId).Select(a => a.IsBuiltIn).FirstOrDefaultAsync(cancellationToken);
        if (builtIn)
        {
            return [];
        }

        var catalogue = await CatalogueAsync(_context, applicationId, cancellationToken);
        var missing = new List<string>();
        foreach (var query in plan.Queries)
        {
            var aliases = Aliases(query);
            foreach (var (_, target) in aliases)
            {
                if (!catalogue.Objects.ContainsKey(target))
                {
                    missing.Add(target);
                }
            }

            foreach (var reference in QueryPlanValidator.ReferencedColumns(query))
            {
                if (aliases.TryGetValue(reference.Alias, out var target) && catalogue.Objects.TryGetValue(target, out var objectId) && !catalogue.Columns.ContainsKey((objectId, reference.Column.ToUpperInvariant())))
                {
                    missing.Add($"{target}.{reference.Column}");
                }
            }
        }

        return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static Dictionary<string, string> Aliases(QueryPlan query)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [query.Source.Alias] = $"{query.Source.Schema}.{query.Source.ObjectName}" };
        foreach (var join in query.Joins)
        {
            aliases[join.Alias] = $"{join.Schema}.{join.ObjectName}";
        }

        return aliases;
    }

    internal sealed record Catalogue(Dictionary<string, Guid> Objects, Dictionary<(Guid ObjectId, string Column), DatabaseColumn> Columns);

    /// <summary>The application's approved, present objects (by "schema.name", case-insensitive) and their approved, present columns.</summary>
    internal static async Task<Catalogue> CatalogueAsync(ConfigurationDbContext context, Guid applicationId, CancellationToken cancellationToken)
    {
        var objects = await context.DatabaseObjects.AsNoTracking().Where(o => o.ApplicationId == applicationId && o.IsApproved && o.IsPresent).Select(o => new { o.ObjectId, o.SchemaName, o.ObjectName }).ToListAsync(cancellationToken);
        var ids = objects.Select(o => o.ObjectId).ToList();
        var columns = await context.DatabaseColumns.AsNoTracking().Where(c => ids.Contains(c.ObjectId) && c.IsApproved && c.IsPresent).ToListAsync(cancellationToken);
        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in objects)
        {
            byName[$"{o.SchemaName}.{o.ObjectName}"] = o.ObjectId;
        }

        var byColumn = new Dictionary<(Guid, string), DatabaseColumn>();
        foreach (var c in columns)
        {
            byColumn[(c.ObjectId, c.ColumnName.ToUpperInvariant())] = c;
        }

        return new Catalogue(byName, byColumn);
    }
}

/// <summary>
/// Chooses the approved answers given to the planner as examples (P13.53): same organization (tenant filter) and
/// application, the most similar questions by shared words, and only those whose every query the asker may run under
/// their own permissions. Values that could identify a person, an account or a specific record are removed.
/// </summary>
public sealed class VerifiedExampleSource : IVerifiedExampleSource
{
    private readonly ConfigurationDbContext _context;
    private readonly IClock _clock;

    public VerifiedExampleSource(ConfigurationDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<IReadOnlyList<VerifiedExample>> FindAsync(Guid applicationId, string question, EffectivePermissions permissions, IReadOnlyDictionary<Guid, RowMappingColumns> mappings, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var terms = VerifiedExamples.Terms(question);
        if (terms.Count == 0)
        {
            return [];
        }

        var candidates = await _context.VerifiedAnswers.AsNoTracking()
            .Where(a => a.ApplicationId == applicationId && a.Status == VerifiedAnswerStatuses.Approved)
            .OrderByDescending(a => a.ReviewedAt)
            .Take(VerifiedExamples.MaxApprovedPerApplication)
            .Select(a => new { a.VerifiedAnswerId, a.Question, a.PlanJson, a.Terms })
            .ToListAsync(cancellationToken);
        var ranked = candidates
            .Select(c => (Candidate: c, Score: VerifiedExamples.Similarity(terms, c.Terms.Split(' ', StringSplitOptions.RemoveEmptyEntries))))
            .Where(c => c.Score >= VerifiedExamples.MinimumSimilarity)
            .OrderByDescending(c => c.Score)
            .ToList();
        if (ranked.Count == 0)
        {
            return [];
        }

        var catalogue = await VerifiedAnswerService.CatalogueAsync(_context, applicationId, cancellationToken);
        var sensitiveTargets = await SensitiveTargetsAsync(catalogue, cancellationToken);
        var employeeColumns = await _context.RowScopeColumnMappings.AsNoTracking().Where(m => m.EmployeeColumnId != null).Select(m => m.EmployeeColumnId!.Value).ToListAsync(cancellationToken);
        var employee = employeeColumns.ToHashSet();

        var examples = new List<VerifiedExample>();
        var used = new List<Guid>();
        foreach (var (candidate, _) in ranked)
        {
            var plan = VerifiedExamples.TryRead(candidate.PlanJson);
            if (plan is null || plan.Queries.Any(q => QueryPlanAuthorizer.Authorize(q, applicationId, permissions, mappings, today: today).Errors.Count > 0))
            {
                continue; // The asker may not read everything this example uses: it is not shown to the model for them.
            }

            var (clean, removed) = VerifiedExamples.RemoveSpecificValues(plan, (schema, name, column) => KeepValue(catalogue, sensitiveTargets, employee, schema, name, column));
            if (VerifiedExamples.Serialize(clean).Length > VerifiedExamples.MaxExamplePlanLength)
            {
                continue;
            }

            examples.Add(new VerifiedExample(VerifiedExamples.RemoveValuesFromQuestion(candidate.Question, removed), clean));
            used.Add(candidate.VerifiedAnswerId);
            if (examples.Count == VerifiedExamples.MaxExamplesPerQuestion)
            {
                break;
            }
        }

        if (used.Count > 0)
        {
            var now = _clock.UtcNow;
            await _context.VerifiedAnswers.Where(a => used.Contains(a.VerifiedAnswerId))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.UseCount, a => a.UseCount + 1).SetProperty(a => a.LastUsedAt, now), cancellationToken);
        }

        return examples;
    }

    /// <summary>
    /// A filter value stays in an example only on a column that cannot point at a person, an account or one record: an
    /// internal column that is not a key, an identifier, contact data, the employee row-scope column, or a reference to an
    /// object holding confidential, restricted or personal data. Anything unknown is removed.
    /// </summary>
    private static bool KeepValue(VerifiedAnswerService.Catalogue catalogue, HashSet<Guid> sensitiveTargets, HashSet<Guid> employeeColumns, string schema, string name, string column)
    {
        if (!catalogue.Objects.TryGetValue($"{schema}.{name}", out var objectId) || !catalogue.Columns.TryGetValue((objectId, column.ToUpperInvariant()), out var row))
        {
            return false;
        }

        return row.Classification == DataClassification.Internal
            && !row.IsIdentifierLike
            && row.SemanticRole != ColumnSemanticRole.Contact
            && !employeeColumns.Contains(row.ColumnId)
            && !sensitiveTargets.Contains(row.ColumnId);
    }

    /// <summary>Columns that reference (by a relationship) an object holding confidential, restricted or personal data.</summary>
    private async Task<HashSet<Guid>> SensitiveTargetsAsync(VerifiedAnswerService.Catalogue catalogue, CancellationToken cancellationToken)
    {
        var objectIds = catalogue.Objects.Values.ToList();
        var relationships = await _context.DatabaseRelationships.AsNoTracking().Where(r => r.IsPresent && objectIds.Contains(r.FromObjectId)).Select(r => new { r.FromColumnId, r.ToObjectId }).ToListAsync(cancellationToken);
        var targetIds = relationships.Select(r => r.ToObjectId).Distinct().ToList();
        var sensitiveObjects = (await _context.DatabaseColumns.AsNoTracking()
            .Where(c => targetIds.Contains(c.ObjectId) && (c.Classification != DataClassification.Internal || c.SemanticRole == ColumnSemanticRole.PersonIdentifier || c.SemanticRole == ColumnSemanticRole.AccountIdentifier || c.SemanticRole == ColumnSemanticRole.Contact))
            .Select(c => c.ObjectId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        return relationships.Where(r => sensitiveObjects.Contains(r.ToObjectId)).Select(r => r.FromColumnId).ToHashSet();
    }
}
