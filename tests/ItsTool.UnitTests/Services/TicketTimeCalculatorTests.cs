using ItsTool.Domain.Entities.SLA;
using ItsTool.Domain.Entities.Ticket;
using ItsTool.Infrastructure.Services;
using static ItsTool.Infrastructure.Services.TicketTimeCalculator;

namespace ItsTool.UnitTests.Services;

public class TicketTimeCalculatorTests
{
    private const int Open = 1, InProgress = 2, OnHold = 3, Resolved = 4, Closed = 5;
    private const int Requester = 1, Agent = 10, GroupMember = 20, Group = 7;

    private static readonly Dictionary<int, StatusFlags> Statuses = new()
    {
        [Open] = new(false, false),
        [InProgress] = new(false, false),
        [OnHold] = new(false, true),
        [Resolved] = new(true, false),
        [Closed] = new(true, false)
    };

    // Monday to Friday 07:30-15:30 UAE time; the ticket is opened on Monday 2 November 2026 at 08:00 UAE time
    private static readonly BusinessCalendar Calendar = new(
        Enumerable.Range(1, 5).Select(d => new BusinessHour { DayOfWeek = (DayOfWeek)d, StartTime = new TimeSpan(7, 30, 0), EndTime = new TimeSpan(15, 30, 0), IsWorkingDay = true }),
        Array.Empty<Holiday>(),
        BusinessCalendar.ResolveTimeZone(null));

    private static readonly Ticket OpenedMonday = new() { Id = 5, CreatedAt = Utc(2, 4), StatusId = Open };

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    private static TicketHistory Event(string action, DateTime at, object by, string? field = null, string? oldValue = null, string? newValue = null) =>
        new() { TicketId = 5, Action = action, CreatedAt = at, CreatedBy = by.ToString(), FieldName = field ?? "", OldValue = oldValue, NewValue = newValue };

    private static TicketHistory Status(DateTime at, int from, int to, object? by = null) =>
        Event(from is Resolved or Closed && to is not (Resolved or Closed) ? "Reopened" : "StatusChanged", at, by ?? Requester, "StatusId", from.ToString(), to.ToString());

    private static TicketTimeMeasure Calculate(IEnumerable<TicketHistory> history, Ticket? ticket = null, params Assignment[] assignments) =>
        TicketTimeCalculator.Calculate(ticket ?? OpenedMonday, history, Statuses, assignments,
            new Dictionary<int, HashSet<int>> { [Group] = new() { GroupMember } }, Calendar);

    [Fact]
    public void The_ticket_is_owned_at_the_first_action_of_an_assigned_agent()
    {
        var times = Calculate(new[]
        {
            Event("CommentAdded", Utc(2, 4, 30), Requester),
            Event("CommentAdded", Utc(2, 6), Agent)
        }, null, new Assignment(Agent, null, Utc(2, 5)));

        Assert.Equal((Utc(2, 6), Agent, 120), (times.OwnedAt, times.OwnedByUserId, times.TimeToOwnMinutes));
    }

    [Fact]
    public void Actions_before_the_assignment_do_not_count()
    {
        var times = Calculate(new[]
        {
            Event("CommentAdded", Utc(2, 4, 30), Agent),
            Status(Utc(2, 5, 30), Open, InProgress, Agent)
        }, null, new Assignment(Agent, null, Utc(2, 5)));

        Assert.Equal(Utc(2, 5, 30), times.OwnedAt);
    }

    [Fact]
    public void Members_of_an_assigned_group_own_the_ticket()
    {
        var times = Calculate(new[] { Status(Utc(2, 4, 40), Open, InProgress, GroupMember) }, null, new Assignment(null, Group, Utc(2, 4, 10)));

        Assert.Equal((GroupMember, 40), (times.OwnedByUserId, times.TimeToOwnMinutes));
    }

    [Fact]
    public void Taking_a_ticket_counts_as_owning_it()
    {
        var times = Calculate(new[] { Event("Assigned", Utc(2, 5), Agent, "Assignments", "Multiple", $"Users:{Agent},Groups:") }, null, new Assignment(Agent, null, Utc(2, 5)));

        Assert.Equal(Utc(2, 5), times.OwnedAt);
    }

