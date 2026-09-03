using AdrCampus.Application.Membership;
using AdrCampus.Web.Drafts;

namespace AdrCampus.Web.Members;

/// <summary>
/// Periodically reconciles the local membership projection against the configured SSO groups, independent
/// of any Maintainer visiting an administration page. Without this, a member who joins, authors a draft,
/// and leaves between two such visits is never observed as departed: no removal transition is recorded and
/// no draft-recovery window ever starts, orphaning their draft.
/// </summary>
public sealed class MembershipSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    CurrentOrganization organization,
    ILogger<MembershipSyncBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var observation = scope.ServiceProvider.GetRequiredService<MembershipObservationService>();
                await observation.SynchronizeAsync(organization.Id, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "A background membership synchronization iteration failed.");
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
}
