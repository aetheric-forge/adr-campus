using AdrCampus.Application.Identity;
using AdrCampus.Application.Maintenance;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Maintenance;

namespace AdrCampus.Application.Tests;

public sealed class MaintenanceApplicationServiceTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Maintainer = new("maintainer-1");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RequestPurgeRequiresMaintainer()
    {
        var postOffice = new StubPostOffice();
        var authorized = await Create(postOffice, maintainer: true).RequestPurgeAsync(Organization, Maintainer);
        var unauthorized = await Create(postOffice, maintainer: false).RequestPurgeAsync(Organization, Maintainer);

        Assert.True(authorized.IsAuthorized);
        Assert.NotNull(authorized.Command);
        Assert.False(unauthorized.IsAuthorized);
        Assert.Single(postOffice.Posted);
    }

    [Fact]
    public async Task RequestPurgePostsAPurgeExpiredDraftsCommand()
    {
        var postOffice = new StubPostOffice();
        var result = await Create(postOffice).RequestPurgeAsync(Organization, Maintainer);

        Assert.Equal(MaintenanceJob.PurgeExpiredDrafts, result.Command!.Job);
        Assert.Equal(Organization, result.Command.OrganizationId);
    }

    [Fact]
    public async Task ListRunsRequiresMaintainer()
    {
        var postOffice = new StubPostOffice();
        var unauthorized = await Create(postOffice, maintainer: false).ListRunsAsync(Organization, Maintainer);
        Assert.False(unauthorized.IsAuthorized);
        Assert.Empty(unauthorized.Runs);
    }

    private static MaintenanceApplicationService Create(StubPostOffice postOffice, bool maintainer = true) =>
        new(postOffice, new StubAuthority(maintainer), new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class StubAuthority(bool maintainer) : IMemberAuthority
    {
        public Task<bool> IsActiveMemberAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(maintainer);
        public Task<bool> IsActiveMaintainerAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(maintainer);
    }

    private sealed class StubPostOffice : IMaintenancePostOffice
    {
        public List<MaintenanceCommand> Posted { get; } = [];

        public Task<MaintenancePostResult> PostAsync(MaintenanceCommand command, CancellationToken cancellationToken = default)
        {
            Posted.Add(command);
            return Task.FromResult(new MaintenancePostResult(MaintenancePostStatus.Accepted, command));
        }

        public Task<MaintenanceCommand?> CollectNextAsync(MaintenanceJob job, CancellationToken cancellationToken = default) => Task.FromResult<MaintenanceCommand?>(null);
        public Task RecordOutcomeAsync(MaintenanceRunOutcome outcome, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MaintenanceRunRecord>> ListRunsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MaintenanceRunRecord>>(Posted.Where(c => c.OrganizationId == organizationId).Select(c => new MaintenanceRunRecord(c, null, false)).ToArray());
    }
}
