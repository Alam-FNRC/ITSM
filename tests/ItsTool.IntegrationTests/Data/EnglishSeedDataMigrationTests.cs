using ItsTool.Domain.Entities.Auth;
using ItsTool.Domain.Entities.KnowledgeBase;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.Ticket;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ItsTool.IntegrationTests.Data;

// Databases seeded before 3 Nov 2026 hold Turkish names; the EnglishSeedData migration renames them
public class EnglishSeedDataMigrationTests
{
    private const string BeforeEnglish = "20261102130716_AddRiskRegister";
    private const string English = "20261103120841_EnglishSeedData";

    private const string TurkishChecklist = "DİKKAT: Bu doküman sadece IT personeli içindir.\n\nAylık sunucu bakımlarında kontrol edilmesi gereken metrikler:\n- CPU ve RAM kullanım trendleri\n- Disk doluluk oranları (>%80 ise uyarı)\n- Güvenlik yamalarının (patch) durumu\n- Backup operasyonlarının son durumu (Success/Failure logları)";
    private const string EnglishChecklist = "NOTE: This document is for IT staff only.\n\nMetrics to check during monthly server maintenance:\n- CPU and RAM usage trends\n- Disk usage (warn above 80%)\n- Security patch status\n- Latest status of backup jobs (success/failure logs)";

    [Fact]
    public async Task Old_turkish_seed_data_becomes_english_and_back()
    {
        // Its own database, so the shared one stays at the latest migration
        var fixture = new SqlServerFixture();
        try
        {
            await using (var context = fixture.CreateContext())
            {
                await context.GetService<IMigrator>().MigrateAsync(BeforeEnglish);

                var department = new Department { Name = "Bilgi Teknolojileri" };
                var author = new User { Username = "author", Email = "author@fnrc.test", PasswordHash = "x", FirstName = "Kb", LastName = "Author" };
                var kbCategory = new KnowledgeCategory { Name = "Rehberler" };
                context.AddRange(department, author, kbCategory,
                    new Status { Name = "Açık", SortOrder = 1 },
                    new Priority { Name = "Orta", SeverityLevel = 3 },
                    // Someone already added the English name, so the Turkish one is left as it is
                    new Priority { Name = "Medium", SeverityLevel = 3 },
                    new Permission { Key = "ticket.edit", Name = "ticket.edit", Description = "Bilet detaylarını düzenleme yetkisi sağlar." });
                await context.SaveChangesAsync();
                context.AddRange(
                    new Group { Name = "Yazılım Destek Ekibi", DepartmentId = department.Id },
                    new KnowledgeArticle { Title = "Sunucu Bakım Kontrol Listesi", Content = TurkishChecklist, CategoryId = kbCategory.Id, AuthorUserId = author.Id },
                    // An article someone has since edited keeps their text
                    new KnowledgeArticle { Title = "Own article", Content = TurkishChecklist + " (edited)", CategoryId = kbCategory.Id, AuthorUserId = author.Id });
                await context.SaveChangesAsync();

                await context.GetService<IMigrator>().MigrateAsync(English);
            }

            await using (var context = fixture.CreateContext())
            {
                Assert.Equal("Information Technology", (await context.Departments.SingleAsync()).Name);
                Assert.Equal("Software Support Team", (await context.Groups.SingleAsync()).Name);
                Assert.Equal("Open", (await context.Statuses.SingleAsync()).Name);
                Assert.Equal(new[] { "Medium", "Orta" }, await context.Priorities.Select(p => p.Name).OrderBy(n => n).ToListAsync());
                Assert.Equal("Guides", (await context.KnowledgeCategories.SingleAsync()).Name);
                var articles = await context.KnowledgeArticles.OrderBy(a => a.Id).ToListAsync();
                Assert.Equal(("Server Maintenance Checklist", EnglishChecklist), (articles[0].Title, articles[0].Content));
                Assert.Equal(TurkishChecklist + " (edited)", articles[1].Content);
                // The seeder writes the English description when the app starts
                Assert.Equal("", (await context.Permissions.SingleAsync()).Description);

                var audit = await context.SystemAuditLogs.Where(a => a.CreatedBy == "system").ToListAsync();
                Assert.Contains(audit, a => a is { EntityType: "Group", Action: "Updated", FieldName: "Name", OldValue: "Yazılım Destek Ekibi", NewValue: "Software Support Team" });
                Assert.Contains(audit, a => a is { EntityType: "Permission", FieldName: "Description", OldValue: "Bilet detaylarını düzenleme yetkisi sağlar." });
                Assert.DoesNotContain(audit, a => a.OldValue == "Orta");

                await context.GetService<IMigrator>().MigrateAsync(BeforeEnglish);
            }

            await using (var context = fixture.CreateContext())
            {
                Assert.Equal("Yazılım Destek Ekibi", (await context.Groups.SingleAsync()).Name);
                Assert.Equal("Açık", (await context.Statuses.SingleAsync()).Name);
                Assert.Equal(TurkishChecklist, (await context.KnowledgeArticles.OrderBy(a => a.Id).FirstAsync()).Content);
            }
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
