using System.ComponentModel.DataAnnotations;

namespace WabiInsight.Api.Contracts;

public sealed record RenameHistoryRequest([Required, MaxLength(200)] string Title);

public sealed record SetFavoriteRequest(bool IsFavorite);

public sealed record SetArchivedRequest(bool IsArchived);

public sealed record SaveCollectionRequest([Required, MaxLength(120)] string Name, [MaxLength(1000)] string? Description);

public sealed record CollectionItemRequest([Required] Guid HistoryId);

public sealed record CreateShareRequest([Required, MinLength(1), MaxLength(20)] IReadOnlyList<Guid> RecipientUserIds, [MaxLength(1000)] string? Message, DateTimeOffset? ExpiresAt);

public sealed record SendShareEmailRequest([Range(1, int.MaxValue)] int VersionNumber, [Required, MinLength(1), MaxLength(10)] IReadOnlyList<string> Recipients, [Required, MaxLength(200)] string Subject, [Required, MaxLength(8000)] string Body, bool AttachReport = true, bool DisclosureApproved = false);
