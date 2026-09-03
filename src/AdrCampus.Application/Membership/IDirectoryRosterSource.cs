using AdrCampus.Core.Domain;

namespace AdrCampus.Application.Membership;

public interface IDirectoryRosterSource
{
    Task<DirectoryRosterSnapshot> GetCurrentAsync(OrganizationId organizationId, CancellationToken cancellationToken = default);
}

public sealed record DirectoryRosterEntry(MemberId MemberId, string DisplayName, bool IsMaintainer);

public sealed record DirectoryRosterSnapshot(bool IsAvailable, IReadOnlyCollection<DirectoryRosterEntry> Members, DateTimeOffset ObservedAtUtc, string? ErrorMessage)
{
    public bool HasActiveMaintainer => Members.Any(member => member.IsMaintainer);

    public static DirectoryRosterSnapshot Success(IReadOnlyCollection<DirectoryRosterEntry> members, DateTimeOffset observedAtUtc) => new(true, members, observedAtUtc, null);
    public static DirectoryRosterSnapshot Unavailable(string errorMessage) => new(false, Array.Empty<DirectoryRosterEntry>(), default, errorMessage);
}
