using AdrCampus.Application.Discovery;
using AdrCampus.Application.Identity;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Domain;

namespace AdrCampus.Application.Tests;

public sealed class DiscoveryApplicationServiceTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Member = new("member-1");
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SharedRecordView.Current, AdrLifecycleStatus.Accepted)]
    [InlineData(SharedRecordView.Proposed, AdrLifecycleStatus.Proposed)]
    [InlineData(SharedRecordView.Historical, AdrLifecycleStatus.Rejected)]
    public async Task EachBrowseViewReturnsOnlyItsLifecycleGroup(SharedRecordView view, AdrLifecycleStatus expected)
    {
        var service = Service([Proposed(1), Decided(2, DecisionOutcome.Accepted), Decided(3, DecisionOutcome.Rejected)]);
        var result = await service.BrowseAsync(new(Organization, Member, view));
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.TotalSharedCount);
        Assert.Single(result.Items);
        Assert.Equal(expected, result.Items[0].Status);
    }

    [Fact]
    public async Task AllViewOrdersByRelevantLifecycleEventThenStableIdentifier()
    {
        var newestLowId = Decided(1, DecisionOutcome.Accepted, 5);
        var newestHighId = Decided(3, DecisionOutcome.Rejected, 5);
        var older = Proposed(2, 1);
        var result = await Service([older, newestHighId, newestLowId]).BrowseAsync(new(Organization, Member, SharedRecordView.All));
        Assert.Equal(new[] { newestLowId.Id, newestHighId.Id, older.Id }, result.Items.Select(item => item.Id));
        Assert.Equal("Decider", result.Items[0].RelevantActorRole);
        Assert.Equal("Proposer", result.Items[2].RelevantActorRole);
    }

    [Fact]
    public async Task DistinguishesEmptyOrganizationFromEmptyView()
    {
        var empty = await Service([]).BrowseAsync(new(Organization, Member, SharedRecordView.Current));
        var noCurrent = await Service([Proposed(1)]).BrowseAsync(new(Organization, Member, SharedRecordView.Current));
        Assert.True(empty.IsOrganizationEmpty);
        Assert.False(empty.IsViewEmpty);
        Assert.False(noCurrent.IsOrganizationEmpty);
        Assert.True(noCurrent.IsViewEmpty);
    }

    [Fact]
    public async Task UnauthorizedMemberDoesNotReachSharedRepository()
    {
        var repository = new Repository([]);
        var service = new DiscoveryApplicationService(repository, new Authority(false));
        var result = await service.BrowseAsync(new(Organization, Member, SharedRecordView.All));
        Assert.Equal(DiscoveryQueryStatus.Unauthorized, result.Status);
        Assert.Equal(0, repository.Calls);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task PersistenceFailureReturnsUnavailableRatherThanPartialResults()
    {
        var service = new DiscoveryApplicationService(new Repository([], new IOException("unavailable")), new Authority(true));
        var result = await service.BrowseAsync(new(Organization, Member, SharedRecordView.All));
        Assert.Equal(DiscoveryQueryStatus.Unavailable, result.Status);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task UndefinedViewIsRejectedWithoutQueryingTheRepository()
    {
        var repository = new Repository([]);
        var result = await new DiscoveryApplicationService(repository, new Authority(true)).BrowseAsync(new(Organization, Member, (SharedRecordView)999));
        Assert.Equal(DiscoveryQueryStatus.Invalid, result.Status);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task ProviderRecordsFromAnotherOrganizationAreDiscarded()
    {
        var other = Proposed(1) with { OrganizationId = new OrganizationId("other") };
        var result = await Service([other]).BrowseAsync(new(Organization, Member, SharedRecordView.All));
        Assert.True(result.IsOrganizationEmpty);
        Assert.Empty(result.Items);
    }

    private static DiscoveryApplicationService Service(IReadOnlyList<AdrProposal> records) => new(new Repository(records), new Authority(true));
    private static AdrProposal Proposed(int id, int proposedMinute = 0) => new(new AdrId(new Guid(id, 0, 0, new byte[8])), Organization, new("author"), new("proposer"), new(new("Decision title"), "Context", "Decision", "Consequences"), Now, Now.AddMinutes(proposedMinute), 1);
    private static AdrProposal Decided(int id, DecisionOutcome outcome, int decidedMinute = 2) => Proposed(id).Decide(outcome, new MemberId("decider"), outcome == DecisionOutcome.Accepted ? "" : "Reason", Now.AddMinutes(decidedMinute));

    private sealed class Repository(IReadOnlyList<AdrProposal> records, Exception? exception = null) : ISharedRecordRepository
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<AdrProposal>> ListSharedAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (exception is not null) throw exception;
            return Task.FromResult(records);
        }
    }

    private sealed class Authority(bool active) : IMemberAuthority
    {
        public Task<bool> IsActiveMemberAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(active);
    }
}
