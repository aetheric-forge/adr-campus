using AdrCampus.Core.Domain;
using AdrCampus.Core.Maintenance;
using AdrCampus.Providers.Drafts.Workbench;
using AethericForge.Runtime.Providers.Staging.InMemory;

namespace AdrCampus.Providers.Drafts.Workbench.Tests;

public sealed class WorkbenchMaintenancePostOfficeTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static MaintenanceCommand Command(Guid? id = null) => new(id ?? Guid.NewGuid(), Organization, MaintenanceJob.PurgeExpiredDrafts, Now, "Maintainer");

    [Fact]
    public async Task PostingTheSameCommandTwiceIsIdempotent()
    {
        var staging = new InMemoryStagingProvider("maintenance");
        var postOffice = new WorkbenchMaintenancePostOffice(staging);
        var command = Command();

        var first = await postOffice.PostAsync(command);
        var retry = await postOffice.PostAsync(command);

        Assert.Equal(MaintenancePostStatus.Accepted, first.Status);
        Assert.Equal(MaintenancePostStatus.AlreadyAccepted, retry.Status);
        Assert.Single(await postOffice.ListRunsAsync(Organization));
    }

    [Fact]
    public async Task CollectingMarksTheCommandCollectedSoASecondCollectorGetsNothing()
    {
        var staging = new InMemoryStagingProvider("maintenance");
        var postOffice = new WorkbenchMaintenancePostOffice(staging);
        await postOffice.PostAsync(Command());

        var first = await postOffice.CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts);
        var second = await postOffice.CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentCollectorsGetsTheSameCommand()
    {
        var staging = new InMemoryStagingProvider("maintenance");
        var postOffice = new WorkbenchMaintenancePostOffice(staging);
        await postOffice.PostAsync(Command());

        var results = await Task.WhenAll(
            postOffice.CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts),
            postOffice.CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts));

        Assert.Single(results, r => r is not null);
        Assert.Single(results, r => r is null);
    }

    [Fact]
    public async Task RecordingTheSameOutcomeTwiceDoesNotDuplicateIt()
    {
        var staging = new InMemoryStagingProvider("maintenance");
        var postOffice = new WorkbenchMaintenancePostOffice(staging);
        var command = Command();
        await postOffice.PostAsync(command);
        await postOffice.CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts);
        var outcome = new MaintenanceRunOutcome(command.Id, MaintenanceRunStatus.Completed, 3, 0, Now.AddMinutes(1));

        await postOffice.RecordOutcomeAsync(outcome);
        await postOffice.RecordOutcomeAsync(outcome);

        var run = Assert.Single(await postOffice.ListRunsAsync(Organization));
        Assert.NotNull(run.Outcome);
        Assert.Equal(3, run.Outcome!.ProcessedCount);
    }

    [Fact]
    public async Task RunsSurviveRepositoryRecomposition()
    {
        var staging = new InMemoryStagingProvider("maintenance");
        var command = Command();
        await new WorkbenchMaintenancePostOffice(staging).PostAsync(command);
        await new WorkbenchMaintenancePostOffice(staging).CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts);
        await new WorkbenchMaintenancePostOffice(staging).RecordOutcomeAsync(new MaintenanceRunOutcome(command.Id, MaintenanceRunStatus.Completed, 1, 0, Now.AddMinutes(1)));

        var recomposed = new WorkbenchMaintenancePostOffice(staging);
        var run = Assert.Single(await recomposed.ListRunsAsync(Organization));

        Assert.True(run.IsCollected);
        Assert.Equal(MaintenanceRunStatus.Completed, run.Outcome!.Status);
    }
}
