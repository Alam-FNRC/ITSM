using ItsTool.Domain.Entities.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ItsTool.Infrastructure.Data.Configurations;
public class UserConfiguration : IEntityTypeConfiguration<User> {
    public void Configure(EntityTypeBuilder<User> builder) {
        // Login matches either column exactly, so each must identify one current user (review A9).
        // Comparison follows the database collation, which is case-insensitive on SQL Server by default.
        // Users created from e-mail use the address as username, hence the same length.
        builder.Property(u => u.Username).IsRequired().HasMaxLength(256);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Username).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
