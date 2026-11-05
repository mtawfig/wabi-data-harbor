using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationUserConfiguration : IEntityTypeConfiguration<OrganizationUser>
{
    public void Configure(EntityTypeBuilder<OrganizationUser> builder)
    {
        builder.ToTable("OrganizationUsers");
        builder.HasKey(u => u.UserId);
        builder.Property(u => u.UserId).ValueGeneratedNever();
        builder.HasAlternateKey(u => new { u.OrganizationId, u.UserId });

        // Mandatory ownership: NOT NULL + FK to Organizations (specification §4.2).
        builder.Property(u => u.OrganizationId).IsRequired();
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.HasIndex(u => new { u.OrganizationId, u.NormalizedEmail }).IsUnique();

        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.EmployeeNumber).HasMaxLength(50);
        builder.HasIndex(u => new { u.OrganizationId, u.EmployeeNumber })
            .IsUnique()
            .HasFilter("[EmployeeNumber] IS NOT NULL");

        builder.Property(u => u.PhoneNumber).HasMaxLength(50);
        builder.Property(u => u.PreferredLanguage).HasMaxLength(2).IsRequired();
        builder.Property(u => u.PreferredLocale).HasMaxLength(16);
        builder.Property(u => u.PreferredTimezone).HasMaxLength(64);
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();

        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(u => new { u.OrganizationId, u.Status });
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_OrganizationUsers_Status",
            "[Status] IN ('PendingInvitation','Active','Inactive')"));

        builder.HasIndex(u => new { u.OrganizationId, u.DepartmentId });
        builder.HasIndex(u => new { u.OrganizationId, u.SectionId });

        builder.HasOne<Administrator>()
            .WithMany()
            .HasForeignKey(u => u.CreatedByAdministratorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(u => u.RowVersion).IsRowVersion();
    }
}
