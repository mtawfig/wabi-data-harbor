using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Ai;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class AiProviderConfigurationConfiguration : IEntityTypeConfiguration<AiProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<AiProviderConfiguration> builder)
    {
        builder.ToTable("AIProviderConfigurations", t =>
        {
            t.HasCheckConstraint("CK_AIProviderConfigurations_Scope", "([Scope] = 'Platform' AND [OrganizationId] IS NULL) OR ([Scope] = 'Organization' AND [OrganizationId] IS NOT NULL)");
            t.HasCheckConstraint("CK_AIProviderConfigurations_ProviderType", "[ProviderType] IN ('Anthropic','OpenAiCompatible','Gemini')");
        });
        builder.HasKey(p => p.ProviderId);
        builder.Property(p => p.Scope).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.ProviderType).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Endpoint).HasMaxLength(500);
        builder.Property(p => p.Model).HasMaxLength(200).IsRequired();
        builder.Property(p => p.EncryptedApiKey).HasMaxLength(2000);
        builder.Property(p => p.ApiKeyHint).HasMaxLength(16);
        builder.Property(p => p.InputPricePerMillion).HasPrecision(12, 4);
        builder.Property(p => p.OutputPricePerMillion).HasPrecision(12, 4);
        builder.Property(p => p.CachedInputPricePerMillion).HasPrecision(12, 4);
        builder.Property(p => p.Effort).HasMaxLength(16);
        builder.Property(p => p.LastTestDetail).HasMaxLength(500);
        builder.HasIndex(p => new { p.Scope, p.OrganizationId });
        builder.HasIndex(p => p.IsDefault).HasFilter("[IsDefault] = 1 AND [Scope] = 'Platform'").IsUnique();
        builder.HasOne<WabiInsight.Core.Entities.Platform.Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AnalysisExecutionMetricConfiguration : IEntityTypeConfiguration<AnalysisExecutionMetric>
{
    public void Configure(EntityTypeBuilder<AnalysisExecutionMetric> builder)
    {
        builder.ToTable("AnalysisExecutionMetrics");
        builder.HasKey(m => m.MetricId);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.Model).HasMaxLength(200);
        builder.Property(m => m.FailureStage).HasMaxLength(32);
        builder.Property(m => m.FailureCode).HasMaxLength(100);
        builder.Property(m => m.Language).HasMaxLength(2);
        builder.Property(m => m.ReportArtifactKey).HasMaxLength(260);
        builder.Property(m => m.ReportFileName).HasMaxLength(200);
        builder.Property(m => m.EstimatedCost).HasPrecision(14, 6);
        builder.HasIndex(m => new { m.OrganizationId, m.CreatedAt });
        builder.HasIndex(m => new { m.OrganizationId, m.UserId });
    }
}

internal sealed class AiCallConfiguration : IEntityTypeConfiguration<AiCall>
{
    public void Configure(EntityTypeBuilder<AiCall> builder)
    {
        builder.ToTable("AiCalls");
        builder.HasKey(c => c.CallId);
        builder.Property(c => c.CallId).ValueGeneratedNever();
        builder.Property(c => c.Purpose).HasMaxLength(32).IsRequired();
        builder.Property(c => c.ProviderType).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Model).HasMaxLength(200).IsRequired();
        builder.Property(c => c.FailureCode).HasMaxLength(100);
        builder.Property(c => c.StopReason).HasMaxLength(50);
        builder.Property(c => c.InputCost).HasPrecision(14, 6);
        builder.Property(c => c.OutputCost).HasPrecision(14, 6);
        builder.Property(c => c.TotalCost).HasPrecision(14, 6);
        builder.Property(c => c.InputPricePerMillion).HasPrecision(14, 6);
        builder.Property(c => c.OutputPricePerMillion).HasPrecision(14, 6);
        builder.Property(c => c.CachedInputPricePerMillion).HasPrecision(14, 6);
        // The month's spending of an organization is one range read of this index.
        builder.HasIndex(c => new { c.OrganizationId, c.CreatedAt });
        builder.HasIndex(c => c.RunId);
    }
}
