using AdrCampus.Application.Identity;
using AdrCampus.Core.Domain;
using AdrCampus.Web.Members;

namespace AdrCampus.Web.Drafts;

public sealed class KeycloakMemberAuthority(MemberRosterService rosterService) : IMemberAuthority
{
    public async Task<bool> IsActiveMemberAsync(
        OrganizationId organizationId,
        MemberId memberId,
        CancellationToken cancellationToken = default)
    {
        _ = organizationId;
        var membership = await rosterService.GetMembershipAsync(
            memberId.Value,
            cancellationToken).ConfigureAwait(false);
        return membership.IsAvailable && membership.IsMember;
    }

    public async Task<bool> IsActiveMaintainerAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default)
    {
        _ = organizationId;
        var membership = await rosterService.GetMembershipAsync(memberId.Value, cancellationToken).ConfigureAwait(false);
        return membership.IsAvailable && membership.IsMember && membership.IsMaintainer;
    }
}

public sealed record CurrentOrganization(OrganizationId Id);
