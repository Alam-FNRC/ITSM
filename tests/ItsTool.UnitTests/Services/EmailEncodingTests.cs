using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Organization;
using ItsTool.Domain.Entities.SLA;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ItsTool.UnitTests.Services;

// A requester chooses the ticket title, so it must reach emails as text, never as markup
public class EmailEncodingTests : TestBase
{
    private const string HostileTitle = "Printer <a href=\"https://evil.example\">reset your password</a>";

    [Theory]
    [InlineData("ticket.created")]
    [InlineData("sla.breached")]
    public void TemplateValues_AreEncoded(string eventKey)
    {
        var service = new EmailTemplateService(Mock.Of<ILogger<EmailTemplateService>>());
        var data = new Dictionary<string, string>
        {
            { "EventName", "Ticket Created" },
            { "TicketNumber", "GEN-1" },
            { "Title", HostileTitle },
            { "Context", HostileTitle },
            { "AppUrl", "https://itsm.fnrc.test/ticket-detail.html?id=1" }
        };

        var html = service.GenerateEmailBody(eventKey, data);

        Assert.DoesNotContain("<a href=\"https://evil.example\"", html);
        Assert.Contains("&lt;a href=&quot;https://evil.example&quot;&gt;", html);
    }

    [Fact]
    public async Task SlaWarningEmail_EncodesTheTicketTitle()
    {
        var body = await SendSlaWarningAsync(null);

        Assert.DoesNotContain("<a href=\"https://evil.example\"", body);
        Assert.Contains("&lt;a href=&quot;https://evil.example&quot;&gt;", body);
    }

    [Fact]
    public async Task SlaWarningEmail_LinksToTheConfiguredSite()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = "https://itsm.fnrc.test/" })
            .Build();

        var body = await SendSlaWarningAsync(config);

        Assert.Contains("href=\"https://itsm.fnrc.test/ticket-detail.html?id=1\"", body);
        Assert.DoesNotContain("localhost", body);
    }

    private async Task<string> SendSlaWarningAsync(IConfiguration? config)
    {
        _context.Users.Add(new User { Id = 1, Username = "agent", Email = "agent@fnrc.test", PasswordHash = "x", IsActive = true });
        _context.Tickets.Add(new Ticket
        {
            Id = 1,
            TicketNumber = "GEN-1",
            Title = HostileTitle,
            Assignments = new List<TicketAssignment> { new TicketAssignment { AssignedUserId = 1, IsActive = true } }
        });
        var now = DateTime.UtcNow;
        _context.TicketSlas.Add(new TicketSla { TicketId = 1, CreatedAt = now.AddMinutes(-110), FirstResponseDueAt = now.AddMinutes(10) });
        await _context.SaveChangesAsync();

        string? body = null;
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, string, string, bool>((_, _, b, _) => body = b)
            .Returns(Task.CompletedTask);

        await new SlaEngine(_context, email.Object, Mock.Of<INotificationDispatcher>(), config).CheckBreachesAsync(now);

        Assert.NotNull(body);
        return body!;
    }
}
