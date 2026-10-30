using ItsTool.Domain.Entities.Ticket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ItsTool.Infrastructure.Data.Configurations;
public class TicketTimeMeasureConfiguration : IEntityTypeConfiguration<TicketTimeMeasure> {
    public void Configure(EntityTypeBuilder<TicketTimeMeasure> builder) {
        builder.HasOne(m => m.Ticket)
               .WithOne(t => t.TimeMeasure)
               .HasForeignKey<TicketTimeMeasure>(m => m.TicketId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.TicketId).IsUnique();
        // KPI reports select by resolution date
        builder.HasIndex(m => m.ResolvedAt);
    }
}
