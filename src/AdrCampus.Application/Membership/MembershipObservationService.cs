using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Membership;

namespace AdrCampus.Application.Membership;

public sealed class MembershipObservationService(IMembershipRepository repository, IDirectoryRosterSource directory, TimeProvider timeProvider)
{
    private const string Source = "SSO observation";

    public async Task<MembershipSyncResult> SynchronizeAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        var snapshot = await directory.GetCurrentAsync(organizationId, cancellationToken);
        if (!snapshot.IsAvailable)
        {
            return MembershipSyncResult.Unavailable(snapshot.ErrorMessage ?? "The member directory is unavailable.");
        }

        var persisted = await repository.ListAsync(organizationId, cancellationToken);
        var persistedByMember = persisted.ToDictionary(p => p.MemberId.Value, p => p, StringComparer.Ordinal);
        var now = timeProvider.GetUtcNow();
        var transitions = new List<MembershipTransition>();

        foreach (var entry in snapshot.Members)
        {
            var role = entry.IsMaintainer ? MemberRole.Maintainer : MemberRole.Member;
            var name = entry.DisplayName.Trim();
            persistedByMember.TryGetValue(entry.MemberId.Value, out var current);

            if (current is null)
            {
                var next = MembershipProjection.Observe(organizationId, entry.MemberId, role, name, now);
                var evt = new AdministrationEvent(Guid.NewGuid(), organizationId, AdministrationEventType.MemberAdded, now, Source, SubjectId: entry.MemberId, NewValue: role.ToString());
                var write = await repository.ApplyAsync(next, null, evt, cancellationToken);
                Record(write, entry.MemberId, AdministrationEventType.MemberAdded, transitions);
                continue;
            }

            if (current.HasSameObservedState(role, name))
            {
                continue;
            }

            var working = current;
            if (working.Role != role)
            {
                var type = RoleEventType(working.Role, role);
                var next = working.Transition(role, working.DisplayName, now);
                var evt = new AdministrationEvent(Guid.NewGuid(), organizationId, type, now, Source, SubjectId: entry.MemberId, PreviousValue: working.Role.ToString(), NewValue: role.ToString());
                var write = await repository.ApplyAsync(next, working.Version, evt, cancellationToken);
                Record(write, entry.MemberId, type, transitions);
                if (write.Status == MembershipWriteStatus.Conflict) continue;
                working = write.State ?? next;
            }

            if (working.DisplayName != name)
            {
                var next = working.Transition(working.Role, name, now);
                var evt = new AdministrationEvent(Guid.NewGuid(), organizationId, AdministrationEventType.MemberDisplayNameChanged, now, Source, SubjectId: entry.MemberId, PreviousValue: working.DisplayName, NewValue: name);
                var write = await repository.ApplyAsync(next, working.Version, evt, cancellationToken);
                Record(write, entry.MemberId, AdministrationEventType.MemberDisplayNameChanged, transitions);
            }
        }

        var snapshotMemberIds = snapshot.Members.Select(m => m.MemberId.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var current in persisted)
        {
            if (current.Role == MemberRole.None || snapshotMemberIds.Contains(current.MemberId.Value))
            {
                continue;
            }

            var next = current.Transition(MemberRole.None, current.DisplayName, now);
            var evt = new AdministrationEvent(Guid.NewGuid(), organizationId, AdministrationEventType.MemberRemoved, now, Source, SubjectId: current.MemberId, PreviousValue: current.Role.ToString(), NewValue: MemberRole.None.ToString());
            var write = await repository.ApplyAsync(next, current.Version, evt, cancellationToken);
            Record(write, current.MemberId, AdministrationEventType.MemberRemoved, transitions);
        }

        return MembershipSyncResult.Success(snapshot, transitions);
    }

    private static void Record(MembershipWriteResult write, MemberId memberId, AdministrationEventType type, List<MembershipTransition> transitions)
    {
        if (write.Status == MembershipWriteStatus.Applied)
        {
            transitions.Add(new(memberId, type));
        }
    }

    private static AdministrationEventType RoleEventType(MemberRole from, MemberRole to) => to switch
    {
        MemberRole.None => AdministrationEventType.MemberRemoved,
        _ when from == MemberRole.None => AdministrationEventType.MemberAdded,
        MemberRole.Maintainer => AdministrationEventType.MaintainerGranted,
        _ => AdministrationEventType.MaintainerRevoked
    };
}

public sealed record MembershipTransition(MemberId MemberId, AdministrationEventType Type);

public sealed record MembershipSyncResult(bool IsAvailable, DirectoryRosterSnapshot? Snapshot, IReadOnlyList<MembershipTransition> Transitions, string? ErrorMessage)
{
    public bool HasActiveMaintainer => Snapshot?.HasActiveMaintainer ?? false;
    public static MembershipSyncResult Success(DirectoryRosterSnapshot snapshot, IReadOnlyList<MembershipTransition> transitions) => new(true, snapshot, transitions, null);
    public static MembershipSyncResult Unavailable(string errorMessage) => new(false, null, Array.Empty<MembershipTransition>(), errorMessage);
}
