using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Analysis;
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

internal sealed class MonitoringRuleConfiguration : IEntityTypeConfiguration<MonitoringRule>
{
    public void Configure(EntityTypeBuilder<MonitoringRule> builder)
    {
        builder.ToTable("MonitoringRules");
        builder.HasKey(r => r.RuleId);
        builder.Property(r => r.RuleId).ValueGeneratedNever();
        builder.HasAlternateKey(r => new { r.OrganizationId, r.RuleId });
        builder.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<OrganizationUser>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.OwnerUserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.NoAction);
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(2000);
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(r => r.TemplateCode).HasMaxLength(64);
        builder.Property(r => r.DefinitionJson).HasMaxLength(8000).IsRequired();
        builder.Property(r => r.Severity).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(r => r.RecurrenceKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.LastOutcome).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.LastError).HasMaxLength(200);
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.HasIndex(r => new { r.OrganizationId, r.OwnerUserId });
        builder.HasIndex(r => new { r.OrganizationId, r.Name }).IsUnique();
    }
}

internal sealed class MonitoringRuleRecipientConfiguration : IEntityTypeConfiguration<MonitoringRuleRecipient>
{
    public void Configure(EntityTypeBuilder<MonitoringRuleRecipient> builder)
    {
        builder.ToTable("MonitoringRuleRecipients", t => t.HasCheckConstraint("CK_MonitoringRuleRecipients_OneTarget", "([UserId] IS NULL AND [RoleId] IS NOT NULL) OR ([UserId] IS NOT NULL AND [RoleId] IS NULL)"));
        builder.HasKey(r => r.RecipientId);
        builder.Property(r => r.RecipientId).ValueGeneratedNever();
        builder.HasOne<MonitoringRule>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.RuleId }).HasPrincipalKey(r => new { r.OrganizationId, r.RuleId }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<OrganizationUser>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.UserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Role>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.RoleId }).HasPrincipalKey(r => new { r.OrganizationId, r.RoleId }).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(r => new { r.OrganizationId, r.RuleId, r.UserId, r.RoleId }).IsUnique().HasFilter(null);
    }
}

internal sealed class RuleEvaluationConfiguration : IEntityTypeConfiguration<RuleEvaluation>
{
    public void Configure(EntityTypeBuilder<RuleEvaluation> builder)
    {
        builder.ToTable("RuleEvaluations");
        builder.HasKey(e => e.EvaluationId);
        builder.Property(e => e.EvaluationId).ValueGeneratedNever();
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // No foreign key to the rule: evaluations outlive a deleted rule until retention removes them.
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.ErrorCode).HasMaxLength(100);
        builder.HasIndex(e => new { e.OrganizationId, e.RuleId, e.StartedAt });
    }
}

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts");
        builder.HasKey(a => a.AlertId);
        builder.Property(a => a.AlertId).ValueGeneratedNever();
        builder.HasAlternateKey(a => new { a.OrganizationId, a.AlertId });
        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(a => a.Severity).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.GroupKey).HasMaxLength(400).IsRequired();
        builder.Property(a => a.Title).HasMaxLength(300).IsRequired();
        builder.Property(a => a.SummaryJson).HasMaxLength(4000).IsRequired();
        builder.Property(a => a.Note).HasMaxLength(2000);
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.HasIndex(a => new { a.OrganizationId, a.Status, a.LastSeenAt });
        builder.HasIndex(a => new { a.OrganizationId, a.RuleId, a.GroupKey, a.Status });
    }
}

internal sealed class AlertEventConfiguration : IEntityTypeConfiguration<AlertEvent>
{
    public void Configure(EntityTypeBuilder<AlertEvent> builder)
    {
        builder.ToTable("AlertEvents");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).ValueGeneratedNever();
        builder.HasOne<Alert>().WithMany().HasForeignKey(e => new { e.OrganizationId, e.AlertId }).HasPrincipalKey(a => new { a.OrganizationId, a.AlertId }).OnDelete(DeleteBehavior.Cascade);
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(2000);
        builder.HasIndex(e => new { e.OrganizationId, e.AlertId, e.CreatedAt });
    }
}

internal sealed class ReportSubscriptionConfiguration : IEntityTypeConfiguration<ReportSubscription>
{
    public void Configure(EntityTypeBuilder<ReportSubscription> builder)
    {
        builder.ToTable("ReportSubscriptions", t =>
        {
            t.HasCheckConstraint("CK_ReportSubscriptions_Format", "[Format] IN ('Pdf','Xlsx')");
            t.HasCheckConstraint("CK_ReportSubscriptions_Language", "[Language] IN ('en','ar','recipient')");
        });
        builder.HasKey(s => s.SubscriptionId);
        builder.Property(s => s.SubscriptionId).ValueGeneratedNever();
        builder.HasAlternateKey(s => new { s.OrganizationId, s.SubscriptionId });
        builder.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AnalysisHistory>().WithMany().HasForeignKey(s => s.HistoryId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrganizationUser>().WithMany().HasForeignKey(s => new { s.OrganizationId, s.CreatedByUserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.NoAction);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.RecurrenceKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(s => s.Format).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(s => s.Language).HasMaxLength(10).IsRequired();
        builder.Property(s => s.PausedReason).HasMaxLength(100);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.HasIndex(s => new { s.OrganizationId, s.Name }).IsUnique();
        builder.HasIndex(s => new { s.OrganizationId, s.CreatedByUserId });
    }
}

internal sealed class ReportSubscriptionRecipientConfiguration : IEntityTypeConfiguration<ReportSubscriptionRecipient>
{
    public void Configure(EntityTypeBuilder<ReportSubscriptionRecipient> builder)
    {
        builder.ToTable("ReportSubscriptionRecipients");
        builder.HasKey(r => r.RecipientId);
        builder.Property(r => r.RecipientId).ValueGeneratedNever();
        builder.HasOne<ReportSubscription>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.SubscriptionId }).HasPrincipalKey(s => new { s.OrganizationId, s.SubscriptionId }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<OrganizationUser>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.UserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(r => new { r.OrganizationId, r.SubscriptionId, r.UserId }).IsUnique();
        builder.HasIndex(r => new { r.OrganizationId, r.UserId });
    }
}

internal sealed class ReportDeliveryConfiguration : IEntityTypeConfiguration<ReportDelivery>
{
    public void Configure(EntityTypeBuilder<ReportDelivery> builder)
    {
        builder.ToTable("ReportDeliveries");
        builder.HasKey(d => d.DeliveryId);
        builder.Property(d => d.DeliveryId).ValueGeneratedNever();
        builder.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        // No foreign key to the subscription: the log and the delivered files outlive a deleted subscription until retention.
        builder.HasOne<OrganizationUser>().WithMany().HasForeignKey(d => new { d.OrganizationId, d.RecipientUserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.NoAction);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.ReportName).HasMaxLength(200).IsRequired();
        builder.Property(d => d.AnalysisTitle).HasMaxLength(300).IsRequired();
        builder.Property(d => d.StorageKey).HasMaxLength(260);
        builder.Property(d => d.FileName).HasMaxLength(260);
        builder.Property(d => d.ContentType).HasMaxLength(100);
        builder.Property(d => d.Language).HasMaxLength(2).IsRequired();
        builder.Property(d => d.ErrorCode).HasMaxLength(100);
        builder.HasIndex(d => new { d.OrganizationId, d.SubscriptionId, d.CreatedAt });
        builder.HasIndex(d => new { d.OrganizationId, d.RecipientUserId, d.CreatedAt });
    }
}
