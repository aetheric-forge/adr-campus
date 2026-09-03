using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Administration;

public interface IOrganizationAdministrationRepository
{
    Task<OrganizationAdministrationState?> GetAsync(OrganizationId organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationAdministrationWriteResult> BootstrapAsync(OrganizationAdministrationState state, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default);
    Task<OrganizationAdministrationWriteResult> RenameAsync(OrganizationAdministrationState state, long expectedVersion, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdministrationEvent>> ListEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default);
}

public enum OrganizationAdministrationWriteStatus { Created, Saved, AlreadyApplied, Conflict, ConfigurationMismatch, OperationMismatch }
public sealed record OrganizationAdministrationWriteResult(OrganizationAdministrationWriteStatus Status, OrganizationAdministrationState? State)
{
    public bool IsSuccess => Status is OrganizationAdministrationWriteStatus.Created or OrganizationAdministrationWriteStatus.Saved or OrganizationAdministrationWriteStatus.AlreadyApplied;
}
