using AdrCampus.Application.Drafts;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Maintenance;

namespace AdrCampus.Application.Tests;

public sealed class ExpiredDraftPurgeWorkerTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static MaintenanceCommand Command() => new(Guid.NewGuid(), Organization, MaintenanceJob.PurgeExpiredDrafts, Now, "Maintainer");

    [Fact]
    public async Task NoExpiredDraftsCompletesWithNothingProcessed()
    {
        var worker = new ExpiredDraftPurgeWorker(new StubPurgeRepository([]), new FixedTimeProvider(Now));
        var outcome = await worker.RunAsync(Command());
        Assert.Equal(MaintenanceRunStatus.Completed, outcome.Status);
        Assert.Equal(0, outcome.ProcessedCount);
        Assert.Equal(0, outcome.RemainingCount);
    }

    [Fact]
    public async Task AllPurgedUnderBatchSizeCompletesTheRun()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => AdrId.New()).ToArray();
        var worker = new ExpiredDraftPurgeWorker(new StubPurgeRepository(ids), new FixedTimeProvider(Now));
        var outcome = await worker.RunAsync(Command());
        Assert.Equal(MaintenanceRunStatus.Completed, outcome.Status);
        Assert.Equal(5, outcome.ProcessedCount);
        Assert.Equal(0, outcome.RemainingCount);
    }

    [Fact]
    public async Task ContinuesPastIndividualFailuresAndReportsPartial()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => AdrId.New()).ToArray();
        var failing = new HashSet<Guid> { ids[2].Value };
        var repository = new StubPurgeRepository(ids, failing);
        var worker = new ExpiredDraftPurgeWorker(repository, new FixedTimeProvider(Now));

        var outcome = await worker.RunAsync(Command());

        Assert.Equal(MaintenanceRunStatus.Partial, outcome.Status);
        Assert.Equal(4, outcome.ProcessedCount);
        Assert.Equal(5, repository.PurgedCalls.Count);
        Assert.NotNull(outcome.FailureReason);
    }

    [Fact]
    public async Task AllFailuresReportFailed()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => AdrId.New()).ToArray();
        var failing = ids.Select(i => i.Value).ToHashSet();
        var worker = new ExpiredDraftPurgeWorker(new StubPurgeRepository(ids, failing), new FixedTimeProvider(Now));

        var outcome = await worker.RunAsync(Command());

        Assert.Equal(MaintenanceRunStatus.Failed, outcome.Status);
        Assert.Equal(0, outcome.ProcessedCount);
    }

    [Fact]
    public async Task AFullBatchSignalsRemainingWorkForFollowUp()
    {
        var ids = Enumerable.Range(0, 25).Select(_ => AdrId.New()).ToArray();
        var worker = new ExpiredDraftPurgeWorker(new StubPurgeRepository(ids), new FixedTimeProvider(Now));

        var outcome = await worker.RunAsync(Command());

        Assert.Equal(MaintenanceRunStatus.Partial, outcome.Status);
        Assert.Equal(25, outcome.ProcessedCount);
        Assert.True(outcome.RemainingCount > 0);
    }

    [Fact]
    public async Task AListingFailureReportsFailedWithAReason()
    {
        var worker = new ExpiredDraftPurgeWorker(new ThrowingListRepository(), new FixedTimeProvider(Now));
        var outcome = await worker.RunAsync(Command());
        Assert.Equal(MaintenanceRunStatus.Failed, outcome.Status);
        Assert.NotNull(outcome.FailureReason);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class StubPurgeRepository(IReadOnlyList<AdrId> expired, HashSet<Guid>? failIds = null) : IExpiredDraftPurgeRepository
    {
        public List<AdrId> PurgedCalls { get; } = [];

        public Task<IReadOnlyList<AdrId>> ListExpiredAsync(OrganizationId organizationId, DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdrId>>(expired.Take(batchSize).ToArray());

        public Task<int> PurgeBatchAsync(OrganizationId organizationId, IReadOnlyCollection<AdrId> draftIds, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default)
        {
            var id = draftIds.Single();
            PurgedCalls.Add(id);
            return Task.FromResult(failIds?.Contains(id.Value) == true ? 0 : 1);
        }
    }

    private sealed class ThrowingListRepository : IExpiredDraftPurgeRepository
    {
        public Task<IReadOnlyList<AdrId>> ListExpiredAsync(OrganizationId organizationId, DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Storage unavailable.");
        public Task<int> PurgeBatchAsync(OrganizationId organizationId, IReadOnlyCollection<AdrId> draftIds, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
