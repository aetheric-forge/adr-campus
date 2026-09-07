using AdrCampus.Core.Domain;
using AdrCampus.Core.Maintenance;
using AethericForge.Runtime.Institutions.PostOffice;

namespace AdrCampus.Providers.PostOffice.Tests;

public sealed class PostOfficeMaintenanceDispatcherTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static MaintenanceCommand Command(Guid? id = null) => new(id ?? Guid.NewGuid(), Organization, MaintenanceJob.PurgeExpiredDrafts, Now, "Maintainer");

    [Fact]
    public async Task PostingTheSameCommandTwiceIsIdempotent()
    {
        var postOffice = PostOfficeTestSupport.NewPostOffice();
        var dispatcher = new PostOfficeMaintenanceDispatcher(postOffice);
        var command = Command();

        var first = await dispatcher.PostAsync(command);
        var retry = await dispatcher.PostAsync(command);

        Assert.Equal(MaintenancePostStatus.Accepted, first.Status);
        Assert.Equal(MaintenancePostStatus.AlreadyAccepted, retry.Status);
        Assert.Single(await dispatcher.ListRunsAsync(Organization));
    }

    [Fact]
    public async Task CollectingMarksTheCommandCollectedSoASecondCollectorGetsNothing()
    {
        var postOffice = PostOfficeTestSupport.NewPostOffice();
        var dispatcher = new PostOfficeMaintenanceDispatcher(postOffice);
        await dispatcher.PostAsync(Command());

        var first = await dispatcher.CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts);
        var second = await dispatcher.CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task RecordingTheSameOutcomeTwiceDoesNotDuplicateIt()
    {
        var postOffice = PostOfficeTestSupport.NewPostOffice();
        var dispatcher = new PostOfficeMaintenanceDispatcher(postOffice);
        var command = Command();
        await dispatcher.PostAsync(command);
        await dispatcher.CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts);
        var outcome = new MaintenanceRunOutcome(command.Id, MaintenanceRunStatus.Completed, 3, 0, Now.AddMinutes(1));

        await dispatcher.RecordOutcomeAsync(outcome);
        await dispatcher.RecordOutcomeAsync(outcome);

        var run = Assert.Single(await dispatcher.ListRunsAsync(Organization));
        Assert.NotNull(run.Outcome);
        Assert.Equal(3, run.Outcome!.ProcessedCount);
    }

    [Fact]
    public async Task RunsSurviveDispatcherRecomposition()
    {
        var postOffice = PostOfficeTestSupport.NewPostOffice();
        var command = Command();
        await new PostOfficeMaintenanceDispatcher(postOffice).PostAsync(command);
        await new PostOfficeMaintenanceDispatcher(postOffice).CollectNextAsync(MaintenanceJob.PurgeExpiredDrafts);
        await new PostOfficeMaintenanceDispatcher(postOffice).RecordOutcomeAsync(new MaintenanceRunOutcome(command.Id, MaintenanceRunStatus.Completed, 1, 0, Now.AddMinutes(1)));

        var recomposed = new PostOfficeMaintenanceDispatcher(postOffice);
        var run = Assert.Single(await recomposed.ListRunsAsync(Organization));

        Assert.True(run.IsCollected);
        Assert.Equal(MaintenanceRunStatus.Completed, run.Outcome!.Status);
    }
}
