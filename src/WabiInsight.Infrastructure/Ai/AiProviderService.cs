using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Ai;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Usage;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Tenancy;
using WabiInsight.Core.Usage;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Ai;

/// <summary>Provider configurations (specification §18): platform rows, the allow-list, organization rows and selection. Keys are encrypted and never returned.</summary>
public sealed class AiProviderService : IAiProviderService, IAiProviderResolver
{
    public const string AllowedTypesKey = "Ai.AllowedProviderTypes";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly AiCredentialProtector _protector;
    private readonly IAiProviderFactory _factory;
    private readonly IAuditWriter _audit;
    private readonly IUsageEventWriter _usage;
    private readonly IClock _clock;
    private readonly AiOptions _options;

    public AiProviderService(ConfigurationDbContext context, ITenantContext tenantContext, AiCredentialProtector protector, IAiProviderFactory factory, IAuditWriter audit, IUsageEventWriter usage, IClock clock, IOptions<AiOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _context = context;
        _tenantContext = tenantContext;
        _protector = protector;
        _factory = factory;
        _audit = audit;
        _usage = usage;
        _clock = clock;
        _options = options.Value;
    }

    // ---- Platform ----

    public async Task<IReadOnlyList<AiProviderDto>> ListPlatformAsync(CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var rows = await _context.AiProviderConfigurations.AsNoTracking().Where(p => p.Scope == AiProviderScope.Platform).OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<AiProviderDto> CreatePlatformAsync(SaveAiProviderCommand command, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var entity = new AiProviderConfiguration { ProviderId = Ids.New(), Scope = AiProviderScope.Platform, CreatedAt = _clock.UtcNow };
        Apply(entity, command, allowApproval: true);
        if (!await _context.AiProviderConfigurations.AnyAsync(p => p.Scope == AiProviderScope.Platform && p.IsDefault, cancellationToken))
        {
            entity.IsDefault = entity.IsApproved;
        }

        _context.AiProviderConfigurations.Add(entity);
        _audit.Append(AuditEvents.AiProviderChanged, "AiProvider", entity.ProviderId.ToString(), new { action = "created", entity.Scope, entity.ProviderType, entity.Name, entity.Model, entity.IsApproved });
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<AiProviderDto> UpdatePlatformAsync(Guid providerId, SaveAiProviderCommand command, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var entity = await _context.AiProviderConfigurations.SingleOrDefaultAsync(p => p.ProviderId == providerId && p.Scope == AiProviderScope.Platform, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "ai_provider_not_found");
        Apply(entity, command, allowApproval: true);
        if (entity.IsDefault && (!entity.IsApproved || !entity.IsEnabled))
        {
            entity.IsDefault = false;
        }

        _audit.Append(AuditEvents.AiProviderChanged, "AiProvider", entity.ProviderId.ToString(), new { action = "updated", entity.Scope, entity.ProviderType, entity.Name, entity.Model, entity.IsApproved, entity.IsEnabled });
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task DeletePlatformAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var entity = await _context.AiProviderConfigurations.SingleOrDefaultAsync(p => p.ProviderId == providerId && p.Scope == AiProviderScope.Platform, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "ai_provider_not_found");
        _context.AiProviderConfigurations.Remove(entity);
        _audit.Append(AuditEvents.AiProviderChanged, "AiProvider", entity.ProviderId.ToString(), new { action = "deleted", entity.Name });
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AiProviderDto> SetDefaultAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        RequirePlatformAdministrator();
        var rows = await _context.AiProviderConfigurations.Where(p => p.Scope == AiProviderScope.Platform).ToListAsync(cancellationToken);
        var entity = rows.SingleOrDefault(p => p.ProviderId == providerId) ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "ai_provider_not_found");
        if (!entity.IsApproved || !entity.IsEnabled)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_provider_not_approved");
        }

        // Two statements so the unique filtered index on IsDefault never sees two defaults at once.
        foreach (var row in rows.Where(r => r.IsDefault && r.ProviderId != providerId))
        {
            row.IsDefault = false;
        }

        await _context.SaveChangesAsync(cancellationToken);
        entity.IsDefault = true;
        _audit.Append(AuditEvents.AiProviderChanged, "AiProvider", entity.ProviderId.ToString(), new { action = "default", entity.Name });
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<IReadOnlyList<AiProviderType>> GetAllowedTypesAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _context.PlatformSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == AllowedTypesKey, cancellationToken);
        var names = setting is null ? _options.AllowedProviderTypes : JsonSerializer.Deserialize<string[]>(setting.Value, JsonOptions) ?? [];
        return names.Select(n => Enum.TryParse<AiProviderType>(n, true, out var t) ? t : (AiProviderType?)null).Where(t => t.HasValue).Select(t => t!.Value).Distinct().ToList();
    }

    public async Task<IReadOnlyList<AiProviderType>> SetAllowedTypesAsync(IReadOnlyCollection<AiProviderType> types, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(types);
        RequirePlatformAdministrator();
        var value = JsonSerializer.Serialize(types.Distinct().Select(t => t.ToString()).ToArray(), JsonOptions);
        var setting = await _context.PlatformSettings.SingleOrDefaultAsync(s => s.Key == AllowedTypesKey, cancellationToken);
        if (setting is null)
        {
            _context.PlatformSettings.Add(new PlatformSetting { Key = AllowedTypesKey, Value = value, Description = "Provider types organizations may configure for themselves.", UpdatedAt = _clock.UtcNow, UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = _clock.UtcNow;
            setting.UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId;
        }

        _audit.Append(AuditEvents.AiAllowListChanged, "PlatformSetting", AllowedTypesKey, new { types = types.Select(t => t.ToString()) });
        await _context.SaveChangesAsync(cancellationToken);
        return await GetAllowedTypesAsync(cancellationToken);
    }

    // ---- Organization ----

    public async Task<OrganizationAiSettingsDto> GetOrganizationSettingsAsync(CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationAdministrator();
        var organization = await _context.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        var platform = await _context.AiProviderConfigurations.AsNoTracking().Where(p => p.Scope == AiProviderScope.Platform && p.IsApproved && p.IsEnabled).OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        var own = await _context.AiProviderConfigurations.AsNoTracking().Where(p => p.Scope == AiProviderScope.Organization && p.OrganizationId == organizationId).OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return new OrganizationAiSettingsDto(organization.AiProviderConfigurationId, platform.Select(ToDto).ToList(), own.Select(ToDto).ToList(), await GetAllowedTypesAsync(cancellationToken));
    }

    public async Task<AiProviderDto> CreateOrganizationAsync(SaveAiProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireOrganizationAdministrator();
        await RequireAllowedTypeAsync(command.ProviderType, cancellationToken);
        var entity = new AiProviderConfiguration { ProviderId = Ids.New(), Scope = AiProviderScope.Organization, OrganizationId = organizationId, CreatedAt = _clock.UtcNow };
        Apply(entity, command, allowApproval: false);
        entity.IsApproved = true; // approval of organization rows is the allow-list itself
        _context.AiProviderConfigurations.Add(entity);
        _audit.Append(AuditEvents.AiProviderChanged, "AiProvider", entity.ProviderId.ToString(), new { action = "created", entity.Scope, entity.ProviderType, entity.Name, entity.Model }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<AiProviderDto> UpdateOrganizationAsync(Guid providerId, SaveAiProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var organizationId = RequireOrganizationAdministrator();
        var entity = await _context.AiProviderConfigurations.SingleOrDefaultAsync(p => p.ProviderId == providerId && p.Scope == AiProviderScope.Organization && p.OrganizationId == organizationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "ai_provider_not_found");
        await RequireAllowedTypeAsync(command.ProviderType, cancellationToken);
        Apply(entity, command, allowApproval: false);
        _audit.Append(AuditEvents.AiProviderChanged, "AiProvider", entity.ProviderId.ToString(), new { action = "updated", entity.Scope, entity.ProviderType, entity.Name, entity.Model, entity.IsEnabled }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task DeleteOrganizationAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationAdministrator();
        var entity = await _context.AiProviderConfigurations.SingleOrDefaultAsync(p => p.ProviderId == providerId && p.Scope == AiProviderScope.Organization && p.OrganizationId == organizationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "ai_provider_not_found");
        var organization = await _context.Organizations.SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        if (organization.AiProviderConfigurationId == providerId)
        {
            organization.AiProviderConfigurationId = null;
        }

        _context.AiProviderConfigurations.Remove(entity);
        _audit.Append(AuditEvents.AiProviderChanged, "AiProvider", entity.ProviderId.ToString(), new { action = "deleted", entity.Name }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrganizationAiSettingsDto> SelectForOrganizationAsync(Guid? providerId, CancellationToken cancellationToken = default)
    {
        var organizationId = RequireOrganizationAdministrator();
        var organization = await _context.Organizations.SingleAsync(o => o.OrganizationId == organizationId, cancellationToken);
        if (providerId is { } id)
        {
            var allowed = await GetAllowedTypesAsync(cancellationToken);
            var row = await _context.AiProviderConfigurations.AsNoTracking().SingleOrDefaultAsync(p => p.ProviderId == id && p.IsEnabled && ((p.Scope == AiProviderScope.Platform && p.IsApproved) || (p.Scope == AiProviderScope.Organization && p.OrganizationId == organizationId)), cancellationToken)
                ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "ai_provider_not_found");
            if (row.Scope == AiProviderScope.Organization && !allowed.Contains(row.ProviderType))
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_provider_type_not_allowed");
            }
        }

        organization.AiProviderConfigurationId = providerId;
        organization.UpdatedAt = _clock.UtcNow;
        _audit.Append(AuditEvents.AiProviderSelected, "Organization", organizationId.ToString(), new { providerId }, organizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetOrganizationSettingsAsync(cancellationToken);
    }

    public async Task<AiProviderTestResult> TestAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var entity = _tenantContext.IsPlatformAdministrator
            ? await _context.AiProviderConfigurations.SingleOrDefaultAsync(p => p.ProviderId == providerId && p.Scope == AiProviderScope.Platform, cancellationToken)
            : await _context.AiProviderConfigurations.SingleOrDefaultAsync(p => p.ProviderId == providerId && p.Scope == AiProviderScope.Organization && p.OrganizationId == RequireOrganizationAdministrator(), cancellationToken);
        if (entity is null)
        {
            throw new DomainException(OrganizationOperationOutcome.NotFound, "ai_provider_not_found");
        }

        var watch = Stopwatch.StartNew();
        AiProviderTestResult result;
        try
        {
            var descriptor = Describe(entity);
            var completion = await _factory.Resolve(entity.ProviderType).CompleteAsync(descriptor, new AiRequest("You are a connectivity check. Reply with the single word OK.", "Reply with OK.", null, "none", 16, "low"), cancellationToken);
            result = new AiProviderTestResult(true, completion.Model, completion.Usage.InputTokens, completion.Usage.OutputTokens, watch.Elapsed.TotalMilliseconds, null);
        }
        catch (AiProviderException ex)
        {
            result = new AiProviderTestResult(false, null, 0, 0, watch.Elapsed.TotalMilliseconds, ex.Code);
        }

        entity.LastTestAt = _clock.UtcNow;
        entity.LastTestSucceeded = result.Succeeded;
        entity.LastTestDetail = result.Succeeded ? result.Model : result.Error;
        _usage.Append(UsageEventTypes.AiProviderTested, "AiProvider", entity.ProviderId.ToString(), quantity: result.InputTokens + result.OutputTokens, succeeded: result.Succeeded, metadata: new { entity.ProviderType, entity.Model, result.Error, result.DurationMs }, organizationId: entity.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
        return result;
    }

    // ---- Resolution ----

    public async Task<ResolvedAiProvider> ResolveForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _context.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.OrganizationId == organizationId, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "organization_not_found");
        AiProviderConfiguration? entity = null;
        if (organization.AiProviderConfigurationId is { } selected)
        {
            entity = await _context.AiProviderConfigurations.AsNoTracking().SingleOrDefaultAsync(p => p.ProviderId == selected && p.IsEnabled && p.IsApproved && (p.Scope == AiProviderScope.Platform || p.OrganizationId == organizationId), cancellationToken);
            if (entity is not null && entity.Scope == AiProviderScope.Organization && !(await GetAllowedTypesAsync(cancellationToken)).Contains(entity.ProviderType))
            {
                entity = null; // type removed from the allow-list: no silent fallback to it
            }
        }

        entity ??= await _context.AiProviderConfigurations.AsNoTracking().SingleOrDefaultAsync(p => p.Scope == AiProviderScope.Platform && p.IsDefault && p.IsEnabled && p.IsApproved, cancellationToken)
            ?? throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_provider_not_configured");
        return new ResolvedAiProvider(Describe(entity), _factory.Resolve(entity.ProviderType), entity.Scope, entity.Name);
    }

    private AiProviderDescriptor Describe(AiProviderConfiguration entity)
    {
        if (string.IsNullOrEmpty(entity.EncryptedApiKey))
        {
            throw new AiProviderException("ai_provider_key_missing", "No API key is configured for this provider.");
        }

        return new AiProviderDescriptor(entity.ProviderId, entity.ProviderType, entity.Endpoint, entity.Model, _protector.Unprotect(entity.EncryptedApiKey), entity.MaxOutputTokens, entity.Effort, entity.InputPricePerMillion, entity.OutputPricePerMillion, entity.CachedInputPricePerMillion)
        {
            PublicAddressesOnly = entity.Scope == AiProviderScope.Organization,
        };
    }

    private void Apply(AiProviderConfiguration entity, SaveAiProviderCommand command, bool allowApproval)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Name) || string.IsNullOrWhiteSpace(command.Model))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_provider_invalid");
        }

        if (command.Endpoint is { } endpoint && !string.IsNullOrWhiteSpace(endpoint))
        {
            if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_provider_endpoint_invalid");
            }

            // A Platform Administrator may register a model inside the platform's own network (http on this server,
            // a private address). An organization's provider has to be a public https address: see PublicAddressGuard.
            if (entity.Scope == AiProviderScope.Organization && !PublicAddressGuard.IsAcceptableEndpoint(uri))
            {
                throw new DomainException(OrganizationOperationOutcome.ValidationFailed, PublicAddressGuard.ErrorCode);
            }
        }

        entity.ProviderType = command.ProviderType;
        entity.Name = command.Name.Trim();
        entity.Endpoint = string.IsNullOrWhiteSpace(command.Endpoint) ? null : command.Endpoint.Trim();
        entity.Model = command.Model.Trim();
        if (!string.IsNullOrWhiteSpace(command.ApiKey))
        {
            entity.EncryptedApiKey = _protector.Protect(command.ApiKey.Trim());
            entity.ApiKeyHint = AiCredentialProtector.Hint(command.ApiKey.Trim());
        }

        entity.InputPricePerMillion = command.InputPricePerMillion;
        entity.OutputPricePerMillion = command.OutputPricePerMillion;
        entity.CachedInputPricePerMillion = command.CachedInputPricePerMillion;
        entity.MaxOutputTokens = Math.Clamp(command.MaxOutputTokens, 256, 65536);
        entity.Effort = command.Effort is "low" or "medium" or "high" ? command.Effort : null;
        entity.IsEnabled = command.IsEnabled;
        if (allowApproval)
        {
            entity.IsApproved = command.IsApproved;
        }

        entity.UpdatedAt = _clock.UtcNow;
        entity.UpdatedByAdministratorId = _tenantContext.Principal.AdministratorId;
        entity.UpdatedByUserId = _tenantContext.Principal.UserId;
    }

    private async Task RequireAllowedTypeAsync(AiProviderType type, CancellationToken cancellationToken)
    {
        if (!(await GetAllowedTypesAsync(cancellationToken)).Contains(type))
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "ai_provider_type_not_allowed");
        }
    }

    internal static AiProviderDto ToDto(AiProviderConfiguration p) => new(p.ProviderId, p.Scope, p.OrganizationId, p.ProviderType, p.Name, p.Endpoint, p.Model, !string.IsNullOrEmpty(p.EncryptedApiKey), p.ApiKeyHint, p.InputPricePerMillion, p.OutputPricePerMillion, p.CachedInputPricePerMillion, p.MaxOutputTokens, p.Effort, p.IsEnabled, p.IsApproved, p.IsDefault, p.LastTestAt, p.LastTestSucceeded, p.LastTestDetail, p.UpdatedAt);

    private void RequirePlatformAdministrator()
    {
        if (!_tenantContext.IsPlatformAdministrator)
        {
            throw new TenantContextException("Only platform administrators manage platform AI providers.");
        }
    }

    private Guid RequireOrganizationAdministrator()
    {
        var organizationId = _tenantContext.RequireOrganizationId();
        if (!_tenantContext.Principal.Roles.Contains(SystemRoles.OrganizationAdmin))
        {
            throw new TenantContextException("Only organization administrators manage AI provider selection.");
        }

        return organizationId;
    }
}
