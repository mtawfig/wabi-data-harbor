using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class PasswordResetRequestConfiguration : IEntityTypeConfiguration<PasswordResetRequest>
{
    public void Configure(EntityTypeBuilder<PasswordResetRequest> builder)
    {
        builder.ToTable("PasswordResetRequests");
        builder.HasKey(r => r.PasswordResetRequestId);
        builder.Property(r => r.PasswordResetRequestId).ValueGeneratedNever();

        builder.Property(r => r.PrincipalType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PasswordResetRequests_PrincipalType",
            "[PrincipalType] IN ('PlatformAdministrator','OrganizationUser')"));
        // An organization user's request always names the organization; an administrator's never does.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PasswordResetRequests_Organization",
            "([PrincipalType] = 'OrganizationUser' AND [OrganizationId] IS NOT NULL) OR ([PrincipalType] = 'PlatformAdministrator' AND [OrganizationId] IS NULL)"));

        // SHA-256 in hexadecimal: the link is found by its hash, and no two links share one.
        builder.Property(r => r.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(r => r.TokenHash).IsUnique();
        builder.Property(r => r.RequestedFromIp).HasMaxLength(64);

        // "How many links did this account ask for in the last hour" and "which of its links are still open".
        builder.HasIndex(r => new { r.PrincipalType, r.PrincipalId, r.CreatedAt });
        builder.HasIndex(r => r.ExpiresAt);
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(r => r.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
