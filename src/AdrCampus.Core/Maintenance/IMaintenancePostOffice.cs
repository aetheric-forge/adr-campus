using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Maintenance;

/// <summary>
/// Custody ledger for maintenance commands. Accepting a command records custody of the request, not
/// successful execution; <see cref="RecordOutcomeAsync"/> is what records processing outcomes.
/// </summary>
public interface IMaintenancePostOffice
{
    Task<MaintenancePostResult> PostAsync(MaintenanceCommand command, CancellationToken cancellationToken = default);
    Task<MaintenanceCommand?> CollectNextAsync(MaintenanceJob job, CancellationToken cancellationToken = default);
    Task RecordOutcomeAsync(MaintenanceRunOutcome outcome, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaintenanceRunRecord>> ListRunsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default);
}

public enum MaintenancePostStatus { Accepted, AlreadyAccepted }
public sealed record MaintenancePostResult(MaintenancePostStatus Status, MaintenanceCommand Command);

public interface IMaintenanceWorker
{
    MaintenanceJob Job { get; }
    Task<MaintenanceRunOutcome> RunAsync(MaintenanceCommand command, CancellationToken cancellationToken = default);
}
