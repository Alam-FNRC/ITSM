using ItsTool.Domain.Entities.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ItsTool.Infrastructure.Data.Configurations;

// Incidents are never deleted, and their history must not go with them if one were, so nothing cascades

public class SecurityIncidentConfiguration : IEntityTypeConfiguration<SecurityIncident> {
    public void Configure(EntityTypeBuilder<SecurityIncident> builder) {
        builder.Property(i => i.Reference).IsRequired().HasMaxLength(30);
        builder.HasIndex(i => i.Reference).IsUnique();
        builder.Property(i => i.Title).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Description).IsRequired();
        builder.Property(i => i.TriageReason).HasMaxLength(2000);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.ReporterUserId);
    }
}

public class SecurityIncidentActionConfiguration : IEntityTypeConfiguration<SecurityIncidentAction> {
    public void Configure(EntityTypeBuilder<SecurityIncidentAction> builder) {
        builder.Property(a => a.Kind).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Field).HasMaxLength(50);
        builder.HasOne(a => a.Incident).WithMany(i => i.Actions).HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SecurityIncidentMemberConfiguration : IEntityTypeConfiguration<SecurityIncidentMember> {
    public void Configure(EntityTypeBuilder<SecurityIncidentMember> builder) {
        builder.HasOne(m => m.Incident).WithMany(i => i.Members).HasForeignKey(m => m.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => new { m.IncidentId, m.UserId });
        builder.HasIndex(m => m.UserId);
    }
}

public class SecurityIncidentAccessConfiguration : IEntityTypeConfiguration<SecurityIncidentAccess> {
    public void Configure(EntityTypeBuilder<SecurityIncidentAccess> builder) {
        builder.HasOne<SecurityIncident>().WithMany().HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SecurityAffectedItemConfiguration : IEntityTypeConfiguration<SecurityAffectedItem> {
    public void Configure(EntityTypeBuilder<SecurityAffectedItem> builder) {
        builder.Property(a => a.Name).IsRequired().HasMaxLength(300);
        builder.Property(a => a.StatusReason).HasMaxLength(2000);
        builder.HasOne(a => a.Incident).WithMany(i => i.AffectedItems).HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Restrict);
    }
}
