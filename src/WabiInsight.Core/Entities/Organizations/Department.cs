using WabiInsight.Core.Common;
using WabiInsight.Core.Tenancy;

namespace WabiInsight.Core.Entities.Organizations;

/// <summary>Organization department (specification §9). Codes are unique per organization.</summary>
public class Department : AuditableEntity, ITenantOwned
{
    public Guid DepartmentId { get; set; }

    public Guid OrganizationId { get; set; }

    public string DepartmentCode { get; set; } = string.Empty;

    public string DepartmentNameEn { get; set; } = string.Empty;

    public string DepartmentNameAr { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

/// <summary>Section inside a department. The composite foreign keys keep sections, departments and users inside one organization.</summary>
public class Section : AuditableEntity, ITenantOwned
{
    public Guid SectionId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid DepartmentId { get; set; }

    public string SectionCode { get; set; } = string.Empty;

    public string SectionNameEn { get; set; } = string.Empty;

    public string SectionNameAr { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
