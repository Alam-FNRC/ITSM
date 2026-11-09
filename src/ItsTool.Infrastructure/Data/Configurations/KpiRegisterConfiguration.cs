using ItsTool.Domain.Entities.Kpis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ItsTool.Infrastructure.Data.Configurations;

// KPIs are soft-deleted, so nothing cascades; values and targets stay with their KPI

public class KpiCapabilityConfiguration : IEntityTypeConfiguration<KpiCapability>
{
    public void Configure(EntityTypeBuilder<KpiCapability> builder)
    {
        builder.Property(c => c.NameEn).IsRequired().HasMaxLength(200);
        builder.Property(c => c.NameAr).IsRequired().HasMaxLength(200);
        builder.HasIndex(c => c.Number);
    }
}

public class KpiDefinitionConfiguration : IEntityTypeConfiguration<KpiDefinition>
{
    public void Configure(EntityTypeBuilder<KpiDefinition> builder)
    {
        builder.Property(k => k.NameEn).IsRequired().HasMaxLength(300);
        builder.Property(k => k.NameAr).IsRequired().HasMaxLength(300);
        builder.Property(k => k.Description).HasMaxLength(2000);
        builder.Property(k => k.Formula).HasMaxLength(1000);
        builder.Property(k => k.DataSource).HasMaxLength(500);
        builder.Property(k => k.Scope).HasMaxLength(100);
        builder.Property(k => k.Unit).HasMaxLength(100);
        builder.HasIndex(k => k.Number);
        builder.HasIndex(k => k.OwnerUserId);
        builder.HasOne(k => k.Capability).WithMany(c => c.Kpis).HasForeignKey(k => k.CapabilityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class KpiValueConfiguration : IEntityTypeConfiguration<KpiValue>
{
    public void Configure(EntityTypeBuilder<KpiValue> builder)
    {
        builder.Property(v => v.Value).HasPrecision(18, 4);
        builder.Property(v => v.Note).HasMaxLength(2000);
        builder.HasIndex(v => new { v.KpiId, v.Year, v.Cycle, v.Period }).IsUnique();
        builder.HasOne(v => v.Kpi).WithMany(k => k.Values).HasForeignKey(v => v.KpiId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class KpiTargetConfiguration : IEntityTypeConfiguration<KpiTarget>
{
    public void Configure(EntityTypeBuilder<KpiTarget> builder)
    {
        builder.Property(t => t.Value).HasPrecision(18, 4);
        builder.HasIndex(t => new { t.KpiId, t.Year }).IsUnique();
        builder.HasOne(t => t.Kpi).WithMany(k => k.Targets).HasForeignKey(t => t.KpiId).OnDelete(DeleteBehavior.Restrict);
    }
}
