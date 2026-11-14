using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.HasKey(d => d.DepartmentId);
        builder.Property(d => d.DepartmentId).ValueGeneratedNever();
        builder.HasAlternateKey(d => new { d.OrganizationId, d.DepartmentId });
        builder.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(d => d.DepartmentCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(d => new { d.OrganizationId, d.DepartmentCode }).IsUnique();
        builder.Property(d => d.DepartmentNameEn).HasMaxLength(200).IsRequired();
        builder.Property(d => d.DepartmentNameAr).HasMaxLength(200).IsRequired();
        builder.Property(d => d.RowVersion).IsRowVersion();
    }
}

internal sealed class SectionConfiguration : IEntityTypeConfiguration<Section>
{
    public void Configure(EntityTypeBuilder<Section> builder)
    {
        builder.ToTable("Sections");
        builder.HasKey(s => s.SectionId);
        builder.Property(s => s.SectionId).ValueGeneratedNever();
        builder.HasAlternateKey(s => new { s.OrganizationId, s.DepartmentId, s.SectionId });
        builder.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        // A section's department must belong to the same organization.
        builder.HasOne<Department>()
            .WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.DepartmentId })
            .HasPrincipalKey(d => new { d.OrganizationId, d.DepartmentId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(s => s.SectionCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(s => new { s.OrganizationId, s.DepartmentId, s.SectionCode }).IsUnique();
        builder.Property(s => s.SectionNameEn).HasMaxLength(200).IsRequired();
        builder.Property(s => s.SectionNameAr).HasMaxLength(200).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();
    }
}

internal sealed class DefaultRoleTemplateConfiguration : IEntityTypeConfiguration<DefaultRoleTemplate>
{
    public void Configure(EntityTypeBuilder<DefaultRoleTemplate> builder)
    {
        builder.ToTable("DefaultRoleTemplates");
        builder.HasKey(t => t.TemplateId);
        builder.Property(t => t.TemplateId).ValueGeneratedNever();
        builder.Property(t => t.RoleCode).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.RoleCode).IsUnique();
        builder.Property(t => t.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(t => t.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.RowVersion).IsRowVersion();

        // Seed the built-in catalog with stable ids so every environment starts with the same templates.
        var seeded = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            new DefaultRoleTemplate { TemplateId = Guid.Parse("4f6a1a10-0000-7000-8000-000000000001"), RoleCode = SystemRoles.OrganizationAdmin, NameEn = "Organization Administrator", NameAr = "مدير المؤسسة", Description = "Manages the organization's users, structure, roles, applications, metadata and settings.", IsSystem = true, IsActive = true, SortOrder = 1, Version = 1, UpdatedAt = seeded },
            new DefaultRoleTemplate { TemplateId = Guid.Parse("4f6a1a10-0000-7000-8000-000000000002"), RoleCode = SystemRoles.Management, NameEn = "Management", NameAr = "الإدارة", Description = "Read-only access to all approved registered objects of the organization.", IsSystem = true, IsActive = true, SortOrder = 2, Version = 1, UpdatedAt = seeded },
            new DefaultRoleTemplate { TemplateId = Guid.Parse("4f6a1a10-0000-7000-8000-000000000003"), RoleCode = SystemRoles.Analyst, NameEn = "Analyst", NameAr = "محلّل", Description = "Default business-data role; permissions are granted explicitly.", IsSystem = false, IsActive = true, SortOrder = 3, Version = 1, UpdatedAt = seeded });
    }
}

internal sealed class ServicePlanConfiguration : IEntityTypeConfiguration<ServicePlan>
{
    public void Configure(EntityTypeBuilder<ServicePlan> builder)
    {
        builder.ToTable("ServicePlans", t => t.HasCheckConstraint("CK_ServicePlans_Limit", "[MonthlyAiSpendLimit] IS NULL OR [MonthlyAiSpendLimit] > 0"));
        builder.HasKey(p => p.PlanId);
        builder.Property(p => p.PlanId).ValueGeneratedNever();
        builder.Property(p => p.PlanCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(p => p.PlanCode).IsUnique();
        builder.Property(p => p.NameEn).HasMaxLength(100).IsRequired();
        builder.Property(p => p.NameAr).HasMaxLength(100).IsRequired();
        builder.Property(p => p.MonthlyAiSpendLimit).HasPrecision(14, 2);
        builder.Property(p => p.RowVersion).IsRowVersion();

        // Three starting plans with stable ids; the Platform Administrator changes the limits or adds plans.
        // Existing organizations keep no plan (no limit) until one is chosen for them.
        var seeded = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            new ServicePlan { PlanId = Guid.Parse("5a7b2c10-0000-7000-8000-000000000001"), PlanCode = "STARTER", NameEn = "Starter", NameAr = "الأساسية", MonthlyAiSpendLimit = 50m, IsActive = true, SortOrder = 10, UpdatedAt = seeded },
            new ServicePlan { PlanId = Guid.Parse("5a7b2c10-0000-7000-8000-000000000002"), PlanCode = "BUSINESS", NameEn = "Business", NameAr = "الأعمال", MonthlyAiSpendLimit = 250m, IsActive = true, SortOrder = 20, UpdatedAt = seeded },
            new ServicePlan { PlanId = Guid.Parse("5a7b2c10-0000-7000-8000-000000000003"), PlanCode = "ENTERPRISE", NameEn = "Enterprise", NameAr = "المؤسسات", MonthlyAiSpendLimit = 1000m, IsActive = true, SortOrder = 30, UpdatedAt = seeded });
    }
}

internal sealed class AiSpendingNoticeConfiguration : IEntityTypeConfiguration<AiSpendingNotice>
{
    public void Configure(EntityTypeBuilder<AiSpendingNotice> builder)
    {
        builder.ToTable("AiSpendingNotices");
        builder.HasKey(n => n.NoticeId);
        builder.Property(n => n.NoticeId).ValueGeneratedNever();
        builder.Property(n => n.Month).HasMaxLength(7).IsRequired();
        builder.Property(n => n.Threshold).HasMaxLength(16).IsRequired();
        builder.Property(n => n.MonthlyAiSpendLimit).HasPrecision(14, 2);
        builder.Property(n => n.MonthSpend).HasPrecision(14, 2);
        // One alert per threshold per month: the index is what makes two simultaneous requests send it once.
        builder.HasIndex(n => new { n.OrganizationId, n.Month, n.Threshold }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(n => n.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
