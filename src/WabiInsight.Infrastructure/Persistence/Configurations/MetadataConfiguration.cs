using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WabiInsight.Core.Entities.Metadata;
using WabiInsight.Core.Entities.Organizations;
using WabiInsight.Core.Entities.Platform;

namespace WabiInsight.Infrastructure.Persistence.Configurations;

internal sealed class DatabaseObjectConfiguration : IEntityTypeConfiguration<DatabaseObject>
{
    public void Configure(EntityTypeBuilder<DatabaseObject> builder)
    {
        builder.ToTable("DatabaseObjects");
        builder.HasKey(o => o.ObjectId);
        builder.Property(o => o.ObjectId).ValueGeneratedNever();
        builder.HasAlternateKey(o => new { o.OrganizationId, o.ObjectId });
        builder.HasOne<Organization>().WithMany().HasForeignKey(o => o.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Application>()
            .WithMany()
            .HasForeignKey(o => new { o.OrganizationId, o.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.ApplicationId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(o => o.SchemaName).HasMaxLength(128).IsRequired();
        builder.Property(o => o.ObjectName).HasMaxLength(128).IsRequired();
        builder.HasIndex(o => new { o.ApplicationId, o.SchemaName, o.ObjectName }).IsUnique();
        builder.Property(o => o.ObjectType).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(o => o.DisplayNameEn).HasMaxLength(200);
        builder.Property(o => o.DisplayNameAr).HasMaxLength(200);
        builder.Property(o => o.BusinessDescription).HasMaxLength(2000);
        builder.HasIndex(o => new { o.OrganizationId, o.ApplicationId, o.IsApproved });
        builder.Property(o => o.RowVersion).IsRowVersion();
    }
}

internal sealed class DatabaseColumnConfiguration : IEntityTypeConfiguration<DatabaseColumn>
{
    public void Configure(EntityTypeBuilder<DatabaseColumn> builder)
    {
        builder.ToTable("DatabaseColumns");
        builder.HasKey(c => c.ColumnId);
        builder.Property(c => c.ColumnId).ValueGeneratedNever();
        builder.HasAlternateKey(c => new { c.OrganizationId, c.ColumnId });
        builder.HasOne<DatabaseObject>()
            .WithMany()
            .HasForeignKey(c => new { c.OrganizationId, c.ObjectId })
            .HasPrincipalKey(o => new { o.OrganizationId, o.ObjectId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.ColumnName).HasMaxLength(128).IsRequired();
        builder.HasIndex(c => new { c.ObjectId, c.ColumnName }).IsUnique();
        builder.Property(c => c.DataType).HasMaxLength(128).IsRequired();
        builder.Property(c => c.DisplayNameEn).HasMaxLength(200);
        builder.Property(c => c.DisplayNameAr).HasMaxLength(200);
        builder.Property(c => c.BusinessDescription).HasMaxLength(2000);
        builder.Property(c => c.Classification).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_DatabaseColumns_Classification", "[Classification] IN ('Internal','Confidential','Restricted')"));
        builder.Property(c => c.RowVersion).IsRowVersion();
    }
}

internal sealed class DatabaseRelationshipConfiguration : IEntityTypeConfiguration<DatabaseRelationship>
{
    public void Configure(EntityTypeBuilder<DatabaseRelationship> builder)
    {
        builder.ToTable("DatabaseRelationships");
        builder.HasKey(r => r.RelationshipId);
        builder.Property(r => r.RelationshipId).ValueGeneratedNever();
        builder.HasOne<Application>()
            .WithMany()
            .HasForeignKey(r => new { r.OrganizationId, r.ApplicationId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.ApplicationId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DatabaseObject>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.FromObjectId }).HasPrincipalKey(o => new { o.OrganizationId, o.ObjectId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DatabaseObject>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.ToObjectId }).HasPrincipalKey(o => new { o.OrganizationId, o.ObjectId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DatabaseColumn>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.FromColumnId }).HasPrincipalKey(c => new { c.OrganizationId, c.ColumnId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DatabaseColumn>().WithMany().HasForeignKey(r => new { r.OrganizationId, r.ToColumnId }).HasPrincipalKey(c => new { c.OrganizationId, c.ColumnId }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.ConstraintName).HasMaxLength(128).IsRequired();
        builder.HasIndex(r => new { r.ApplicationId, r.ConstraintName, r.FromColumnId }).IsUnique();
    }
}
