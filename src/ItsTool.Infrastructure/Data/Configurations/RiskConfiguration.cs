using ItsTool.Domain.Entities.Risks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ItsTool.Infrastructure.Data.Configurations;

// Risks are never deleted, and their history must not go with them if one were, so nothing cascades

public class RiskConfiguration : IEntityTypeConfiguration<Risk> {
    public void Configure(EntityTypeBuilder<Risk> builder) {
        builder.HasIndex(r => r.Number).IsUnique();
        builder.Property(r => r.Title).IsRequired().HasMaxLength(300);
        builder.Property(r => r.Threat).HasMaxLength(1000);
        builder.Property(r => r.Vulnerability).HasMaxLength(2000);
        builder.Property(r => r.ExistingControls).HasMaxLength(2000);
        builder.Property(r => r.RecommendedControls).HasMaxLength(2000);
        builder.Property(r => r.AnnexAControls).HasMaxLength(300);
        builder.HasIndex(r => r.OwnerUserId);
    }
}

public class RiskHistoryConfiguration : IEntityTypeConfiguration<RiskHistory> {
    public void Configure(EntityTypeBuilder<RiskHistory> builder) {
        builder.Property(h => h.Kind).IsRequired().HasMaxLength(50);
        builder.Property(h => h.Field).HasMaxLength(50);
        builder.Property(h => h.OldValue).HasMaxLength(2000);
        builder.Property(h => h.NewValue).HasMaxLength(2000);
        builder.Property(h => h.Note).HasMaxLength(2000);
        builder.HasOne(h => h.Risk).WithMany(r => r.History).HasForeignKey(h => h.RiskId).OnDelete(DeleteBehavior.Restrict);
    }
}
