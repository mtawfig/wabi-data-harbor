using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Usage;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class UsageEventConfiguration : IEntityTypeConfiguration<UsageEvent>
{
    public void Configure(EntityTypeBuilder<UsageEvent> builder)
    {
        builder.ToTable("UsageEvents");
        builder.HasKey(e => e.UsageEventId);
        builder.Property(e => e.UsageEventId).ValueGeneratedNever();

        builder.Property(e => e.ActorType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.EntityType).HasMaxLength(100);
        builder.Property(e => e.EntityId).HasMaxLength(64);
        builder.Property(e => e.MetadataJson).HasMaxLength(4000);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);

        builder.HasIndex(e => new { e.OrganizationId, e.CreatedAt });
        builder.HasIndex(e => new { e.EventType, e.CreatedAt });
        builder.HasIndex(e => new { e.OrganizationId, e.EventType, e.CreatedAt });
        builder.HasIndex(e => e.CreatedAt);
    }
}
