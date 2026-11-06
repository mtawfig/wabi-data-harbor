using System.ComponentModel.DataAnnotations;

namespace WabiInsight.Api.Contracts;

public sealed record RenameHistoryRequest([Required, MaxLength(200)] string Title);

public sealed record SetFavoriteRequest(bool IsFavorite);

public sealed record SetArchivedRequest(bool IsArchived);

public sealed record SaveCollectionRequest([Required, MaxLength(120)] string Name, [MaxLength(1000)] string? Description);

public sealed record CollectionItemRequest([Required] Guid HistoryId);
