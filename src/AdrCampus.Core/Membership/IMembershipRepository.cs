using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Membership;

public interface IMembershipRepository
{
    Task<IReadOnlyList<MembershipProjection>> ListAsync(OrganizationId organizationId, CancellationToken cancellationToken = default);
    Task<MembershipWriteResult> ApplyAsync(MembershipProjection next, long? expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdministrationEvent>> ListEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default);
}

public enum MembershipWriteStatus { Applied, AlreadyApplied, Conflict }
public sealed record MembershipWriteResult(MembershipWriteStatus Status, MembershipProjection? State);
