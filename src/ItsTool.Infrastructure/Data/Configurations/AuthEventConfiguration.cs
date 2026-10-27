using ItsTool.Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ItsTool.Infrastructure.Data.Configurations;
public class AuthEventConfiguration : IEntityTypeConfiguration<AuthEvent> {
    public void Configure(EntityTypeBuilder<AuthEvent> builder) {
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Username).IsRequired().HasMaxLength(256);
        builder.Property(e => e.FailureReason).HasMaxLength(50);
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasMaxLength(512);
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.UserId, e.OccurredAt });
    }
}
