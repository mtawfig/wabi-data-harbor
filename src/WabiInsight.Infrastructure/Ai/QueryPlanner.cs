using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Ai;
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

    public QueryPlanner(ConfigurationDbContext context, ITenantContext tenantContext, IPermissionResolver permissions, IAiProviderResolver providers, IUsageEventWriter usage)
    {
        _context = context;
        _tenantContext = tenantContext;
        _permissions = permissions;
        _providers = providers;
        _usage = usage;
    }

    public async Task<MetadataSnapshot> GetSnapshotAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var (snapshot, _, _) = await BuildSnapshotAsync(applicationId, cancellationToken);
        return snapshot;
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

        var language = request.Language == "ar" ? "ar" : "en";
        var userContent = PromptBuilder.BuildUserContent(snapshot, request.Question, language);
        var resolved = await _providers.ResolveForOrganizationAsync(organizationId, cancellationToken);
        var watch = Stopwatch.StartNew();
        AiCompletion completion;
        try
        {
            completion = await resolved.Provider.CompleteAsync(resolved.Descriptor, new AiRequest(PromptBuilder.SystemPrompt, userContent, QueryPlanSchema.Json, QueryPlanSchema.Name, resolved.Descriptor.MaxOutputTokens, resolved.Descriptor.Effort), cancellationToken);
        }
        catch (AiProviderException ex)
        {
            await _usage.AppendAndSaveAsync(UsageEventTypes.AiPlanRejected, "Application", request.ApplicationId.ToString(), succeeded: false, metadata: new { stage = "provider", code = ex.Code, provider = resolved.Descriptor.ProviderType.ToString(), resolved.Descriptor.Model }, cancellationToken: cancellationToken);
            throw;
        }

        var usage = new PlanUsage(resolved.Descriptor.ProviderType.ToString(), completion.Model, completion.Usage.InputTokens, completion.Usage.OutputTokens, completion.Usage.CachedInputTokens, resolved.Descriptor.EstimateCost(completion.Usage), watch.Elapsed.TotalMilliseconds);

        QueryPlan plan;
        try
        {
            plan = Parse(completion.Text);
        }
        catch (JsonException ex)
        {
            await RejectAsync(request, usage, "format", [new PlanError("plan_json_invalid", ex.Message)], cancellationToken);
            throw new PlanRejectedException("format", [new PlanError("plan_json_invalid", "The model did not return a valid plan.")]);
        }

        var shapeErrors = QueryPlanValidator.Validate(plan);
        if (shapeErrors.Count > 0)
        {
            await RejectAsync(request, usage, "shape", shapeErrors, cancellationToken);
            throw new PlanRejectedException("shape", shapeErrors);
        }

        var (authorized, authErrors) = QueryPlanAuthorizer.Authorize(plan, request.ApplicationId, permissions, mappings);
        if (authorized is null)
        {
            await RejectAsync(request, usage, "authorization", authErrors, cancellationToken);
            throw new PlanRejectedException("authorization", authErrors);
        }

        await _usage.AppendAndSaveAsync(UsageEventTypes.AiPlanGenerated, "Application", request.ApplicationId.ToString(), quantity: usage.InputTokens + usage.OutputTokens,
            metadata: new { usage.Provider, usage.Model, usage.InputTokens, usage.OutputTokens, usage.CachedInputTokens, usage.EstimatedCost, usage.DurationMs, objects = authorized.Objects.Count, language }, cancellationToken: cancellationToken);
        return new PlanResponse(plan, authorized, usage);
    }

    /// <summary>Accepts the raw JSON object, or JSON wrapped in a code fence (some OpenAI-compatible servers do that).</summary>
    internal static QueryPlan Parse(string text)
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

        return JsonSerializer.Deserialize<QueryPlan>(trimmed[first..(last + 1)], JsonOptions) ?? throw new JsonException("Empty plan.");
    }

    private async Task RejectAsync(PlanRequest request, PlanUsage usage, string stage, IReadOnlyList<PlanError> errors, CancellationToken cancellationToken)
        => await _usage.AppendAndSaveAsync(UsageEventTypes.AiPlanRejected, "Application", request.ApplicationId.ToString(), quantity: usage.InputTokens + usage.OutputTokens, succeeded: false,
            metadata: new { stage, codes = errors.Select(e => e.Code).Distinct().Take(10), usage.Provider, usage.Model, usage.InputTokens, usage.OutputTokens }, cancellationToken: cancellationToken);

    private async Task<(MetadataSnapshot Snapshot, EffectivePermissions Permissions, Dictionary<Guid, RowMappingColumns> Mappings)> BuildSnapshotAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await _context.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.ApplicationId == applicationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "application_not_found");
        if (application.IsBuiltIn)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_application_unsupported");
        }

        if (!application.IsApproved || !application.IsActive)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_application_not_approved");
        }

        var permissions = await _permissions.ResolveAsync(cancellationToken);
        var objects = permissions.Objects.Where(o => o.ApplicationId == applicationId && o.YieldsRows).ToList();
        var objectIds = objects.Select(o => o.ObjectId).ToList();
        var rows = await _context.DatabaseObjects.AsNoTracking().Where(o => objectIds.Contains(o.ObjectId)).ToDictionaryAsync(o => o.ObjectId, cancellationToken);
        var columnRows = await _context.DatabaseColumns.AsNoTracking().Where(c => objectIds.Contains(c.ObjectId)).ToListAsync(cancellationToken);
        var relationships = await _context.DatabaseRelationships.AsNoTracking().Where(r => objectIds.Contains(r.FromObjectId) && objectIds.Contains(r.ToObjectId)).ToListAsync(cancellationToken);
        var mappingRows = await _context.RowScopeColumnMappings.AsNoTracking().Where(m => objectIds.Contains(m.ObjectId)).ToListAsync(cancellationToken);
        var columnNames = columnRows.ToDictionary(c => c.ColumnId, c => c.ColumnName);

        var snapshotObjects = objects.Select(o =>
        {
            var row = rows[o.ObjectId];
            var permitted = o.Columns.Select(c => c.ColumnId).ToHashSet();
            var columns = columnRows.Where(c => c.ObjectId == o.ObjectId && permitted.Contains(c.ColumnId)).OrderBy(c => c.OrdinalPosition)
                .Select(c => new SnapshotColumn(c.ColumnName, c.DataType, c.DisplayNameEn, c.DisplayNameAr, c.BusinessDescription, c.IsPrimaryKey)).ToList();
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
