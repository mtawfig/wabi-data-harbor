using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class AdministratorConfiguration : IEntityTypeConfiguration<Administrator>
{
    public void Configure(EntityTypeBuilder<Administrator> builder)
    {
        builder.ToTable("Administrators");
        builder.HasKey(a => a.AdministratorId);
        builder.Property(a => a.AdministratorId).ValueGeneratedNever();

        builder.Property(a => a.Email).HasMaxLength(256).IsRequired();
        builder.Property(a => a.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.HasIndex(a => a.NormalizedEmail).IsUnique();

        builder.Property(a => a.FullName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(a => a.SecurityStamp).HasMaxLength(64).IsRequired();
        builder.Property(a => a.PreferredLanguage).HasMaxLength(2).IsRequired();

        builder.HasOne<Administrator>()
            .WithMany()
            .HasForeignKey(a => a.CreatedByAdministratorId)
            .OnDelete(DeleteBehavior.NoAction);

        // Exactly one bootstrap administrator can ever exist.
        builder.HasIndex(a => a.IsBootstrapAdministrator)
            .IsUnique()
            .HasFilter("[IsBootstrapAdministrator] = 1");

        builder.Property(a => a.RowVersion).IsRowVersion();
    }
}
