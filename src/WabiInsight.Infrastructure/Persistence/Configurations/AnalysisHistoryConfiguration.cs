using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Analysis;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class AnalysisHistoryConfiguration : IEntityTypeConfiguration<AnalysisHistory>
{
    public void Configure(EntityTypeBuilder<AnalysisHistory> builder)
    {
        builder.ToTable("AnalysisHistories");
        builder.HasKey(h => h.HistoryId);
        builder.Property(h => h.HistoryId).ValueGeneratedNever();
        builder.Property(h => h.Title).HasMaxLength(200).IsRequired();
        builder.Property(h => h.OriginalPrompt).HasMaxLength(2000).IsRequired();
        builder.Property(h => h.PromptLanguage).HasMaxLength(2).IsRequired();
        builder.Property(h => h.OutputLanguage).HasMaxLength(2).IsRequired();
        builder.Property(h => h.AnalysisType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(h => h.DataClassification).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(h => h.RowVersion).IsRowVersion();
        builder.HasOne<Core.Entities.Platform.Organization>().WithMany().HasForeignKey(h => h.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Entities.Organizations.OrganizationUser>().WithMany().HasForeignKey(h => new { h.OrganizationId, h.OwnerUserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Entities.Organizations.Application>().WithMany().HasForeignKey(h => new { h.OrganizationId, h.ApplicationId }).HasPrincipalKey(a => new { a.OrganizationId, a.ApplicationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(h => h.Versions).WithOne(v => v.History).HasForeignKey(v => v.HistoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(h => new { h.OrganizationId, h.OwnerUserId, h.IsDeleted, h.IsArchived, h.UpdatedAt });
        builder.HasIndex(h => new { h.OrganizationId, h.ApplicationId });
    }
}

internal sealed class AnalysisHistoryVersionConfiguration : IEntityTypeConfiguration<AnalysisHistoryVersion>
{
    public void Configure(EntityTypeBuilder<AnalysisHistoryVersion> builder)
    {
        builder.ToTable("AnalysisHistoryVersions");
        builder.HasKey(v => v.VersionId);
        builder.Property(v => v.VersionId).ValueGeneratedNever();
        builder.Property(v => v.QueryPlanJson).IsRequired();
        builder.Property(v => v.CompiledSqlPreview).IsRequired();
        builder.Property(v => v.ResultSummary).IsRequired();
        builder.Property(v => v.ModelName).HasMaxLength(200);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(v => v.FailureStage).HasMaxLength(32);
        builder.Property(v => v.FailureCode).HasMaxLength(100);
        builder.Property(v => v.EstimatedCost).HasPrecision(14, 6);
        builder.HasIndex(v => new { v.HistoryId, v.VersionNumber }).IsUnique();
        builder.HasIndex(v => new { v.OrganizationId, v.CreatedAt });
    }
}

internal sealed class AnalysisArtifactConfiguration : IEntityTypeConfiguration<AnalysisArtifact>
{
    public void Configure(EntityTypeBuilder<AnalysisArtifact> builder)
    {
        builder.ToTable("AnalysisArtifacts");
        builder.HasKey(a => a.ArtifactId);
        builder.Property(a => a.ArtifactId).ValueGeneratedNever();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(260).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.FileName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Sha256).HasMaxLength(64).IsRequired();
        builder.HasOne<AnalysisHistoryVersion>().WithMany().HasForeignKey(a => a.VersionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.OrganizationId, a.HistoryId });
    }
}

internal sealed class ReportGenerationEventConfiguration : IEntityTypeConfiguration<ReportGenerationEvent>
{
    public void Configure(EntityTypeBuilder<ReportGenerationEvent> builder)
    {
        builder.ToTable("ReportGenerationEvents");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).ValueGeneratedNever();
        builder.Property(e => e.Format).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.Language).HasMaxLength(2).IsRequired();
        builder.Property(e => e.FailureCode).HasMaxLength(100);
        builder.HasIndex(e => new { e.OrganizationId, e.CreatedAt });
        builder.HasIndex(e => new { e.OrganizationId, e.HistoryId });
    }
}

internal sealed class AnalysisCollectionConfiguration : IEntityTypeConfiguration<AnalysisCollectionRecord>
{
    public void Configure(EntityTypeBuilder<AnalysisCollectionRecord> builder)
    {
        builder.ToTable("AnalysisCollections");
        builder.HasKey(c => c.CollectionId);
        builder.Property(c => c.CollectionId).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.Property(c => c.RowVersion).IsRowVersion();
        builder.HasOne<Core.Entities.Platform.Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Core.Entities.Organizations.OrganizationUser>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.OwnerUserId }).HasPrincipalKey(u => new { u.OrganizationId, u.UserId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(c => c.Items).WithOne(i => i.Collection).HasForeignKey(i => i.CollectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.OrganizationId, c.OwnerUserId, c.Name }).IsUnique();
    }
}

internal sealed class AnalysisCollectionItemConfiguration : IEntityTypeConfiguration<AnalysisCollectionItem>
{
    public void Configure(EntityTypeBuilder<AnalysisCollectionItem> builder)
    {
        builder.ToTable("AnalysisCollectionItems");
        builder.HasKey(i => new { i.CollectionId, i.HistoryId });
        builder.HasOne(i => i.History).WithMany().HasForeignKey(i => i.HistoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => new { i.OrganizationId, i.HistoryId });
    }
}
