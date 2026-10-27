using ItsTool.Application.DTOs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.KnowledgeBase;
using ItsTool.Infrastructure.Helpers;
using ItsTool.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ItsTool.IntegrationTests.Data;

/// <summary>
/// These queries used string.Contains/Equals with StringComparison, which EF Core cannot translate to SQL.
/// The in-memory unit tests ran them in C# and passed; against a real database they threw.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class QueryTranslationTests
{
    private readonly SqlServerFixture _fixture;

    public QueryTranslationTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Ticket_keyword_search_matches_number_title_and_description_ignoring_case()
    {
        var titleWord = TestData.Unique("Printer");
        var descriptionWord = TestData.Unique("Floor");
        await using var context = _fixture.CreateContext();
        var ticket = await TestData.CreateTicketAsync(context, $"{titleWord} offline", $"Paper jam on {descriptionWord}");

        Assert.Equal(new[] { ticket.Id }, await SearchTicketsAsync(titleWord.ToUpperInvariant()));
        Assert.Equal(new[] { ticket.Id }, await SearchTicketsAsync(descriptionWord.ToLowerInvariant()));
        Assert.Equal(new[] { ticket.Id }, await SearchTicketsAsync(ticket.TicketNumber.ToLowerInvariant()));
        Assert.Empty(await SearchTicketsAsync(TestData.Unique("nomatch")));
    }

    [Fact]
    public async Task Knowledge_base_search_matches_title_and_content_ignoring_case()
    {
        await using var context = _fixture.CreateContext();
        var author = await TestData.CreateUserAsync(context);
        var category = new KnowledgeCategory { Name = TestData.Unique("Network") };
        context.KnowledgeCategories.Add(category);
        await context.SaveChangesAsync();

        var titleWord = TestData.Unique("Vpn");
        var contentWord = TestData.Unique("Forti");
        var article = new KnowledgeArticle
        {
            CategoryId = category.Id,
            Title = $"Connecting to {titleWord}",
            Content = $"Open the {contentWord} client and sign in.",
            AuthorUserId = author.Id,
            Status = ArticleStatus.Published,
            Visibility = ArticleVisibility.Public
        };
        context.KnowledgeArticles.Add(article);
        await context.SaveChangesAsync();

        var permissions = new Mock<IPermissionCalculator>();
        permissions.Setup(p => p.CalculateEffectivePermissionsAsync(It.IsAny<int>())).ReturnsAsync(new HashSet<string>());
        var service = new KnowledgeBaseService(context, permissions.Object, Mock.Of<ISignalRPusher>());

        Assert.Contains(await service.SearchArticlesAsync(author.Id, titleWord.ToUpperInvariant(), null), a => a.Id == article.Id);
        Assert.Contains(await service.SearchArticlesAsync(author.Id, contentWord.ToLowerInvariant(), null), a => a.Id == article.Id);
    }

    [Fact]
    public async Task Email_ingestion_finds_the_existing_requester_ignoring_email_case()
    {
        await using var context = _fixture.CreateContext();
        var lookups = await TestData.CreateTicketLookupsAsync(context);
        var messageId = Guid.NewGuid().ToString();
        var email = new EmailIngestionDto(
            messageId, lookups.Requester.Email.ToUpperInvariant(), $"[{lookups.Project.ProjectKey}] Printer offline", "It shows an error.");

        await new EmailIngestionService(context).ProcessIncomingEmailAsync(email);

        await using var verify = _fixture.CreateContext();
        var ticket = await verify.Tickets.SingleAsync(t => t.ExternalMessageId == messageId);
        Assert.Equal(lookups.Requester.Id, ticket.RequesterUserId);
    }

    private async Task<int[]> SearchTicketsAsync(string keyword)
    {
        await using var context = _fixture.CreateContext();
        var query = TicketQueryHelpers.ApplyKeywordAndSlaFilters(context.Tickets, new TicketSearchFilterDto { Keyword = keyword });
        return await query.Select(t => t.Id).ToArrayAsync();
    }
}
