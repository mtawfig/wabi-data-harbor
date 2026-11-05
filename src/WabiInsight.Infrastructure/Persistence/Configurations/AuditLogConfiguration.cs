using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Audit;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.AuditLogId);
        builder.Property(a => a.AuditLogId).ValueGeneratedNever();

        builder.Property(a => a.ActorType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.ActorDisplayName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.EventType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100);
        builder.Property(a => a.EntityId).HasMaxLength(64);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.Property(a => a.IpAddress).HasMaxLength(45);

        builder.HasIndex(a => new { a.OrganizationId, a.CreatedAt });
        builder.HasIndex(a => new { a.EventType, a.CreatedAt });
        builder.HasIndex(a => a.CreatedAt);
    }
}
