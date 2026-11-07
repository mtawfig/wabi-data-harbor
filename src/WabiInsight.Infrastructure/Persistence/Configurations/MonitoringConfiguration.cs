using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Monitoring;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class AnalysisAccessFactConfiguration : IEntityTypeConfiguration<AnalysisAccessFact>
{
    public void Configure(EntityTypeBuilder<AnalysisAccessFact> builder)
    {
        builder.ToTable("AnalysisAccessFacts");
        builder.HasKey(f => f.FactId);
        builder.Property(f => f.FactId).ValueGeneratedNever();
        builder.HasOne<Organization>().WithMany().HasForeignKey(f => f.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<OrganizationUser>().WithMany().HasForeignKey(f => new { f.OrganizationId, f.UserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.NoAction);

        builder.Property(f => f.RoleCodes).HasMaxLength(400).IsRequired();
        builder.Property(f => f.SchemaName).HasMaxLength(128).IsRequired();
        builder.Property(f => f.ObjectName).HasMaxLength(128).IsRequired();
        builder.Property(f => f.HighestClassification).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(f => f.ColumnIdsJson).HasMaxLength(4000).IsRequired();
        builder.Property(f => f.FilterColumnIdsJson).HasMaxLength(2000).IsRequired();
        builder.Property(f => f.ExportFormat).HasConversion<string>().HasMaxLength(10);
        builder.Property(f => f.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(f => f.FailureCode).HasMaxLength(100);

        builder.HasIndex(f => new { f.OrganizationId, f.AccessedAt });
        builder.HasIndex(f => new { f.OrganizationId, f.UserId, f.AccessedAt });
        builder.HasIndex(f => new { f.OrganizationId, f.ObjectId, f.AccessedAt });
        builder.HasIndex(f => new { f.OrganizationId, f.Outcome, f.AccessedAt });
    }
}

internal sealed class AccessBaselineConfiguration : IEntityTypeConfiguration<AccessBaseline>
{
    public void Configure(EntityTypeBuilder<AccessBaseline> builder)
    {
        builder.ToTable("AccessBaselines");
        builder.HasKey(b => b.BaselineId);
        builder.Property(b => b.BaselineId).ValueGeneratedNever();
        builder.HasOne<Organization>().WithMany().HasForeignKey(b => b.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(b => b.SubjectKind).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(b => b.RoleCode).HasMaxLength(64);
        builder.HasIndex(b => new { b.OrganizationId, b.SubjectKind, b.UserId, b.RoleCode }).IsUnique();
    }
}

internal sealed class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> builder)
    {
        builder.ToTable("ScheduledJobs");
        builder.HasKey(j => j.JobId);
        builder.Property(j => j.JobId).ValueGeneratedNever();
        builder.HasOne<Organization>().WithMany().HasForeignKey(j => j.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(j => j.JobType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(j => j.RecurrenceKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(j => j.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(j => j.LeaseOwner).HasMaxLength(100);
        builder.Property(j => j.LastOutcome).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.LastError).HasMaxLength(1000);
        builder.HasIndex(j => new { j.IsEnabled, j.NextRunAt });
        builder.HasIndex(j => new { j.OrganizationId, j.JobType, j.TargetId });
    }
}
