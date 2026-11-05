using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Permissions;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class RoleTablePermissionConfiguration : IEntityTypeConfiguration<RoleTableGrant>
{
    public void Configure(EntityTypeBuilder<RoleTableGrant> builder)
    {
        builder.ToTable("RoleTablePermissions");
        builder.HasKey(p => p.PermissionId);
        builder.Property(p => p.PermissionId).ValueGeneratedNever();
        builder.HasIndex(p => new { p.RoleId, p.ObjectId }).IsUnique();
        builder.HasOne<Role>().WithMany().HasForeignKey(p => new { p.OrganizationId, p.RoleId }).HasPrincipalKey(r => new { r.OrganizationId, r.RoleId }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DatabaseObject>().WithMany().HasForeignKey(p => new { p.OrganizationId, p.ObjectId }).HasPrincipalKey(o => new { o.OrganizationId, o.ObjectId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.OrganizationId, p.ObjectId });
    }
}

internal sealed class RoleColumnPermissionConfiguration : IEntityTypeConfiguration<RoleColumnGrant>
{
    public void Configure(EntityTypeBuilder<RoleColumnGrant> builder)
    {
        builder.ToTable("RoleColumnPermissions");
        builder.HasKey(p => new { p.RoleId, p.ColumnId });
        builder.HasOne<Role>().WithMany().HasForeignKey(p => new { p.OrganizationId, p.RoleId }).HasPrincipalKey(r => new { r.OrganizationId, r.RoleId }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DatabaseColumn>().WithMany().HasForeignKey(p => new { p.OrganizationId, p.ColumnId }).HasPrincipalKey(c => new { c.OrganizationId, c.ColumnId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.RoleId, p.ObjectId });
    }
}

internal sealed class RoleRowPermissionConfiguration : IEntityTypeConfiguration<RoleRowGrant>
{
    public void Configure(EntityTypeBuilder<RoleRowGrant> builder)
    {
        builder.ToTable("RoleRowPermissions");
        builder.HasKey(p => new { p.RoleId, p.ObjectId });
        builder.HasOne<Role>().WithMany().HasForeignKey(p => new { p.OrganizationId, p.RoleId }).HasPrincipalKey(r => new { r.OrganizationId, r.RoleId }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DatabaseObject>().WithMany().HasForeignKey(p => new { p.OrganizationId, p.ObjectId }).HasPrincipalKey(o => new { o.OrganizationId, o.ObjectId }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_RoleRowPermissions_Scope", "[Scope] IN ('All','Department','Section','Self')"));
    }
}

internal sealed class RowScopeColumnMappingConfiguration : IEntityTypeConfiguration<RowScopeColumnMapping>
{
    public void Configure(EntityTypeBuilder<RowScopeColumnMapping> builder)
    {
        builder.ToTable("RowScopeColumnMappings");
        builder.HasKey(m => m.ObjectId);
        builder.Property(m => m.ObjectId).ValueGeneratedNever();
        builder.HasOne<DatabaseObject>().WithMany().HasForeignKey(m => new { m.OrganizationId, m.ObjectId }).HasPrincipalKey(o => new { o.OrganizationId, o.ObjectId }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DatabaseColumn>().WithMany().HasForeignKey(m => new { m.OrganizationId, m.EmployeeColumnId }).HasPrincipalKey(c => new { c.OrganizationId, c.ColumnId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DatabaseColumn>().WithMany().HasForeignKey(m => new { m.OrganizationId, m.DepartmentColumnId }).HasPrincipalKey(c => new { c.OrganizationId, c.ColumnId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DatabaseColumn>().WithMany().HasForeignKey(m => new { m.OrganizationId, m.SectionColumnId }).HasPrincipalKey(c => new { c.OrganizationId, c.ColumnId }).OnDelete(DeleteBehavior.Restrict);
    }
}
