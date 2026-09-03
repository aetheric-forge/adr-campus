using AdrCampus.Core.Maintenance;

namespace AdrCampus.Web.Maintenance;

/// <summary>
/// Collects posted maintenance commands and dispatches them to registered bounded workers. Owns no
/// knowledge of what any individual job does — that lives entirely in the <see cref="IMaintenanceWorker"/>
/// implementations resolved through DI.
/// </summary>
public sealed class MaintenanceDispatchService(IMaintenancePostOffice postOffice, IEnumerable<IMaintenanceWorker> workers, ILogger<MaintenanceDispatchService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var byJob = workers.ToDictionary(worker => worker.Job);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(byJob, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "A maintenance dispatch iteration failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DispatchOnceAsync(IReadOnlyDictionary<MaintenanceJob, IMaintenanceWorker> byJob, CancellationToken cancellationToken)
    {
        foreach (var (job, worker) in byJob)
        {
            MaintenanceCommand? command;
            while ((command = await postOffice.CollectNextAsync(job, cancellationToken).ConfigureAwait(false)) is not null)
            {
                var outcome = await RunSafelyAsync(worker, command, cancellationToken).ConfigureAwait(false);
                await postOffice.RecordOutcomeAsync(outcome, cancellationToken).ConfigureAwait(false);
                if (outcome.RemainingCount > 0)
                {
                    await postOffice.PostAsync(new MaintenanceCommand(Guid.NewGuid(), command.OrganizationId, job, outcome.OccurredAtUtc, "Maintenance"), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<MaintenanceRunOutcome> RunSafelyAsync(IMaintenanceWorker worker, MaintenanceCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await worker.RunAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new MaintenanceRunOutcome(command.Id, MaintenanceRunStatus.Failed, 0, 0, DateTimeOffset.UtcNow, exception.Message);
        }
    }
}
