using Microsoft.AspNetCore.Authorization;
using AdrCampus.Web.Members;

namespace AdrCampus.Web.Identity;

public static class IdentityPolicies
{
    public const string ActiveMember = "ActiveMember";
}

public sealed class ActiveMemberRequirement : IAuthorizationRequirement;

public sealed class ActiveMemberAuthorizationHandler(MemberRosterService rosterService)
    : AuthorizationHandler<ActiveMemberRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveMemberRequirement requirement)
    {
        var subjectId = context.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(subjectId) &&
            await rosterService.IsActiveMemberAsync(subjectId).ConfigureAwait(false))
        {
            context.Succeed(requirement);
        }
    }
}
