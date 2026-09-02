using AdrCampus.Application.Administration;

namespace AdrCampus.Web.Members;

public sealed class KeycloakOrganizationBootstrapVerifier(MemberRosterService rosterService) : IOrganizationBootstrapVerifier
{
    public async Task<OrganizationBootstrapVerification> VerifyAsync(OrganizationBootstrapConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _ = configuration;
        var roster = await rosterService.GetCurrentAsync(cancellationToken);
        if (!roster.IsAvailable)
            return OrganizationBootstrapVerification.Invalid(roster.ErrorMessage ?? "The configured SSO groups could not be verified.");
        if (!roster.Members.Any(member => member.IsMaintainer))
            return OrganizationBootstrapVerification.Invalid("At least one enabled identity must be both a Member and a Maintainer.");
        return OrganizationBootstrapVerification.Valid;
    }
}
