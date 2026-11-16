using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Analysis;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class VerifiedAnswerConfiguration : IEntityTypeConfiguration<VerifiedAnswer>
{
    public void Configure(EntityTypeBuilder<VerifiedAnswer> builder)
    {
        builder.ToTable("VerifiedAnswers");
        builder.HasKey(a => a.VerifiedAnswerId);
        builder.Property(a => a.VerifiedAnswerId).ValueGeneratedNever();
        builder.Property(a => a.Question).HasMaxLength(2000).IsRequired();
        builder.Property(a => a.Language).HasMaxLength(2).IsRequired();
        builder.Property(a => a.PlanJson).IsRequired();
        builder.Property(a => a.Terms).HasMaxLength(2000).IsRequired();
        builder.Property(a => a.Status).HasMaxLength(16).IsRequired();
        builder.Property(a => a.LatestVerdict).HasMaxLength(16).IsRequired();
        builder.Property(a => a.LatestReason).HasMaxLength(500);
        builder.Property(a => a.ReviewNote).HasMaxLength(500);
        builder.Property(a => a.SetAsideReason).HasMaxLength(1000);
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.HasOne<Core.Entities.Platform.Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Entities.Organizations.Application>().WithMany().HasForeignKey(a => new { a.OrganizationId, a.ApplicationId }).HasPrincipalKey(p => new { p.OrganizationId, p.ApplicationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AnalysisHistory>().WithMany().HasForeignKey(a => a.HistoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.HistoryId, a.VersionNumber }).IsUnique();
        builder.HasIndex(a => new { a.OrganizationId, a.ApplicationId, a.Status });
        builder.HasIndex(a => new { a.OrganizationId, a.Status, a.LatestFeedbackAt });
    }
}

internal sealed class AnswerFeedbackConfiguration : IEntityTypeConfiguration<AnswerFeedback>
{
    public void Configure(EntityTypeBuilder<AnswerFeedback> builder)
    {
        builder.ToTable("AnswerFeedback");
        builder.HasKey(f => f.FeedbackId);
        builder.Property(f => f.FeedbackId).ValueGeneratedNever();
        builder.Property(f => f.Verdict).HasMaxLength(16).IsRequired();
        builder.Property(f => f.Reason).HasMaxLength(500);
        builder.HasOne<Core.Entities.Platform.Organization>().WithMany().HasForeignKey(f => f.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => new { f.OrganizationId, f.HistoryId, f.VersionNumber, f.CreatedAt });
    }
}
