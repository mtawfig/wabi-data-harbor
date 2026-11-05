using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class LoginIdentityConfiguration : IEntityTypeConfiguration<LoginIdentity>
{
    public void Configure(EntityTypeBuilder<LoginIdentity> builder)
    {
        builder.ToTable("LoginIdentities");

        // Primary key on the normalized email: one login identity across both account stores.
        builder.HasKey(l => l.NormalizedEmail);
        builder.Property(l => l.NormalizedEmail).HasMaxLength(256);

        builder.Property(l => l.PrincipalType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_LoginIdentities_PrincipalType",
            "[PrincipalType] IN ('PlatformAdministrator','OrganizationUser')"));

        builder.HasIndex(l => new { l.PrincipalType, l.PrincipalId }).IsUnique();
        builder.HasIndex(l => l.OrganizationId);
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(l => l.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
