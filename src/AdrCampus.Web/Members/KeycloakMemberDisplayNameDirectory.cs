using AdrCampus.Application.Identity;
using AdrCampus.Core.Domain;

namespace AdrCampus.Web.Members;

public sealed class KeycloakMemberDisplayNameDirectory(MemberRosterService roster) : IMemberDisplayNameDirectory
{
    public async Task<MemberNameResolution> ResolveAsync(OrganizationId organizationId, IReadOnlyCollection<MemberId> memberIds, CancellationToken cancellationToken = default)
    {
        _ = organizationId;
        var current = await roster.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var requested = memberIds.Select(id => id.Value).ToHashSet(StringComparer.Ordinal);
        var names = current.Members.Where(member => requested.Contains(member.SubjectId)).ToDictionary(member => member.SubjectId, member => member.DisplayName, StringComparer.Ordinal);
        return new(current.IsAvailable, names);
    }
}
