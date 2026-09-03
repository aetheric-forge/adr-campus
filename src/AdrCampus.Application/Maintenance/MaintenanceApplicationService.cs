using AdrCampus.Application.Identity;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Maintenance;

namespace AdrCampus.Application.Maintenance;

public sealed class MaintenanceApplicationService(IMaintenancePostOffice postOffice, IMemberAuthority memberAuthority, TimeProvider timeProvider)
{
    public async Task<RequestPurgeResult> RequestPurgeAsync(OrganizationId organizationId, MemberId maintainerId, CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMaintainerAsync(organizationId, maintainerId, cancellationToken).ConfigureAwait(false))
        {
            return RequestPurgeResult.Unauthorized();
        }

        var command = new MaintenanceCommand(Guid.NewGuid(), organizationId, MaintenanceJob.PurgeExpiredDrafts, timeProvider.GetUtcNow(), "Maintainer");
        var result = await postOffice.PostAsync(command, cancellationToken).ConfigureAwait(false);
        return RequestPurgeResult.Posted(result.Command);
    }

    public async Task<MaintenanceRunsResult> ListRunsAsync(OrganizationId organizationId, MemberId maintainerId, CancellationToken cancellationToken = default)
    {
        if (!await memberAuthority.IsActiveMaintainerAsync(organizationId, maintainerId, cancellationToken).ConfigureAwait(false))
        {
            return MaintenanceRunsResult.Unauthorized();
        }

        var runs = await postOffice.ListRunsAsync(organizationId, cancellationToken).ConfigureAwait(false);
        return MaintenanceRunsResult.Success(runs);
    }
}

public sealed record RequestPurgeResult(bool IsAuthorized, MaintenanceCommand? Command)
{
    public static RequestPurgeResult Posted(MaintenanceCommand command) => new(true, command);
    public static RequestPurgeResult Unauthorized() => new(false, null);
}

public sealed record MaintenanceRunsResult(bool IsAuthorized, IReadOnlyList<MaintenanceRunRecord> Runs)
{
    public static MaintenanceRunsResult Success(IReadOnlyList<MaintenanceRunRecord> runs) => new(true, runs);
    public static MaintenanceRunsResult Unauthorized() => new(false, []);
}
