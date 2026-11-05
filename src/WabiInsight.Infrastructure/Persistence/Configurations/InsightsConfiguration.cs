using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Insights;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class InsightsObjectConfiguration : IEntityTypeConfiguration<InsightsObject>
{
    public void Configure(EntityTypeBuilder<InsightsObject> builder)
    {
        builder.ToTable("InsightsObjects");
        builder.HasKey(o => o.InsightsObjectId);
        builder.Property(o => o.ViewName).HasMaxLength(128).IsRequired();
        builder.HasIndex(o => o.ViewName).IsUnique();
        builder.Property(o => o.DisplayNameEn).HasMaxLength(200).IsRequired();
        builder.Property(o => o.DisplayNameAr).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Description).HasMaxLength(1000);
    }
}

internal sealed class InsightsColumnConfiguration : IEntityTypeConfiguration<InsightsColumn>
{
    public void Configure(EntityTypeBuilder<InsightsColumn> builder)
    {
        builder.ToTable("InsightsColumns");
        builder.HasKey(c => c.InsightsColumnId);
        builder.Property(c => c.ColumnName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.DataType).HasMaxLength(64).IsRequired();
        builder.Property(c => c.DisplayNameEn).HasMaxLength(200).IsRequired();
        builder.Property(c => c.DisplayNameAr).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.HasIndex(c => new { c.InsightsObjectId, c.ColumnName }).IsUnique();
        builder.HasOne<InsightsObject>().WithMany().HasForeignKey(c => c.InsightsObjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InsightsMetricConfiguration : IEntityTypeConfiguration<InsightsMetric>
{
    public void Configure(EntityTypeBuilder<InsightsMetric> builder)
    {
        builder.ToTable("InsightsMetrics", t => t.HasCheckConstraint("CK_InsightsMetrics_Aggregate", "[Aggregate] IN ('Count','CountDistinct','Sum','Average','Min','Max')"));
        builder.HasKey(m => m.InsightsMetricId);
        builder.Property(m => m.MetricCode).HasMaxLength(100).IsRequired();
        builder.HasIndex(m => m.MetricCode).IsUnique();
        builder.Property(m => m.DisplayNameEn).HasMaxLength(200).IsRequired();
        builder.Property(m => m.DisplayNameAr).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(1000);
        builder.Property(m => m.Aggregate).HasConversion<string>().HasMaxLength(32);
        builder.Property(m => m.ColumnName).HasMaxLength(128);
        builder.Property(m => m.DateColumn).HasMaxLength(128);
        builder.Property(m => m.FiltersJson).HasMaxLength(4000).IsRequired();
        builder.Property(m => m.Unit).HasMaxLength(32).IsRequired();
        builder.HasOne<InsightsObject>().WithMany().HasForeignKey(m => m.InsightsObjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
