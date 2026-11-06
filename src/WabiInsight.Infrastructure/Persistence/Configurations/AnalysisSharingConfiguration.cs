using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Analysis;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class AnalysisShareConfiguration : IEntityTypeConfiguration<AnalysisShare>
{
    public void Configure(EntityTypeBuilder<AnalysisShare> builder)
    {
        builder.ToTable("AnalysisShares");
        builder.HasKey(s => s.ShareId);
        builder.Property(s => s.ShareId).ValueGeneratedNever();
        builder.Property(s => s.Permission).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(s => s.Message).HasMaxLength(1000);
        builder.HasOne(s => s.History).WithMany().HasForeignKey(s => s.HistoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Core.Entities.Platform.Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        // Owner and recipient are users of the share's organization: the composite keys make a cross-organization share unrepresentable.
        builder.HasOne<Core.Entities.Organizations.OrganizationUser>().WithMany().HasForeignKey(s => new { s.OrganizationId, s.OwnerUserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Entities.Organizations.OrganizationUser>().WithMany().HasForeignKey(s => new { s.OrganizationId, s.RecipientUserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.OrganizationId, s.RecipientUserId, s.RevokedAt, s.ExpiresAt });
        builder.HasIndex(s => new { s.OrganizationId, s.OwnerUserId, s.CreatedAt });
        builder.HasIndex(s => new { s.HistoryId, s.RecipientUserId });
    }
}

internal sealed class EmailDraftEventConfiguration : IEntityTypeConfiguration<EmailDraftEvent>
{
    public void Configure(EntityTypeBuilder<EmailDraftEvent> builder)
    {
        builder.ToTable("EmailDraftEvents");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.DataClassification).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.Recipients).HasMaxLength(2000);
        builder.Property(e => e.BlockReason).HasMaxLength(100);
        builder.HasIndex(e => new { e.OrganizationId, e.CreatedAt });
        builder.HasIndex(e => new { e.OrganizationId, e.HistoryId });
    }
}
