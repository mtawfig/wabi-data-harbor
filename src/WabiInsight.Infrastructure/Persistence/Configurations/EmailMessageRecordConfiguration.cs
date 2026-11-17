using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Email;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class EmailMessageRecordConfiguration : IEntityTypeConfiguration<EmailMessageRecord>
{
    public void Configure(EntityTypeBuilder<EmailMessageRecord> builder)
    {
        builder.ToTable("EmailMessages");
        builder.HasKey(e => e.EmailMessageId);
        builder.Property(e => e.EmailMessageId).ValueGeneratedNever();

        builder.Property(e => e.RecipientEmail).HasMaxLength(256).IsRequired();
        builder.Property(e => e.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Subject).HasMaxLength(500).IsRequired();
        builder.Property(e => e.TemplateCode).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Language).HasMaxLength(2).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_EmailMessages_Status", "[Status] IN ('Queued','Sent','Failed','Skipped')"));
        builder.Property(e => e.LastError).HasMaxLength(1000);
        builder.Property(e => e.RequestedByType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(64);

        builder.HasIndex(e => new { e.OrganizationId, e.CreatedAt });
        builder.HasIndex(e => new { e.Status, e.CreatedAt });
        builder.HasIndex(e => new { e.TemplateCode, e.CreatedAt });
    }
}
