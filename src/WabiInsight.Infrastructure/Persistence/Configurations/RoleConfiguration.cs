using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.RoleId);
        builder.Property(r => r.RoleId).ValueGeneratedNever();

        // Alternate key so UserRoles can reference (OrganizationId, RoleId): a role can never be assigned across organizations.
        builder.HasAlternateKey(r => new { r.OrganizationId, r.RoleId });

        builder.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.RoleCode).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => new { r.OrganizationId, r.RoleCode }).IsUnique();
        builder.Property(r => r.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.RowVersion).IsRowVersion();
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.HasOne<Organization>().WithMany().HasForeignKey(ur => ur.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        // Composite foreign keys carry the OrganizationId so the database rejects a user of A holding a role of B.
        builder.HasOne<OrganizationUser>()
            .WithMany()
            .HasForeignKey(ur => new { ur.OrganizationId, ur.UserId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.UserId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(ur => new { ur.OrganizationId, ur.RoleId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.RoleId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ur => new { ur.OrganizationId, ur.RoleId });
    }
}

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("Applications");
        builder.HasKey(a => a.ApplicationId);
        builder.Property(a => a.ApplicationId).ValueGeneratedNever();
        builder.HasAlternateKey(a => new { a.OrganizationId, a.ApplicationId });

        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.ConnectionHint).HasMaxLength(300);
        builder.Property(a => a.ExampleQuestionsJson).IsRequired().HasDefaultValue("[]");

        builder.Property(a => a.ApplicationName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ApplicationNameAr).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ApplicationCode).HasMaxLength(64).IsRequired();
        builder.HasIndex(a => new { a.OrganizationId, a.ApplicationCode }).IsUnique();
        builder.Property(a => a.Description).HasMaxLength(1000);
        builder.Property(a => a.DatabaseType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_Applications_DatabaseType", "[DatabaseType] IN ('SqlServer','Oracle','PlatformInsights')"));
        builder.ToTable(t => t.HasCheckConstraint("CK_Applications_BuiltIn", "([IsBuiltIn] = 0 AND [BuiltInCode] IS NULL) OR ([IsBuiltIn] = 1 AND [BuiltInCode] IS NOT NULL AND [EncryptedConnectionString] IS NULL)"));
        builder.Property(a => a.ConnectionFingerprint).HasMaxLength(64);
        builder.HasIndex(a => a.ConnectionFingerprint).IsUnique().HasFilter("[ConnectionFingerprint] IS NOT NULL");
        builder.Property(a => a.BuiltInCode).HasMaxLength(32);
        // Exactly one built-in application of each code per organization.
        builder.HasIndex(a => new { a.OrganizationId, a.BuiltInCode }).IsUnique().HasFilter("[BuiltInCode] IS NOT NULL");
        builder.HasIndex(a => new { a.OrganizationId, a.IsActive });
        builder.Property(a => a.RowVersion).IsRowVersion();
    }
}
