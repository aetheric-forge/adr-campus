using AdrCampus.Core.Domain;
using AdrCampus.Plugin;
using Microsoft.AspNetCore.Components.Authorization;

namespace AdrCampus.Web.Drafts;

/// <summary>Uses circuit authentication rather than actor identifiers supplied by a form.</summary>
public sealed class AuthenticatedProposalReviewCaller(AuthenticationStateProvider authentication) : IProposalReviewCaller
{
    public async Task<MemberId?> GetMemberIdAsync(CancellationToken cancellationToken = default)
    {
        var state = await authentication.GetAuthenticationStateAsync().WaitAsync(cancellationToken);
        var subject = state.User.FindFirst("sub")?.Value;
        return state.User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(subject)
            ? new MemberId(subject)
            : null;
    }
}
