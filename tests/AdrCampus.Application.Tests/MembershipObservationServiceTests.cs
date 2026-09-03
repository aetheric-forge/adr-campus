using AdrCampus.Application.Membership;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Membership;

namespace AdrCampus.Application.Tests;

public sealed class MembershipObservationServiceTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Ada = new("ada");
    private static readonly MemberId Grace = new("grace");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewMemberProducesExactlyOneAddedEvent()
    {
        var repository = new StubRepository();
        var service = Create(repository, Snapshot((Ada, "Ada Lovelace", false)));
        var result = await service.SynchronizeAsync(Organization);
        Assert.True(result.IsAvailable);
        Assert.Single(repository.Events);
        Assert.Equal(AdministrationEventType.MemberAdded, repository.Events[0].Type);
        Assert.Equal(Ada, repository.Events[0].SubjectId);
    }

    [Fact]
    public async Task PromotionAndDemotionEachProduceOneEvent()
    {
        var repository = new StubRepository();
        await Create(repository, Snapshot((Ada, "Ada Lovelace", false))).SynchronizeAsync(Organization);
        await Create(repository, Snapshot((Ada, "Ada Lovelace", true))).SynchronizeAsync(Organization);
        await Create(repository, Snapshot((Ada, "Ada Lovelace", false))).SynchronizeAsync(Organization);
        Assert.Equal([AdministrationEventType.MemberAdded, AdministrationEventType.MaintainerGranted, AdministrationEventType.MaintainerRevoked], repository.Events.Select(e => e.Type));
    }

    [Fact]
    public async Task RemovalProducesExactlyOneEvent()
    {
        var repository = new StubRepository();
        await Create(repository, Snapshot((Ada, "Ada Lovelace", false))).SynchronizeAsync(Organization);
        var result = await Create(repository, Snapshot()).SynchronizeAsync(Organization);
        Assert.True(result.IsAvailable);
        Assert.Equal([AdministrationEventType.MemberAdded, AdministrationEventType.MemberRemoved], repository.Events.Select(e => e.Type));
    }

    [Fact]
    public async Task DisplayNameChangeProducesExactlyOneEvent()
    {
        var repository = new StubRepository();
        await Create(repository, Snapshot((Ada, "Ada Lovelace", false))).SynchronizeAsync(Organization);
        var result = await Create(repository, Snapshot((Ada, "Ada L. Byron", false))).SynchronizeAsync(Organization);
        Assert.True(result.IsAvailable);
        Assert.Equal([AdministrationEventType.MemberAdded, AdministrationEventType.MemberDisplayNameChanged], repository.Events.Select(e => e.Type));
        Assert.Equal("Ada Lovelace", repository.Events[1].PreviousValue);
        Assert.Equal("Ada L. Byron", repository.Events[1].NewValue);
    }

    [Fact]
    public async Task ReplayingTheSameSnapshotProducesNoNewEvents()
    {
        var repository = new StubRepository();
        var snapshot = Snapshot((Ada, "Ada Lovelace", false), (Grace, "Grace Hopper", true));
        await Create(repository, snapshot).SynchronizeAsync(Organization);
        await Create(repository, snapshot).SynchronizeAsync(Organization);
        Assert.Equal(2, repository.Events.Count);
    }

    [Fact]
    public async Task UnavailableDirectoryLeavesPersistedStateUntouched()
    {
        var repository = new StubRepository();
        await Create(repository, Snapshot((Ada, "Ada Lovelace", false))).SynchronizeAsync(Organization);
        var directory = new StubDirectory(DirectoryRosterSnapshot.Unavailable("directory down"));
        var service = new MembershipObservationService(repository, directory, new FixedTimeProvider(Now.AddMinutes(1)));
        var result = await service.SynchronizeAsync(Organization);
        Assert.False(result.IsAvailable);
        Assert.Single(repository.Events);
        Assert.Single(await repository.ListAsync(Organization));
    }

    [Fact]
    public async Task NoMaintainerInSnapshotReportsUnavailableAuthorityWithoutPromotingAnyone()
    {
        var repository = new StubRepository();
        var result = await Create(repository, Snapshot((Ada, "Ada Lovelace", false), (Grace, "Grace Hopper", false))).SynchronizeAsync(Organization);
        Assert.True(result.IsAvailable);
        Assert.False(result.HasActiveMaintainer);
        Assert.DoesNotContain(repository.Events, e => e.Type is AdministrationEventType.MaintainerGranted);
    }

    private static MembershipObservationService Create(StubRepository repository, DirectoryRosterSnapshot snapshot) =>
        new(repository, new StubDirectory(snapshot), new FixedTimeProvider(Now));

    private static DirectoryRosterSnapshot Snapshot(params (MemberId Id, string Name, bool IsMaintainer)[] members) =>
        DirectoryRosterSnapshot.Success(members.Select(m => new DirectoryRosterEntry(m.Id, m.Name, m.IsMaintainer)).ToArray(), Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class StubDirectory(DirectoryRosterSnapshot snapshot) : IDirectoryRosterSource
    {
        public Task<DirectoryRosterSnapshot> GetCurrentAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class StubRepository : IMembershipRepository
    {
        private readonly Dictionary<string, MembershipProjection> members = [];
        public List<AdministrationEvent> Events { get; } = [];

        public Task<IReadOnlyList<MembershipProjection>> ListAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MembershipProjection>>(members.Values.Where(m => m.OrganizationId == organizationId).ToArray());

        public Task<MembershipWriteResult> ApplyAsync(MembershipProjection next, long? expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default)
        {
            members.TryGetValue(next.MemberId.Value, out var current);
            if (current is not null && current.Version == expectedVersion)
            {
                members[next.MemberId.Value] = next; Events.Add(administrationEvent);
                return Task.FromResult(new MembershipWriteResult(MembershipWriteStatus.Applied, next));
            }
            if (current is null && expectedVersion is null)
            {
                members[next.MemberId.Value] = next; Events.Add(administrationEvent);
                return Task.FromResult(new MembershipWriteResult(MembershipWriteStatus.Applied, next));
            }
            if (current is not null && current.Version == next.Version && current.HasSameObservedState(next.Role, next.DisplayName))
            {
                return Task.FromResult(new MembershipWriteResult(MembershipWriteStatus.AlreadyApplied, current));
            }
            return Task.FromResult(new MembershipWriteResult(MembershipWriteStatus.Conflict, current));
        }

        public Task<IReadOnlyList<AdministrationEvent>> ListEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdministrationEvent>>(Events.Where(e => e.OrganizationId == organizationId).ToArray());
    }
}
