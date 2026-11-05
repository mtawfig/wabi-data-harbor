using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class SystemInitializationConfiguration : IEntityTypeConfiguration<SystemInitialization>
{
    public void Configure(EntityTypeBuilder<SystemInitialization> builder)
    {
        builder.ToTable("SystemInitialization");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Singleton: the only permitted key value is 1.
        builder.ToTable(t => t.HasCheckConstraint("CK_SystemInitialization_Singleton", "[Id] = 1"));

        builder.Property(s => s.SchemaVersion).HasMaxLength(100);
        builder.HasOne<Administrator>()
            .WithMany()
            .HasForeignKey(s => s.BootstrapAdministratorId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(s => s.RowVersion).IsRowVersion();
    }
}
