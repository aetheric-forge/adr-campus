using AdrCampus.Core.Domain;

namespace AdrCampus.Application.Drafts;

/// <summary>
/// Consumed by <see cref="AdrCampus.Application.Membership.MembershipObservationService"/> to start
/// or cancel draft recovery windows immediately after a membership transition is persisted.
/// </summary>
public interface IDraftRecoveryCoordinator
{
    Task StartRecoveryForDepartedMemberAsync(OrganizationId organizationId, MemberId formerMemberId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default);
    Task CancelRecoveryForReturningMemberAsync(OrganizationId organizationId, MemberId memberId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken = default);
}
