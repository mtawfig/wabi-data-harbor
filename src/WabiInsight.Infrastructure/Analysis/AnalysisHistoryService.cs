using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WabiInsight.Core.Ai;
using WabiInsight.Core.Analysis;
using WabiInsight.Core.Audit;
using WabiInsight.Core.Common;
using WabiInsight.Core.Entities.Analysis;
using WabiInsight.Core.Organizations;
using WabiInsight.Core.Storage;
using WabiInsight.Core.Tenancy;
using WabiInsight.Infrastructure.Persistence;

namespace WabiInsight.Infrastructure.Analysis;

/// <summary>
/// Persistent history (specification §23). Every query runs under the tenant filter and an explicit
/// owner check; stored results are read back from artifact storage, never recomputed; re-runs go through
/// the full secure workflow and become new versions.
/// </summary>
public sealed class AnalysisHistoryService : IAnalysisHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxTitleLength = 200;

    private readonly ConfigurationDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IAnalysisService _analysis;
    private readonly IArtifactStorage _storage;
    private readonly IAuditWriter _audit;
    private readonly IClock _clock;

    public AnalysisHistoryService(ConfigurationDbContext context, ITenantContext tenantContext, IAnalysisService analysis, IArtifactStorage storage, IAuditWriter audit, IClock clock)
    {
        _context = context;
        _tenantContext = tenantContext;
        _analysis = analysis;
        _storage = storage;
        _audit = audit;
        _clock = clock;
    }

    private Guid UserId
    {
        get
        {
            _tenantContext.RequireOrganizationId();
            return _tenantContext.Principal.UserId ?? throw new TenantContextException("History belongs to organization users.");
        }
    }

    private IQueryable<AnalysisHistory> Mine => _context.AnalysisHistories.Where(h => h.OwnerUserId == UserId && !h.IsDeleted);

    public async Task<PagedResult<HistorySummaryDto>> ListAsync(HistoryListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var histories = Mine.Where(h => h.IsArchived == query.Archived);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            histories = histories.Where(h => h.Title.Contains(term) || h.OriginalPrompt.Contains(term));
        }

        if (query.ApplicationId is { } applicationId)
        {
            histories = histories.Where(h => h.ApplicationId == applicationId);
        }

        if (query.From is { } from)
        {
            histories = histories.Where(h => h.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            histories = histories.Where(h => h.CreatedAt < to);
        }

        if (query.Favorite is { } favorite)
        {
            histories = histories.Where(h => h.IsFavorite == favorite);
        }

        if (query.AnalysisType is { } type)
        {
            histories = histories.Where(h => h.AnalysisType == type);
        }

        if (query.CollectionId is { } collectionId)
        {
            var owned = await _context.AnalysisCollections.AnyAsync(c => c.CollectionId == collectionId && c.OwnerUserId == UserId, cancellationToken);
            if (!owned)
            {
                throw new DomainException(OrganizationOperationOutcome.NotFound, "collection_not_found");
            }

            histories = histories.Where(h => _context.AnalysisCollectionItems.Any(i => i.CollectionId == collectionId && i.HistoryId == h.HistoryId));
        }

        var total = await histories.CountAsync(cancellationToken);
        var items = await Project(histories.OrderByDescending(h => h.UpdatedAt ?? h.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(cancellationToken);
        return new PagedResult<HistorySummaryDto>(items, page, pageSize, total);
    }

    public async Task<HistoryDetailDto?> GetAsync(Guid historyId, CancellationToken cancellationToken = default)
    {
        var summary = await Project(Mine.Where(h => h.HistoryId == historyId)).SingleOrDefaultAsync(cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var entry = await Mine.AsNoTracking().SingleAsync(h => h.HistoryId == historyId, cancellationToken);
        var versions = await _context.AnalysisHistoryVersions.AsNoTracking().Where(v => v.HistoryId == historyId).OrderByDescending(v => v.VersionNumber).ToListAsync(cancellationToken);
        var reportNames = await _context.AnalysisArtifacts.AsNoTracking().Where(a => a.HistoryId == historyId && a.Kind == AnalysisArtifactKind.ReportPdf).ToDictionaryAsync(a => a.VersionId, a => a.FileName, cancellationToken);
        var collections = await _context.AnalysisCollections.AsNoTracking()
            .Where(c => c.OwnerUserId == UserId && c.Items.Any(i => i.HistoryId == historyId))
            .Select(c => new CollectionSummaryDto(c.CollectionId, c.Name, c.Description, c.Items.Count, c.CreatedAt, c.UpdatedAt)).ToListAsync(cancellationToken);
        return new HistoryDetailDto(summary, entry.OriginalPrompt, entry.PromptLanguage,
            versions.Select(v => new HistoryVersionDto(v.VersionId, v.VersionNumber, v.Status, v.FailureStage, v.FailureCode, v.ModelName, v.InputTokens, v.OutputTokens, v.EstimatedCost, v.DurationMs, v.PanelCount, v.RowCount, v.ResultArtifactId != null, v.ReportArtifactId != null, reportNames.GetValueOrDefault(v.VersionId), v.CreatedAt)).ToList(),
            collections);
    }

    public async Task<AskResponse?> GetVersionResultAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default)
    {
        var artifact = await ArtifactAsync(historyId, versionNumber, AnalysisArtifactKind.ResultJson, cancellationToken);
        if (artifact is null)
        {
            return null;
        }

        await using var stream = await _storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        return await JsonSerializer.DeserializeAsync<AskResponse>(stream, JsonOptions, cancellationToken);
    }

    public async Task<ArtifactDownload?> OpenVersionReportAsync(Guid historyId, int versionNumber, CancellationToken cancellationToken = default)
    {
        var artifact = await ArtifactAsync(historyId, versionNumber, AnalysisArtifactKind.ReportPdf, cancellationToken);
        if (artifact is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(artifact.StorageKey, cancellationToken);
        return stream is null ? null : new ArtifactDownload(stream, artifact.FileName, artifact.ContentType);
    }

    public async Task<HistorySummaryDto> RenameAsync(Guid historyId, string title, CancellationToken cancellationToken = default)
    {
        var clean = PromptBuilder.Sanitize(title, MaxTitleLength);
        if (clean.Length == 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "history_title_required");
        }

        var entry = await OwnedAsync(historyId, cancellationToken);
        entry.Title = clean;
        entry.UpdatedAt = _clock.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(historyId, cancellationToken);
    }

    public async Task<HistorySummaryDto> SetFavoriteAsync(Guid historyId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        var entry = await OwnedAsync(historyId, cancellationToken);
        entry.IsFavorite = isFavorite;
        await _context.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(historyId, cancellationToken);
    }

    public async Task<HistorySummaryDto> SetArchivedAsync(Guid historyId, bool isArchived, CancellationToken cancellationToken = default)
    {
        var entry = await OwnedAsync(historyId, cancellationToken);
        entry.IsArchived = isArchived;
        entry.UpdatedAt = _clock.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return await SummaryAsync(historyId, cancellationToken);
    }

    public async Task DeleteAsync(Guid historyId, CancellationToken cancellationToken = default)
    {
        // Soft delete: artifacts and versions stay for audit; the entry disappears from every list and lookup.
        var entry = await OwnedAsync(historyId, cancellationToken);
        entry.IsDeleted = true;
        entry.DeletedAt = _clock.UtcNow;
        entry.UpdatedAt = _clock.UtcNow;
        _audit.Append(AuditEvents.HistoryDeleted, "AnalysisHistory", historyId.ToString(), new { entry.Title, entry.CurrentVersionNumber }, entry.OrganizationId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AskResponse> RerunAsync(Guid historyId, CancellationToken cancellationToken = default)
    {
        var entry = await OwnedAsync(historyId, cancellationToken);
        _context.Entry(entry).State = EntityState.Detached;
        return await _analysis.AskAsync(new AskRequest(entry.ApplicationId, entry.OriginalPrompt, entry.OutputLanguage, true, historyId), cancellationToken);
    }

    public async Task<IReadOnlyList<CollectionSummaryDto>> ListCollectionsAsync(CancellationToken cancellationToken = default)
        => await _context.AnalysisCollections.AsNoTracking().Where(c => c.OwnerUserId == UserId).OrderBy(c => c.Name)
            .Select(c => new CollectionSummaryDto(c.CollectionId, c.Name, c.Description, c.Items.Count(i => !i.History!.IsDeleted), c.CreatedAt, c.UpdatedAt)).ToListAsync(cancellationToken);

    public async Task<CollectionSummaryDto> CreateCollectionAsync(SaveCollectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (name, description) = ValidateCollection(command);
        if (await _context.AnalysisCollections.AnyAsync(c => c.OwnerUserId == UserId && c.Name == name, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "collection_name_exists");
        }

        var collection = new AnalysisCollectionRecord { CollectionId = Ids.New(), OrganizationId = _tenantContext.RequireOrganizationId(), OwnerUserId = UserId, Name = name, Description = description, CreatedAt = _clock.UtcNow };
        _context.AnalysisCollections.Add(collection);
        await _context.SaveChangesAsync(cancellationToken);
        return new CollectionSummaryDto(collection.CollectionId, collection.Name, collection.Description, 0, collection.CreatedAt, collection.UpdatedAt);
    }

    public async Task<CollectionSummaryDto> UpdateCollectionAsync(Guid collectionId, SaveCollectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (name, description) = ValidateCollection(command);
        var collection = await OwnedCollectionAsync(collectionId, cancellationToken);
        if (await _context.AnalysisCollections.AnyAsync(c => c.OwnerUserId == UserId && c.Name == name && c.CollectionId != collectionId, cancellationToken))
        {
            throw new DomainException(OrganizationOperationOutcome.CodeAlreadyExists, "collection_name_exists");
        }

        collection.Name = name;
        collection.Description = description;
        collection.UpdatedAt = _clock.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        var count = await _context.AnalysisCollectionItems.CountAsync(i => i.CollectionId == collectionId, cancellationToken);
        return new CollectionSummaryDto(collection.CollectionId, collection.Name, collection.Description, count, collection.CreatedAt, collection.UpdatedAt);
    }

    public async Task DeleteCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var collection = await OwnedCollectionAsync(collectionId, cancellationToken);
        _context.AnalysisCollections.Remove(collection);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddToCollectionAsync(Guid collectionId, Guid historyId, CancellationToken cancellationToken = default)
    {
        var collection = await OwnedCollectionAsync(collectionId, cancellationToken);
        var entry = await OwnedAsync(historyId, cancellationToken);
        if (!await _context.AnalysisCollectionItems.AnyAsync(i => i.CollectionId == collectionId && i.HistoryId == historyId, cancellationToken))
        {
            _context.AnalysisCollectionItems.Add(new AnalysisCollectionItem { CollectionId = collectionId, HistoryId = entry.HistoryId, OrganizationId = collection.OrganizationId, AddedAt = _clock.UtcNow });
            collection.UpdatedAt = _clock.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveFromCollectionAsync(Guid collectionId, Guid historyId, CancellationToken cancellationToken = default)
    {
        var collection = await OwnedCollectionAsync(collectionId, cancellationToken);
        var item = await _context.AnalysisCollectionItems.SingleOrDefaultAsync(i => i.CollectionId == collectionId && i.HistoryId == historyId, cancellationToken);
        if (item is not null)
        {
            _context.AnalysisCollectionItems.Remove(item);
            collection.UpdatedAt = _clock.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static (string Name, string? Description) ValidateCollection(SaveCollectionCommand command)
    {
        var name = PromptBuilder.Sanitize(command.Name, 120);
        if (name.Length == 0)
        {
            throw new DomainException(OrganizationOperationOutcome.ValidationFailed, "collection_name_required");
        }

        var description = PromptBuilder.Sanitize(command.Description, 1000);
        return (name, description.Length == 0 ? null : description);
    }

    private async Task<AnalysisHistory> OwnedAsync(Guid historyId, CancellationToken cancellationToken)
        => await Mine.SingleOrDefaultAsync(h => h.HistoryId == historyId, cancellationToken) ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "history_not_found");

    private async Task<AnalysisCollectionRecord> OwnedCollectionAsync(Guid collectionId, CancellationToken cancellationToken)
        => await _context.AnalysisCollections.SingleOrDefaultAsync(c => c.CollectionId == collectionId && c.OwnerUserId == UserId, cancellationToken) ?? throw new DomainException(OrganizationOperationOutcome.NotFound, "collection_not_found");

    private async Task<AnalysisArtifact?> ArtifactAsync(Guid historyId, int versionNumber, AnalysisArtifactKind kind, CancellationToken cancellationToken)
    {
        if (!await Mine.AnyAsync(h => h.HistoryId == historyId, cancellationToken))
        {
            return null;
        }

        var version = await _context.AnalysisHistoryVersions.AsNoTracking().SingleOrDefaultAsync(v => v.HistoryId == historyId && v.VersionNumber == versionNumber, cancellationToken);
        if (version is null)
        {
            return null;
        }

        return await _context.AnalysisArtifacts.AsNoTracking().SingleOrDefaultAsync(a => a.VersionId == version.VersionId && a.Kind == kind, cancellationToken);
    }

    private async Task<HistorySummaryDto> SummaryAsync(Guid historyId, CancellationToken cancellationToken)
        => await Project(Mine.Where(h => h.HistoryId == historyId)).SingleAsync(cancellationToken);

    private IQueryable<HistorySummaryDto> Project(IQueryable<AnalysisHistory> histories)
        => histories.Join(_context.Applications, h => h.ApplicationId, a => a.ApplicationId, (h, a) => new { h, a })
            .Select(x => new HistorySummaryDto(
                x.h.HistoryId,
                x.h.Title,
                x.h.OriginalPrompt.Length > 160 ? x.h.OriginalPrompt.Substring(0, 160) : x.h.OriginalPrompt,
                x.h.ApplicationId,
                x.a.ApplicationCode,
                x.a.ApplicationName,
                x.a.ApplicationNameAr,
                x.h.AnalysisType,
                x.h.Status,
                x.h.DataClassification,
                x.h.OutputLanguage,
                x.h.CurrentVersionNumber,
                x.h.IsFavorite,
                x.h.IsArchived,
                _context.AnalysisArtifacts.Any(ar => ar.HistoryId == x.h.HistoryId && ar.Kind == AnalysisArtifactKind.ReportPdf),
                _context.AnalysisHistoryVersions.Where(v => v.HistoryId == x.h.HistoryId).Sum(v => v.EstimatedCost),
                x.h.CreatedAt,
                x.h.UpdatedAt));
}
