using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Insights;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Permissions;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Ai;

/// <summary>
/// Secure workflow steps 1-10 (specification §19). The organization comes from the tenant context, the
/// application must belong to it, the snapshot holds only what the effective permissions allow, the model
/// returns a plan that is validated and authorized before anything else can happen. No SQL is built here.
/// </summary>
public sealed class QueryPlanner : IQueryPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IPermissionResolver _permissions;
    private readonly IAiProviderResolver _providers;
    private readonly IUsageEventWriter _usage;

    private readonly Core.Plans.IAiSpendingGuard? _spending;
    private readonly Core.Analysis.IVerifiedExampleSource? _examples;

    public QueryPlanner(ConfigurationDbContext context, ITenantContext tenantContext, IPermissionResolver permissions, IAiProviderResolver providers, IUsageEventWriter usage, Core.Plans.IAiSpendingGuard? spending = null, Core.Analysis.IVerifiedExampleSource? examples = null)
    {
        _spending = spending;
        _examples = examples;
        _context = context;
        _tenantContext = tenantContext;
        _permissions = permissions;
        _providers = providers;
        _usage = usage;
    }

    /// <summary>
    /// Today in the organization's time zone: what "today", "this month" and the relative dates of a plan mean to the asker
    /// (a Dubai organization is already in the next day when UTC is still in the evening).
    /// </summary>
    private async Task<DateOnly> TodayAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var timeZone = await _context.Organizations.AsNoTracking().Where(o => o.OrganizationId == organizationId).Select(o => o.TimeZone).FirstOrDefaultAsync(cancellationToken);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Core.Monitoring.JobRecurrence.ResolveTimeZone(timeZone)).DateTime);
    }

    public async Task<MetadataSnapshot> GetSnapshotAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var (snapshot, _, _) = await BuildSnapshotAsync(applicationId, cancellationToken);
        return snapshot;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountPermittedObjectsAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await _permissions.ResolveAsync(cancellationToken);
        var counts = permissions.Objects.Where(o => o.YieldsRows).GroupBy(o => o.ApplicationId).ToDictionary(g => g.Key, g => g.Count());
        var builtIn = await _context.Applications.AsNoTracking().Where(a => a.IsBuiltIn && a.IsActive && a.IsApproved).ToListAsync(cancellationToken);
        foreach (var application in builtIn)
        {
            var (snapshot, _, _) = await BuildInsightsSnapshotAsync(application, cancellationToken);
            counts[application.ApplicationId] = snapshot.Objects.Count;
        }

        return counts;
    }

    public async Task<PlanResponse> PlanAsync(PlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationId = _tenantContext.RequireOrganizationId();
        var (snapshot, permissions, mappings) = await BuildSnapshotAsync(request.ApplicationId, cancellationToken);
        if (snapshot.Objects.Count == 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_no_permitted_objects");
        }

        // The plan's monthly AI spending limit (P13.46) is checked before anything is sent to a model.
        if (_spending is not null && await _spending.IsReachedAsync(organizationId, cancellationToken))
        {
            throw new PlanRejectedException("limit", [new PlanError(Core.Plans.AiSpendingState.ReachedCode, "The organization's plan has reached its AI spending limit for this month.")]);
        }

        var language = request.Language == "ar" ? "ar" : "en";
        var today = await TodayAsync(organizationId, cancellationToken);
        // Approved answers of the organization to similar questions (P13.53), only those the asker may run themselves.
        IReadOnlyList<VerifiedExample> examples = _examples is null || snapshot.DatabaseType == "PlatformInsights"
            ? []
            : await _examples.FindAsync(request.ApplicationId, request.Question, permissions, mappings, today, cancellationToken);
        var prompt = PromptBuilder.BuildPrompt(snapshot, request.Question, language, today, examples);
        var resolved = await _providers.ResolveForOrganizationAsync(organizationId, cancellationToken);
        var watch = Stopwatch.StartNew();
        AiCompletion completion;
        try
        {
            completion = await resolved.Provider.CompleteAsync(resolved.Descriptor, new AiRequest(PromptBuilder.SystemPrompt, prompt.Request, AnalysisPlanSchema.Json, AnalysisPlanSchema.Name, Math.Max(resolved.Descriptor.MaxOutputTokens, AnalysisPlanSchema.MinPlanOutputTokens), resolved.Descriptor.Effort, SharedContext: prompt.Catalogue) { Purpose = Core.Entities.Ai.AiCallPurposes.Plan, OrganizationId = organizationId }, cancellationToken);
        }
        catch (AiProviderException ex)
        {
            await _usage.AppendAndSaveAsync(UsageEventTypes.AiPlanRejected, "Application", request.ApplicationId.ToString(), succeeded: false, metadata: new { stage = "provider", code = ex.Code, provider = resolved.Descriptor.ProviderType.ToString(), resolved.Descriptor.Model }, cancellationToken: cancellationToken);
            throw;
        }

        var usage = new PlanUsage(resolved.Descriptor.ProviderType.ToString(), completion.Model, completion.Usage.InputTokens, completion.Usage.OutputTokens, completion.Usage.CachedInputTokens, resolved.Descriptor.EstimateCost(completion.Usage), watch.Elapsed.TotalMilliseconds);

        if (string.Equals(completion.StopReason, "max_tokens", StringComparison.OrdinalIgnoreCase) || string.Equals(completion.StopReason, "length", StringComparison.OrdinalIgnoreCase) || string.Equals(completion.StopReason, "MAX_TOKENS", StringComparison.Ordinal))
        {
            // The plan was cut off by the output budget: say so instead of reporting broken JSON.
            await RejectAsync(request, usage, "format", [new PlanError("plan_output_truncated", completion.StopReason!)], cancellationToken);
            throw new PlanRejectedException("format", [new PlanError("plan_output_truncated", "The plan did not fit in the output budget; ask for fewer panels or raise the provider's max output tokens.")]);
        }

        AnalysisPlan plan;
        try
        {
            plan = Parse(completion.Text);
        }
        catch (JsonException ex)
        {
            await RejectAsync(request, usage, "format", [new PlanError("plan_json_invalid", ex.Message)], cancellationToken);
            throw new PlanRejectedException("format", [new PlanError("plan_json_invalid", "The model did not return a valid plan.")]);
        }

        var shapeErrors = QueryPlanValidator.ValidateAnalysis(plan);
        if (shapeErrors.Count > 0)
        {
            await RejectAsync(request, usage, "shape", shapeErrors, cancellationToken);
            throw new PlanRejectedException("shape", shapeErrors) { Plan = plan };
        }

        // Every query is authorized on its own; one unauthorized reference rejects the whole answer.
        var authorizedPlans = new List<AuthorizedPlan>(plan.Queries.Count);
        var authErrors = new List<PlanError>();
        for (var i = 0; i < plan.Queries.Count; i++)
        {
            var (authorized, errors) = QueryPlanAuthorizer.Authorize(plan.Queries[i], request.ApplicationId, permissions, mappings, today: today);
            authErrors.AddRange(errors.Select(e => new PlanError(e.Code, $"query {i + 1}: {e.Detail}")));
            if (authorized is null)
            {
                continue;
            }

            if (snapshot.DatabaseType == "PlatformInsights")
            {
                // Mandatory organization scope on every insights view (specification §12.1), bound from the principal.
                authorized = authorized with { Objects = authorized.Objects.Select(o => o with { RowPredicates = [new RowPredicate(InsightsSqlBuilder.OrganizationColumn, InsightsSqlBuilder.TenantParameter, organizationId.ToString())] }).ToList() };
            }

            authorizedPlans.Add(authorized);
        }

        if (authorizedPlans.Count != plan.Queries.Count)
        {
            await RejectAsync(request, usage, "authorization", authErrors, cancellationToken);
            throw new PlanRejectedException("authorization", authErrors) { Plan = plan };
        }

        await _usage.AppendAndSaveAsync(UsageEventTypes.AiPlanGenerated, "Application", request.ApplicationId.ToString(), quantity: usage.InputTokens + usage.OutputTokens,
            metadata: new { usage.Provider, usage.Model, usage.InputTokens, usage.OutputTokens, usage.CachedInputTokens, cacheWriteTokens = completion.Usage.CacheWriteTokens, usage.EstimatedCost, usage.DurationMs, queries = plan.Queries.Count, objects = authorizedPlans.Sum(a => a.Objects.Count), language, examples = examples.Count }, cancellationToken: cancellationToken);
        return new PlanResponse(plan, authorizedPlans, usage, HighestClassification(plan, authorizedPlans, permissions));
    }

    public async Task<IReadOnlyList<AuthorizedPlan>> AuthorizeStoredAsync(Guid applicationId, AnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var organizationId = _tenantContext.RequireOrganizationId();
        var shapeErrors = QueryPlanValidator.ValidateAnalysis(plan);
        if (shapeErrors.Count > 0)
        {
            throw new PlanRejectedException("shape", shapeErrors) { Plan = plan };
        }

        var (snapshot, permissions, mappings) = await BuildSnapshotAsync(applicationId, cancellationToken);
        var today = await TodayAsync(_tenantContext.RequireOrganizationId(), cancellationToken);
        var authorizedPlans = new List<AuthorizedPlan>(plan.Queries.Count);
        var errors = new List<PlanError>();
        foreach (var query in plan.Queries)
        {
            var (authorized, queryErrors) = QueryPlanAuthorizer.Authorize(query, applicationId, permissions, mappings, today: today);
            errors.AddRange(queryErrors);
            if (authorized is null)
            {
                continue;
            }

            if (snapshot.DatabaseType == "PlatformInsights")
            {
                authorized = authorized with { Objects = authorized.Objects.Select(o => o with { RowPredicates = [new RowPredicate(InsightsSqlBuilder.OrganizationColumn, InsightsSqlBuilder.TenantParameter, organizationId.ToString())] }).ToList() };
            }

            authorizedPlans.Add(authorized);
        }

        if (authorizedPlans.Count != plan.Queries.Count)
        {
            throw new PlanRejectedException("authorization", errors) { Plan = plan };
        }

        return authorizedPlans;
    }

    /// <summary>The highest classification among the permitted columns the queries reference; stored on the history entry (specification §23).</summary>
    internal static Core.Entities.Metadata.DataClassification HighestClassification(AnalysisPlan plan, IReadOnlyList<AuthorizedPlan> authorized, EffectivePermissions permissions)
    {
        var highest = Core.Entities.Metadata.DataClassification.Internal;
        for (var i = 0; i < plan.Queries.Count && i < authorized.Count; i++)
        {
            var objects = authorized[i].Objects.ToDictionary(o => o.Alias, o => o.ObjectId, StringComparer.OrdinalIgnoreCase);
            foreach (var reference in QueryPlanValidator.ReferencedColumns(plan.Queries[i]))
            {
                if (!objects.TryGetValue(reference.Alias, out var objectId))
                {
                    continue;
                }

                var column = permissions.Objects.FirstOrDefault(o => o.ObjectId == objectId)?.Columns.FirstOrDefault(c => string.Equals(c.ColumnName, reference.Column, StringComparison.OrdinalIgnoreCase));
                if (column is not null && column.Classification > highest)
                {
                    highest = column.Classification;
                }
            }
        }

        return highest;
    }

    /// <summary>
    /// Accepts the raw JSON object, or JSON wrapped in a code fence (some OpenAI-compatible servers do that).
    /// A bare single query plan (an object with "source" and no "queries") is accepted as a one-query answer.
    /// </summary>
    internal static AnalysisPlan Parse(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('\n', StringComparison.Ordinal);
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            trimmed = start > 0 && end > start ? trimmed[(start + 1)..end].Trim() : trimmed;
        }

        var first = trimmed.IndexOf('{', StringComparison.Ordinal);
        var last = trimmed.LastIndexOf('}');
        if (first < 0 || last <= first)
        {
            throw new JsonException("No JSON object in the response.");
        }

        var json = trimmed[first..(last + 1)];
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty("queries", out _) && document.RootElement.TryGetProperty("source", out _))
        {
            var single = JsonSerializer.Deserialize<QueryPlan>(json, JsonOptions) ?? throw new JsonException("Empty plan.");
            single.RemoveNullEntries();
            return new AnalysisPlan { Objective = single.Objective, Assumptions = single.Assumptions, Queries = [single] };
        }

        var plan = JsonSerializer.Deserialize<AnalysisPlan>(json, JsonOptions) ?? throw new JsonException("Empty plan.");
        plan.Queries.RemoveAll(q => q is null);
        foreach (var query in plan.Queries)
        {
            query.RemoveNullEntries();
            query.Title = PromptBuilder.Sanitize(query.Title, QueryPlanValidator.MaxTitleLength + 1);
            if (string.IsNullOrEmpty(query.Objective))
            {
                query.Objective = query.Title;
            }
        }

        return plan;
    }

    private async Task RejectAsync(PlanRequest request, PlanUsage usage, string stage, IReadOnlyList<PlanError> errors, CancellationToken cancellationToken)
        => await _usage.AppendAndSaveAsync(UsageEventTypes.AiPlanRejected, "Application", request.ApplicationId.ToString(), quantity: usage.InputTokens + usage.OutputTokens, succeeded: false,
            metadata: new { stage, codes = errors.Select(e => e.Code).Distinct().Take(10), usage.Provider, usage.Model, usage.InputTokens, usage.OutputTokens }, cancellationToken: cancellationToken);

    /// <summary>
    /// Platform Insights: the approved views the user may address (every approved view for Organization
    /// Administrators, otherwise the views behind the metrics granted to their roles). The organization
    /// predicate is attached as a row predicate on every view, so the compiler applies it at the source level.
    /// </summary>
    private async Task<(MetadataSnapshot Snapshot, EffectivePermissions Permissions, Dictionary<Guid, RowMappingColumns> Mappings)> BuildInsightsSnapshotAsync(Core.Entities.Organizations.Application application, CancellationToken cancellationToken)
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        var userId = _tenantContext.Principal.UserId!.Value;
        var objects = await _context.InsightsObjects.AsNoTracking().Where(o => o.IsApproved && o.IsPresent).OrderBy(o => o.SortOrder).ToListAsync(cancellationToken);
        if (!_tenantContext.Principal.Roles.Contains(Core.Entities.Organizations.SystemRoles.OrganizationAdmin))
        {
            var roleIds = await _context.UserRoles.AsNoTracking().Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync(cancellationToken);
            var activeRoles = await _context.Roles.AsNoTracking().Where(r => roleIds.Contains(r.RoleId) && r.IsActive).Select(r => r.RoleId).ToListAsync(cancellationToken);
            var grantedMetricIds = await _context.RoleMetricGrants.AsNoTracking().Where(g => activeRoles.Contains(g.RoleId)).Select(g => g.InsightsMetricId).ToListAsync(cancellationToken);
            var viewIds = await _context.InsightsMetrics.AsNoTracking().Where(m => m.IsApproved && grantedMetricIds.Contains(m.InsightsMetricId)).Select(m => m.InsightsObjectId).Distinct().ToListAsync(cancellationToken);
            objects = objects.Where(o => viewIds.Contains(o.InsightsObjectId)).ToList();
        }

        var objectIds = objects.Select(o => o.InsightsObjectId).ToList();
        var columns = await _context.InsightsColumns.AsNoTracking().Where(c => objectIds.Contains(c.InsightsObjectId) && c.IsApproved && c.IsPresent).OrderBy(c => c.OrdinalPosition).ToListAsync(cancellationToken);
        var context = new RowScopeContext(null, null, null);
        var access = objects.Select(o => new EffectiveObjectAccess(application.ApplicationId, application.ApplicationCode, o.InsightsObjectId, Insights.InsightsSchema.SchemaName, o.ViewName, Core.Entities.Metadata.DatabaseObjectType.View, o.DisplayNameEn, o.DisplayNameAr,
            columns.Where(c => c.InsightsObjectId == o.InsightsObjectId).Select(c => new EffectiveColumn(c.InsightsColumnId, c.ColumnName, c.DataType, c.DisplayNameEn, c.DisplayNameAr, Core.Entities.Metadata.DataClassification.Internal)).ToList(),
            [Core.Entities.Permissions.RowScope.All], [], true, [])).ToList();
        var permissions = new EffectivePermissions(userId, organizationId, [.. _tenantContext.Principal.Roles], context, access);
        var snapshotObjects = objects.Select(o => new SnapshotObject(Insights.InsightsSchema.SchemaName, o.ViewName, "View", o.DisplayNameEn, o.DisplayNameAr, o.Description,
            columns.Where(c => c.InsightsObjectId == o.InsightsObjectId).Select(c => new SnapshotColumn(c.ColumnName, c.DataType, c.DisplayNameEn, c.DisplayNameAr, c.Description, false)).ToList())).ToList();
        return (new MetadataSnapshot(application.ApplicationCode, "PlatformInsights", snapshotObjects, []), permissions, new Dictionary<Guid, RowMappingColumns>());
    }

    private async Task<(MetadataSnapshot Snapshot, EffectivePermissions Permissions, Dictionary<Guid, RowMappingColumns> Mappings)> BuildSnapshotAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await _context.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.ApplicationId == applicationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "application_not_found");
        if (!application.IsApproved || !application.IsActive)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_application_not_approved");
        }

        if (application.IsBuiltIn)
        {
            return await BuildInsightsSnapshotAsync(application, cancellationToken);
        }

        var permissions = await _permissions.ResolveAsync(cancellationToken);
        var objects = permissions.Objects.Where(o => o.ApplicationId == applicationId && o.YieldsRows).ToList();
        var objectIds = objects.Select(o => o.ObjectId).ToList();
        var rows = await _context.DatabaseObjects.AsNoTracking().Where(o => objectIds.Contains(o.ObjectId)).ToDictionaryAsync(o => o.ObjectId, cancellationToken);
        var columnRows = await _context.DatabaseColumns.AsNoTracking().Where(c => objectIds.Contains(c.ObjectId)).ToListAsync(cancellationToken);
        var relationships = await _context.DatabaseRelationships.AsNoTracking().Where(r => objectIds.Contains(r.FromObjectId) && objectIds.Contains(r.ToObjectId)).ToListAsync(cancellationToken);
        var mappingRows = await _context.RowScopeColumnMappings.AsNoTracking().Where(m => objectIds.Contains(m.ObjectId)).ToListAsync(cancellationToken);
        var columnNames = columnRows.ToDictionary(c => c.ColumnId, c => c.ColumnName);

        // The organization's row-scope columns tell the planner where "my department / section / records" applies.
        var askerKeys = new Dictionary<Guid, string>();
        foreach (var m in mappingRows)
        {
            foreach (var (columnId, key) in new[] { (m.EmployeeColumnId, "employee"), (m.DepartmentColumnId, "department"), (m.SectionColumnId, "section") })
            {
                if (columnId is { } id)
                {
                    askerKeys[id] = key;
                }
            }
        }

        var snapshotObjects = objects.Select(o =>
        {
            var row = rows[o.ObjectId];
            var permitted = o.Columns.Select(c => c.ColumnId).ToHashSet();
            var columns = columnRows.Where(c => c.ObjectId == o.ObjectId && permitted.Contains(c.ColumnId)).OrderBy(c => c.OrdinalPosition)
                .Select(c => new SnapshotColumn(c.ColumnName, c.DataType, c.DisplayNameEn, c.DisplayNameAr, c.BusinessDescription, c.IsPrimaryKey, askerKeys.GetValueOrDefault(c.ColumnId))).ToList();
            return new SnapshotObject(o.SchemaName, o.ObjectName, o.ObjectType.ToString(), o.DisplayNameEn, o.DisplayNameAr, row.BusinessDescription, columns);
        }).ToList();
        var objectNames = objects.ToDictionary(o => o.ObjectId, o => $"{o.SchemaName}.{o.ObjectName}");
        var snapshotRelationships = relationships
            .Where(r => columnNames.ContainsKey(r.FromColumnId) && columnNames.ContainsKey(r.ToColumnId))
            .Select(r => new SnapshotRelationship(objectNames[r.FromObjectId], columnNames[r.FromColumnId], objectNames[r.ToObjectId], columnNames[r.ToColumnId])).ToList();
        var mappings = mappingRows.ToDictionary(m => m.ObjectId, m => new RowMappingColumns(
            m.EmployeeColumnId is { } e ? columnNames.GetValueOrDefault(e) : null,
            m.DepartmentColumnId is { } d ? columnNames.GetValueOrDefault(d) : null,
            m.SectionColumnId is { } s ? columnNames.GetValueOrDefault(s) : null));
        return (new MetadataSnapshot(application.ApplicationCode, application.DatabaseType.ToString(), snapshotObjects, snapshotRelationships), permissions, mappings);
    }
}
