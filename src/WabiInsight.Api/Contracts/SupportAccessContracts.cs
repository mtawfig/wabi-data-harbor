using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using WabiInsight.Core.Entities.Support;

namespace WabiInsight.Api.Contracts;

public sealed record ProposeGrantRequest(
    [Required, MinLength(1)] IReadOnlyList<SupportOperationEntry> Operations,
    [Required, MaxLength(2000)] string Reason,
    [Range(1, 72)] int? ExpiryHours);

public sealed record GrantDecisionRequest([MaxLength(2000)] string? Note, [Range(1, 72)] int? ExpiryHours = null);

public sealed record ExecuteOperationRequest([Required, MaxLength(64)] string Operation, [MaxLength(64)] string? TargetReference, JsonElement? Payload);
