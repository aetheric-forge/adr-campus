using AdrCampus.Application.Membership;
using AdrCampus.Core.Domain;

namespace AdrCampus.Web.Members;

public sealed class KeycloakDirectoryRosterSource(MemberRosterService rosterService) : IDirectoryRosterSource
{
    public async Task<DirectoryRosterSnapshot> GetCurrentAsync(OrganizationId organizationId, CancellationToken cancellationToken = default)
    {
        _ = organizationId;
        var roster = await rosterService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!roster.IsAvailable)
        {
            return DirectoryRosterSnapshot.Unavailable(roster.ErrorMessage ?? "The member directory is unavailable.");
        }

        var members = roster.Members
            .Select(member => new DirectoryRosterEntry(new MemberId(member.SubjectId), member.DisplayName, member.IsMaintainer))
            .ToArray();
        return DirectoryRosterSnapshot.Success(members, roster.ObservedAtUtc ?? DateTimeOffset.UtcNow);
    }
}
