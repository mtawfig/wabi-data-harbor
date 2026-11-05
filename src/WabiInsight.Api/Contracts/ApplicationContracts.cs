using System.ComponentModel.DataAnnotations;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;

namespace WabiInsight.Api.Contracts;

public sealed record CreateApplicationRequest(
    [Required, MaxLength(64)] string ApplicationCode,
    [Required, MaxLength(200)] string ApplicationName,
    [Required, MaxLength(200)] string ApplicationNameAr,
    [MaxLength(1000)] string? Description,
    [Required] DatabaseType DatabaseType,
    [MaxLength(4000)] string? ConnectionString,
    [MaxLength(300)] string? ConnectionHint);

public sealed record UpdateApplicationRequest([Required, MaxLength(200)] string ApplicationName, [Required, MaxLength(200)] string ApplicationNameAr, [MaxLength(1000)] string? Description, bool IsActive = true);

public sealed record ReplaceConnectionRequest([Required, MaxLength(4000)] string ConnectionString, [MaxLength(300)] string? ConnectionHint);

public sealed record TestConnectionRequest([Required] DatabaseType DatabaseType, [Required, MaxLength(4000)] string ConnectionString);

public sealed record SetApplicationApprovalRequest(bool IsApproved);

public sealed record UpdateObjectRequest([MaxLength(200)] string? DisplayNameEn, [MaxLength(200)] string? DisplayNameAr, [MaxLength(2000)] string? BusinessDescription, bool IsApproved);

public sealed record UpdateColumnRequest([MaxLength(200)] string? DisplayNameEn, [MaxLength(200)] string? DisplayNameAr, [MaxLength(2000)] string? BusinessDescription, DataClassification Classification, bool IsApproved);

public sealed record ApproveObjectRequest(bool Approve);
