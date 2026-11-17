using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.OrganizationId);
        builder.Property(o => o.OrganizationId).ValueGeneratedNever();

        builder.Property(o => o.OrganizationCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(o => o.OrganizationCode).IsUnique();

        builder.Property(o => o.OrganizationName).HasMaxLength(200).IsRequired();
        builder.Property(o => o.OrganizationNameAr).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Description).HasMaxLength(2000);
        builder.Property(o => o.ContactEmail).HasMaxLength(256);
        builder.Property(o => o.ContactPhone).HasMaxLength(50);
        builder.Property(o => o.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(o => o.TimeZone).HasMaxLength(64).IsRequired();
        // Existing organizations get 07:00 / 08:00–18:00 / Monday–Friday from the migration's column defaults.
        builder.Property(o => o.WorkingDays).HasMaxLength(7).IsRequired();
        // Existing organizations get clarifying questions too (P13.54): the column is added with the default on.
        builder.Property(o => o.AskClarifyingQuestions).HasDefaultValue(true);
        builder.Property(o => o.MonthlyAiBudget).HasPrecision(14, 2);
        builder.HasOne<ServicePlan>().WithMany().HasForeignKey(o => o.ServicePlanId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(o => o.DefaultLanguage).HasMaxLength(2).IsRequired();
        builder.Property(o => o.LogoArtifactKey).HasMaxLength(260);
        builder.Property(o => o.LogoContentType).HasMaxLength(100);

        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(o => o.Status);
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Organizations_Status",
            "[Status] IN ('Active','Suspended','Deactivated')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Organizations_DefaultLanguage",
            "[DefaultLanguage] IN ('en','ar')"));

        builder.Property(o => o.RowVersion).IsRowVersion();
    }
}
