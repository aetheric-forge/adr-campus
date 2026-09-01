using AdrCampus.Core.Domain;

namespace AdrCampus.Application.Identity;

public interface IMemberAuthority
{
    Task<bool> IsActiveMemberAsync(
        OrganizationId organizationId,
        MemberId memberId,
        CancellationToken cancellationToken = default);
}
