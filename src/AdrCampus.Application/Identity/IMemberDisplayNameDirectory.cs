using AdrCampus.Core.Domain;

namespace AdrCampus.Application.Identity;

public interface IMemberDisplayNameDirectory
{
    Task<MemberNameResolution> ResolveAsync(OrganizationId organizationId, IReadOnlyCollection<MemberId> memberIds, CancellationToken cancellationToken = default);
}

public sealed record MemberNameResolution(bool IsAvailable, IReadOnlyDictionary<string, string> Names)
{
    public string For(MemberId memberId) => Names.TryGetValue(memberId.Value, out var displayName) ? displayName : "Former member";
}
