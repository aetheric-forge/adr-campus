using AdrCampus.Core.Drafts;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Maintenance;

namespace AdrCampus.Application.Drafts;

public sealed class ExpiredDraftPurgeWorker(IExpiredDraftPurgeRepository repository, TimeProvider timeProvider) : IMaintenanceWorker
{
    private const int BatchSize = 25;

    public MaintenanceJob Job => MaintenanceJob.PurgeExpiredDrafts;

    public async Task<MaintenanceRunOutcome> RunAsync(MaintenanceCommand command, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        IReadOnlyList<AdrId> expired;
        try
        {
            expired = await repository.ListExpiredAsync(command.OrganizationId, now, BatchSize, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(command.Id, MaintenanceRunStatus.Failed, 0, 0, now, exception.Message);
        }

        if (expired.Count == 0)
        {
            return new(command.Id, MaintenanceRunStatus.Completed, 0, 0, now);
        }

        var purgedCount = 0;
        var failedCount = 0;
        foreach (var id in expired)
        {
            try
            {
                var removed = await repository.PurgeBatchAsync(command.OrganizationId, [id], now, cancellationToken).ConfigureAwait(false);
                if (removed > 0) purgedCount++; else failedCount++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failedCount++;
            }
        }

        var hasMoreBatches = expired.Count == BatchSize;
        var remaining = failedCount + (hasMoreBatches ? 1 : 0);
        var status = failedCount == expired.Count
            ? MaintenanceRunStatus.Failed
            : failedCount > 0 || hasMoreBatches ? MaintenanceRunStatus.Partial : MaintenanceRunStatus.Completed;
        return new(command.Id, status, purgedCount, remaining, now, failedCount > 0 ? $"{failedCount} draft(s) could not be purged." : null);
    }
}