    [Fact]
    public void Mentions_comment_edits_and_system_actions_do_not_own_the_ticket()
    {
        var times = Calculate(new[]
        {
            Event("UserMentioned", Utc(2, 5), Agent),
            Event("CommentEdited", Utc(2, 6), Agent),
            Event("Assigned", Utc(2, 7), "System")
        }, null, new Assignment(Agent, null, Utc(2, 4, 30)));

        Assert.Null(times.OwnedAt);
        Assert.Null(times.TimeToOwnMinutes);
    }

    [Fact]
    public void Waiting_time_is_left_out_of_the_time_to_resolve()
    {
        var times = Calculate(new[]
        {
            Status(Utc(2, 5), Open, OnHold),        // Monday 09:00 UAE
            Status(Utc(3, 5), OnHold, InProgress),  // Tuesday 09:00 UAE: 390 + 90 working minutes on hold
            Status(Utc(3, 7), InProgress, Resolved) // Tuesday 11:00 UAE: 450 + 210 working minutes since opening
        });

        Assert.Equal(480, times.WaitingMinutes);
        Assert.Null(times.WaitingSince);
        Assert.Equal(Utc(3, 7), times.ResolvedAt);
        Assert.Equal(180, times.TimeToResolveMinutes);
    }

    [Fact]
    public void A_wait_in_progress_is_marked_but_not_yet_counted()
    {
        var times = Calculate(new[] { Status(Utc(2, 5), Open, OnHold) });

        Assert.Equal(0, times.WaitingMinutes);
        Assert.Equal(Utc(2, 5), times.WaitingSince);
    }

    [Fact]
    public void Reopening_clears_the_resolution_and_is_counted()
    {
        var reopened = new[]
        {
            Status(Utc(2, 6), Open, Resolved),
            Status(Utc(2, 7), Resolved, Closed),
            Status(Utc(2, 8), Closed, Open)
        };
        var times = Calculate(reopened);
        Assert.Equal((1, (DateTime?)null, (int?)null), (times.ReopenCount, times.ResolvedAt, times.TimeToResolveMinutes));

        // Solved again: the time to resolve runs to the second resolution
        times = Calculate(reopened.Append(Status(Utc(2, 9), Open, Resolved)));
        Assert.Equal((1, Utc(2, 9), 300), (times.ReopenCount, times.ResolvedAt, times.TimeToResolveMinutes));
    }

    [Fact]
    public void Moving_from_resolved_to_closed_keeps_the_resolution_time()
    {
        var times = Calculate(new[] { Status(Utc(2, 6), Open, Resolved), Status(Utc(4, 6), Resolved, Closed) });

        Assert.Equal((Utc(2, 6), 120, 0), (times.ResolvedAt, times.TimeToResolveMinutes, times.ReopenCount));
    }

    [Fact]
    public void Assignments_and_transfers_to_a_group_are_counted()
    {
        var times = Calculate(new[]
        {
            Event("Assigned", Utc(2, 5), "System", "Assignments", "-", "Users:,Groups:7"),
            Event("Assigned", Utc(2, 6), Agent, "Assignments", "Multiple", "Users:10,Groups:"),
            Event("Transferred", Utc(2, 7), Agent, "Transfer", "Proj:1,Grp:-", "Proj:2,Grp:5"),
            Event("Transferred", Utc(2, 8), Agent, "Transfer", "Proj:2,Grp:-", "Proj:3,Grp:"),
            Event("Transferred", Utc(2, 9), Agent, "Transfer", "Proj:3,Grp:-", "Proj:4,Grp:-1")
        });

        Assert.Equal(3, times.AssignmentCount);
    }

    [Fact]
    public void Events_are_replayed_in_time_order()
    {
        // History arrives unsorted from the database
        var times = Calculate(new[] { Status(Utc(2, 7), InProgress, Resolved), Status(Utc(2, 5), Open, InProgress) });

        Assert.Equal(Utc(2, 7), times.ResolvedAt);
    }
}
