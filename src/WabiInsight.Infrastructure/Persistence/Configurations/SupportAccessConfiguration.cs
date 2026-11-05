using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;
using WabiInsight.Core.Entities.Support;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class SupportAccessGrantConfiguration : IEntityTypeConfiguration<SupportAccessGrant>
{
    public void Configure(EntityTypeBuilder<SupportAccessGrant> builder)
    {
        builder.ToTable("SupportAccessGrants");
        builder.HasKey(g => g.GrantId);
        builder.Property(g => g.GrantId).ValueGeneratedNever();
        builder.HasAlternateKey(g => new { g.OrganizationId, g.GrantId });

        builder.HasOne<Organization>().WithMany().HasForeignKey(g => g.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupportTicket>()
            .WithMany()
            .HasForeignKey(g => new { g.OrganizationId, g.TicketId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.TicketId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrganizationUser>()
            .WithMany()
            .HasForeignKey(g => new { g.OrganizationId, g.RequestedByUserId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.UserId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Administrator>().WithMany().HasForeignKey(g => g.ProposedByAdministratorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Administrator>().WithMany().HasForeignKey(g => g.AssignedPlatformAdministratorId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(g => g.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_SupportAccessGrants_Status", "[Status] IN ('Proposed','Approved','Active','Expired','Revoked','Completed','Declined')"));
        builder.Property(g => g.AllowedOperationsJson).HasMaxLength(8000).IsRequired();
        builder.Property(g => g.ApprovedObjectReferences).HasMaxLength(4000);
        builder.Property(g => g.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(g => g.DecisionNote).HasMaxLength(2000);
        builder.Property(g => g.RevokedByType).HasConversion<string>().HasMaxLength(32);
        builder.Property(g => g.RowVersion).IsRowVersion();

        builder.HasIndex(g => new { g.OrganizationId, g.Status });
        builder.HasIndex(g => new { g.TicketId, g.CreatedAt });
        builder.HasIndex(g => new { g.AssignedPlatformAdministratorId, g.Status });
        builder.HasIndex(g => g.ExpiresAt);
    }
}

internal sealed class SupportSessionConfiguration : IEntityTypeConfiguration<SupportSession>
{
    public void Configure(EntityTypeBuilder<SupportSession> builder)
    {
        builder.ToTable("SupportSessions");
        builder.HasKey(s => s.SessionId);
        builder.Property(s => s.SessionId).ValueGeneratedNever();
        builder.HasOne<SupportAccessGrant>()
            .WithMany()
            .HasForeignKey(s => new { s.OrganizationId, s.GrantId })
            .HasPrincipalKey(g => new { g.OrganizationId, g.GrantId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Administrator>().WithMany().HasForeignKey(s => s.AdministratorId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.EndReason).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(s => new { s.AdministratorId, s.EndedAt });
        builder.HasIndex(s => new { s.GrantId, s.StartedAt });
    }
}
