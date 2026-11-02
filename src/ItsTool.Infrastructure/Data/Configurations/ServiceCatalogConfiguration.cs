using ItsTool.Domain.Entities.Catalog;
using ItsTool.Domain.Entities.Ticket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ItsTool.Infrastructure.Data.Configurations;

public class ItServiceConfiguration : IEntityTypeConfiguration<ItService>
{
    public void Configure(EntityTypeBuilder<ItService> builder)
    {
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Family).HasMaxLength(100);
        builder.Property(s => s.Description).HasMaxLength(1000);
        builder.HasOne(s => s.OwnerGroup).WithMany().HasForeignKey(s => s.OwnerGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasOne(c => c.Service).WithMany(s => s.Categories).HasForeignKey(c => c.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.DefaultType).WithMany().HasForeignKey(c => c.DefaultTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
