using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class DemoRequestConfiguration : IEntityTypeConfiguration<DemoRequest>
{
    public void Configure(EntityTypeBuilder<DemoRequest> builder)
    {
        builder.ToTable("DemoRequests");
        builder.HasKey(r => r.DemoRequestId);
        builder.Property(r => r.DemoRequestId).ValueGeneratedNever();

        builder.Property(r => r.FullName).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Email).HasMaxLength(254).IsRequired();
        builder.Property(r => r.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(r => r.OrganizationName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.CountryCode).HasMaxLength(2).IsUnicode(false).IsRequired();
        builder.Property(r => r.JobTitle).HasMaxLength(150);
        builder.Property(r => r.Phone).HasMaxLength(40);
        builder.Property(r => r.Industry).HasMaxLength(40).IsUnicode(false);
        builder.Property(r => r.OrganizationSize).HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.DatabaseSystems).HasMaxLength(200);
        builder.Property(r => r.DemoLanguage).HasMaxLength(2).IsUnicode(false).IsRequired();
        builder.Property(r => r.PreferredTime).HasMaxLength(200);
        builder.Property(r => r.Message).HasMaxLength(2000);
        builder.Property(r => r.PageLanguage).HasMaxLength(2).IsUnicode(false).IsRequired();
        builder.Property(r => r.IpAddress).HasMaxLength(64);
        builder.Property(r => r.UserAgent).HasMaxLength(400);

        builder.Property(r => r.Status).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_DemoRequests_Status",
            "[Status] IN ('New','Contacted','Scheduled','Performed','Converted','NotInterested','Cancelled','Spam')"));
        builder.Property(r => r.Remark).HasMaxLength(2000);
        builder.Property(r => r.MeetingLocation).HasMaxLength(400);

        builder.Property(r => r.PresentedBy).HasMaxLength(150);
        builder.Property(r => r.AttendeeName).HasMaxLength(150);
        builder.Property(r => r.AttendeePosition).HasMaxLength(150);
        builder.Property(r => r.AttendeeEmail).HasMaxLength(254);
        builder.Property(r => r.AttendeePhone).HasMaxLength(40);
        builder.Property(r => r.AttendeeMobile).HasMaxLength(40);
        builder.Property(r => r.Impression).HasMaxLength(20).IsUnicode(false);
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_DemoRequests_Impression",
            "[Impression] IS NULL OR [Impression] IN ('VeryPositive','Positive','Neutral','Negative')"));
        builder.Property(r => r.PerformedRemark).HasMaxLength(2000);
        builder.Property(r => r.NextStep).HasMaxLength(500);

        // The list (newest first, by status), "has this address an open request" and "how many from this client today".
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => new { r.NormalizedEmail, r.CreatedAt });
        builder.HasIndex(r => new { r.IpAddress, r.CreatedAt });
        builder.HasIndex(r => r.CreatedAt);

        builder.HasOne<Administrator>().WithMany().HasForeignKey(r => r.AssignedAdministratorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Administrator>().WithMany().HasForeignKey(r => r.UpdatedByAdministratorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DemoRequestEventConfiguration : IEntityTypeConfiguration<DemoRequestEvent>
{
    public void Configure(EntityTypeBuilder<DemoRequestEvent> builder)
    {
        builder.ToTable("DemoRequestEvents");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).ValueGeneratedNever();
        builder.Property(e => e.EventType).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.PreviousStatus).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.NewStatus).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.Note).HasMaxLength(2000);
        builder.Property(e => e.ActorDisplayName).HasMaxLength(200);
        builder.HasIndex(e => new { e.DemoRequestId, e.CreatedAt });
        builder.HasOne<DemoRequest>().WithMany().HasForeignKey(e => e.DemoRequestId).OnDelete(DeleteBehavior.Cascade);
    }
}
