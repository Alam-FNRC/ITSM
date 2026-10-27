using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ItsTool.API.HostedServices;
using ItsTool.API.Hubs;
using ItsTool.Application.Interfaces;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Agents;
using ItsTool.Infrastructure.BackgroundServices;
using ItsTool.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ItsTool.UnitTests.Services;

public class BackgroundAndHostedServicesTests
{
    [Fact]
    public async Task SlaCheckerService_ShouldExecuteCheckBreaches_AndHandleExceptions()
    {
        var slaEngineMock = new Mock<ISlaEngine>();
        slaEngineMock.Setup(e => e.CheckBreachesAsync(It.IsAny<DateTime>()))
            .Returns(Task.CompletedTask);

        var serviceProviderMock = new Mock<IServiceProvider>();
        var scopeMock = new Mock<IServiceScope>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactoryMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ISlaEngine)))
            .Returns(slaEngineMock.Object);

        var loggerMock = new Mock<ILogger<SlaCheckerService>>();
        var service = new SlaCheckerService(serviceProviderMock.Object, loggerMock.Object);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50); // cancel quickly

        var ex = await Record.ExceptionAsync(async () =>
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(100);
            await service.StopAsync(CancellationToken.None);
        });

        Assert.Null(ex);
        slaEngineMock.Verify(e => e.CheckBreachesAsync(It.IsAny<DateTime>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SlaCheckerService_ShouldCatchAndLogException_WhenEngineFails()
    {
        var slaEngineMock = new Mock<ISlaEngine>();
        slaEngineMock.Setup(e => e.CheckBreachesAsync(It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("DB error in SLA checker"));

        var serviceProviderMock = new Mock<IServiceProvider>();
        var scopeMock = new Mock<IServiceScope>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactoryMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ISlaEngine)))
            .Returns(slaEngineMock.Object);

        var loggerMock = new Mock<ILogger<SlaCheckerService>>();
        var service = new SlaCheckerService(serviceProviderMock.Object, loggerMock.Object);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var ex = await Record.ExceptionAsync(async () =>
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(100);
            await service.StopAsync(CancellationToken.None);
        });

        Assert.Null(ex);
    }

    [Fact]
    public async Task EmailBackgroundService_ShouldDequeueAndSendEmail()
    {
        var emailQueueMock = new Mock<IEmailQueue>();
        var emailMsg = new EmailMessage { To = "user@test.com", Subject = "Subj", Body = "Body", IsHtml = true };
        
        var dequeuedOnce = false;
        emailQueueMock.Setup(q => q.DequeueEmailAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct =>
            {
                if (!dequeuedOnce)
                {
                    dequeuedOnce = true;
                    return emailMsg;
                }
                await Task.Delay(1000, ct);
                throw new OperationCanceledException();
            });

        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var serviceProviderMock = new Mock<IServiceProvider>();
        var scopeMock = new Mock<IServiceScope>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactoryMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IEmailService)))
            .Returns(emailServiceMock.Object);

        var loggerMock = new Mock<ILogger<EmailBackgroundService>>();
        var service = new EmailBackgroundService(emailQueueMock.Object, serviceProviderMock.Object, loggerMock.Object);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(80);

        await service.StartAsync(cts.Token);
        await Task.Delay(120);
        await service.StopAsync(CancellationToken.None);

        emailServiceMock.Verify(s => s.SendEmailAsync("user@test.com", "Subj", "Body", true), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AiAgentDispatcher_ShouldDispatchAssignedEvent()
    {
        var ran = new TaskCompletionSource();
        var copilotMock = new Mock<ResolutionCopilotAgent>(null!, null!, null!);
        copilotMock.Setup(c => c.RunAsync(It.IsAny<Ticket>())).Callback(() => ran.TrySetResult()).Returns(Task.CompletedTask);
        var (serviceProvider, _) = MockScopedServices(typeof(ResolutionCopilotAgent), copilotMock.Object);

        var dispatcher = new AiAgentDispatcher(serviceProvider, Mock.Of<ILogger<AiAgentDispatcher>>(), TestConfiguration.AiEnabled());

        await dispatcher.DispatchAsync("ticket.assigned", new Ticket { Id = 10, Title = "Test Ticket" });
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AiAgentDispatcher_ShouldDispatchTransferredEvent()
    {
        var ran = new TaskCompletionSource();
        var swarmMock = new Mock<TicketHandoffSwarm>(null!, null!, null!);
        swarmMock.Setup(s => s.RunAsync(It.IsAny<Ticket>())).Callback(() => ran.TrySetResult()).Returns(Task.CompletedTask);
        var (serviceProvider, _) = MockScopedServices(typeof(TicketHandoffSwarm), swarmMock.Object);

        var dispatcher = new AiAgentDispatcher(serviceProvider, Mock.Of<ILogger<AiAgentDispatcher>>(), TestConfiguration.AiEnabled());

        await dispatcher.DispatchAsync("ticket.transferred", new Ticket { Id = 11, Title = "Handoff Ticket" });
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AiAgentDispatcher_DoesNothingWhileAiIsTurnedOff()
    {
        var (serviceProvider, scopeFactory) = MockScopedServices(typeof(ResolutionCopilotAgent), new Mock<ResolutionCopilotAgent>(null!, null!, null!).Object);
        var aiOff = new ConfigurationBuilder().Build();

        var dispatcher = new AiAgentDispatcher(serviceProvider, Mock.Of<ILogger<AiAgentDispatcher>>(), aiOff);
        await dispatcher.DispatchAsync("ticket.assigned", new Ticket { Id = 12, Title = "Private ticket" });

        // The disabled path returns before starting any background work
        scopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    private static (IServiceProvider Provider, Mock<IServiceScopeFactory> ScopeFactory) MockScopedServices(Type serviceType, object service)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        var scopeMock = new Mock<IServiceScope>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();

        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactoryMock.Object);
        serviceProviderMock.Setup(sp => sp.GetService(serviceType)).Returns(service);

        return (serviceProviderMock.Object, scopeFactoryMock);
    }

    [Fact]
    public async Task NotificationHub_ShouldAddAndRemoveFromGroup()
    {
        var hub = new NotificationHub();
        var groupManagerMock = new Mock<IGroupManager>();
        groupManagerMock.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);
        groupManagerMock.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        var contextMock = new Mock<HubCallerContext>();
        contextMock.Setup(c => c.ConnectionId).Returns("conn_123");
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "99") };
        contextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims)));

        hub.Groups = groupManagerMock.Object;
        hub.Context = contextMock.Object;

        await hub.OnConnectedAsync();
        groupManagerMock.Verify(g => g.AddToGroupAsync("conn_123", "User_99", default), Times.Once);

        await hub.OnDisconnectedAsync(null);
        groupManagerMock.Verify(g => g.RemoveFromGroupAsync("conn_123", "User_99", default), Times.Once);
    }

    [Fact]
    public async Task NotificationHub_ShouldHandleMissingUserId()
    {
        var hub = new NotificationHub();
        var groupManagerMock = new Mock<IGroupManager>();

        var contextMock = new Mock<HubCallerContext>();
        contextMock.Setup(c => c.ConnectionId).Returns("conn_456");
        contextMock.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity()));

        hub.Groups = groupManagerMock.Object;
        hub.Context = contextMock.Object;

        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(new Exception("Lost connection"));

        groupManagerMock.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
        groupManagerMock.Verify(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }
}
