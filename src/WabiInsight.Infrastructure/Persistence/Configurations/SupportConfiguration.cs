using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("SupportTickets");
        builder.HasKey(t => t.TicketId);
        builder.Property(t => t.TicketId).ValueGeneratedNever();
        builder.HasAlternateKey(t => new { t.OrganizationId, t.TicketId });

        builder.Property(t => t.ReferenceNumber).HasMaxLength(20).IsRequired();
        builder.HasIndex(t => t.ReferenceNumber).IsUnique();

        builder.Property(t => t.OrganizationId).IsRequired();
        builder.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        // The requester must belong to the ticket's organization.
        builder.HasOne<OrganizationUser>()
            .WithMany()
            .HasForeignKey(t => new { t.OrganizationId, t.CreatedByUserId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.UserId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Administrator>().WithMany().HasForeignKey(t => t.AssignedAdministratorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Administrator>().WithMany().HasForeignKey(t => t.FirstOpenedByAdministratorId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(t => t.TicketType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Impact).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_SupportTickets_Status", "[Status] IN ('New','Read','Started','PendingUserResponse','Cancelled','Resolved')"));
        builder.Property(t => t.Category).HasMaxLength(100);
        builder.Property(t => t.Subject).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(8000).IsRequired();
        builder.Property(t => t.ResolutionSummary).HasMaxLength(4000);
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasIndex(t => new { t.OrganizationId, t.Status });
        builder.HasIndex(t => new { t.AssignedAdministratorId, t.Status });
        builder.HasIndex(t => t.LastActivityAt);
        builder.HasIndex(t => new { t.OrganizationId, t.CreatedByUserId });
    }
}

internal sealed class TicketSequenceConfiguration : IEntityTypeConfiguration<TicketSequence>
{
    public void Configure(EntityTypeBuilder<TicketSequence> builder)
    {
        builder.ToTable("TicketSequences");
        builder.HasKey(s => s.Year);
        builder.Property(s => s.Year).ValueGeneratedNever();
    }
}

internal sealed class SupportTicketEventConfiguration : IEntityTypeConfiguration<SupportTicketEvent>
{
    public void Configure(EntityTypeBuilder<SupportTicketEvent> builder)
    {
        builder.ToTable("SupportTicketEvents");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).ValueGeneratedNever();
        builder.HasOne<SupportTicket>()
            .WithMany()
            .HasForeignKey(e => new { e.OrganizationId, e.TicketId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.TicketId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.ActorType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(e => e.ActorDisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.PreviousStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.NewStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.DetailsJson).HasMaxLength(4000);
        builder.Property(e => e.Note).HasMaxLength(4000);
        builder.HasIndex(e => new { e.TicketId, e.CreatedAt });
    }
}

internal sealed class SupportTicketCommentConfiguration : IEntityTypeConfiguration<SupportTicketComment>
{
    public void Configure(EntityTypeBuilder<SupportTicketComment> builder)
    {
        builder.ToTable("SupportTicketComments");
        builder.HasKey(c => c.CommentId);
        builder.Property(c => c.CommentId).ValueGeneratedNever();
        builder.HasOne<SupportTicket>()
            .WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.TicketId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.TicketId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(c => c.AuthorType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(c => c.AuthorDisplayName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Body).HasMaxLength(8000).IsRequired();
        builder.HasIndex(c => new { c.TicketId, c.CreatedAt });
    }
}

internal sealed class SupportTicketAttachmentConfiguration : IEntityTypeConfiguration<SupportTicketAttachment>
{
    public void Configure(EntityTypeBuilder<SupportTicketAttachment> builder)
    {
        builder.ToTable("SupportTicketAttachments");
        builder.HasKey(a => a.AttachmentId);
        builder.Property(a => a.AttachmentId).ValueGeneratedNever();
        builder.HasOne<SupportTicket>()
            .WithMany()
            .HasForeignKey(a => new { a.OrganizationId, a.TicketId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.TicketId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupportTicketComment>().WithMany().HasForeignKey(a => a.CommentId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.FileName).HasMaxLength(255).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(127).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(a => a.StorageKey).IsUnique();
        builder.Property(a => a.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(a => a.ScanStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.UploadedByType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.UploadedByDisplayName).HasMaxLength(200).IsRequired();
        builder.HasIndex(a => new { a.TicketId, a.CreatedAt });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.NotificationId);
        builder.Property(n => n.NotificationId).ValueGeneratedNever();
        builder.Property(n => n.RecipientType).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(n => n.NotificationType).HasMaxLength(64).IsRequired();
        builder.Property(n => n.EntityType).HasMaxLength(64);
        builder.Property(n => n.EntityId).HasMaxLength(64);
        builder.Property(n => n.DataJson).HasMaxLength(4000);
        builder.HasIndex(n => new { n.RecipientType, n.RecipientId, n.ReadAt, n.CreatedAt });
    }
}
