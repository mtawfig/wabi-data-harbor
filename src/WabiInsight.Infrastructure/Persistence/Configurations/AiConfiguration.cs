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
