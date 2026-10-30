using ItsTool.Domain.Entities.Ticket;

namespace ItsTool.Infrastructure.Services;

/// <summary>
/// Works out a ticket's time measures (feature spec, "Time measures stored on every ticket") by replaying its
/// history, so live tickets, a recalculation and imported tickets all follow the same rules.
/// </summary>
public static class TicketTimeCalculator
{
    // Actions that show someone working on the ticket; edits and deletions of comments, and mentions, do not count
    private static readonly HashSet<string> WorkActions = new()
    {
        "StatusChanged", "Reopened", "Updated", "Assigned", "Transferred", "CommentAdded", "CommentReplied", "InternalNoteAdded", "AttachmentAdded"
    };

    public sealed record StatusFlags(bool IsClosed, bool PausesSla);

    public sealed record Assignment(int? UserId, int? GroupId, DateTime CreatedAt);

    public static TicketTimeMeasure Calculate(
        Ticket ticket,
        IEnumerable<TicketHistory> history,
        IReadOnlyDictionary<int, StatusFlags> statuses,
        IReadOnlyCollection<Assignment> assignments,
        IReadOnlyDictionary<int, HashSet<int>> groupMembers,
        BusinessCalendar calendar)
    {
        var events = history.Where(h => !h.IsDeleted).OrderBy(h => h.CreatedAt).ThenBy(h => h.Id).ToList();
        var opened = ticket.CreatedAt;
        var result = new TicketTimeMeasure { TicketId = ticket.Id };

        ReplayStatuses(ticket, events, statuses, calendar, result);

        foreach (var e in events)
        {
            if (!WorkActions.Contains(e.Action) || !int.TryParse(e.CreatedBy, out var userId)) continue;
            if (!WasAssigned(userId, e.CreatedAt, assignments, groupMembers)) continue;

            result.OwnedAt = Later(e.CreatedAt, opened);
            result.OwnedByUserId = userId;
            result.TimeToOwnMinutes = calendar.WorkingMinutesBetween(opened, result.OwnedAt.Value);
            break;
        }

        result.AssignmentCount = events.Count(e => e.Action == "Assigned" || (e.Action == "Transferred" && TransferAssignedGroup(e.NewValue)));
        return result;
    }

    private static void ReplayStatuses(
        Ticket ticket, List<TicketHistory> events, IReadOnlyDictionary<int, StatusFlags> statuses, BusinessCalendar calendar, TicketTimeMeasure result)
    {
        var changes = events.Where(e => (e.Action == "StatusChanged" || e.Action == "Reopened") && e.FieldName == "StatusId").ToList();
        StatusFlags Flags(int id) => statuses.TryGetValue(id, out var f) ? f : new StatusFlags(false, false);

        // The status a ticket was opened with is the "from" side of its first change
        var current = changes.Count > 0 && int.TryParse(changes[0].OldValue, out var first) ? first : ticket.StatusId;
        var since = ticket.CreatedAt;
        var waiting = 0;
        var waitingBeforeResolve = 0;

        foreach (var change in changes)
        {
            if (!int.TryParse(change.NewValue, out var next)) continue;
            var at = Later(change.CreatedAt, ticket.CreatedAt);
            var from = Flags(current);
            var to = Flags(next);

            if (from.PausesSla && !from.IsClosed) waiting += calendar.WorkingMinutesBetween(since, at);
            if (!from.IsClosed && to.IsClosed)
            {
                result.ResolvedAt = at;
                waitingBeforeResolve = waiting;
            }
            else if (from.IsClosed && !to.IsClosed)
            {
                result.ReopenCount++;
                result.ResolvedAt = null;
            }

            current = next;
            since = at;
        }

        result.WaitingMinutes = waiting;
        var now = Flags(current);
        result.WaitingSince = now.PausesSla && !now.IsClosed ? since : null;
        if (result.ResolvedAt.HasValue)
        {
            var open = calendar.WorkingMinutesBetween(ticket.CreatedAt, result.ResolvedAt.Value);
            result.TimeToResolveMinutes = Math.Max(0, open - waitingBeforeResolve);
        }
    }

    // Assigned directly or through a group at the time of the action; the group's current members are used
    private static bool WasAssigned(int userId, DateTime at, IReadOnlyCollection<Assignment> assignments, IReadOnlyDictionary<int, HashSet<int>> groupMembers) =>
        assignments.Any(a => a.CreatedAt <= at &&
            (a.UserId == userId || (a.GroupId.HasValue && groupMembers.TryGetValue(a.GroupId.Value, out var members) && members.Contains(userId))));

    // TicketService writes "Proj:{id},Grp:{id}" and only assigns when a group other than -1 is given
    private static bool TransferAssignedGroup(string? newValue)
    {
        var marker = newValue?.IndexOf("Grp:", StringComparison.Ordinal) ?? -1;
        return marker >= 0 && int.TryParse(newValue![(marker + 4)..], out var groupId) && groupId != -1;
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;
}
