using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Membership;

public enum MemberRole { None, Member, Maintainer }

public sealed record MembershipProjection
{
    private MembershipProjection(OrganizationId organizationId, MemberId memberId, MemberRole role, string displayName, DateTimeOffset firstObservedAtUtc, DateTimeOffset lastObservedAtUtc, long version)
    {
        OrganizationId = organizationId;
        MemberId = memberId;
        Role = role;
        DisplayName = Required(displayName, nameof(displayName));
        if (lastObservedAtUtc < firstObservedAtUtc) throw new ArgumentOutOfRangeException(nameof(lastObservedAtUtc));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        FirstObservedAtUtc = firstObservedAtUtc;
        LastObservedAtUtc = lastObservedAtUtc;
        Version = version;
    }

    public OrganizationId OrganizationId { get; }
    public MemberId MemberId { get; }
    public MemberRole Role { get; }
    public string DisplayName { get; }
    public DateTimeOffset FirstObservedAtUtc { get; }
    public DateTimeOffset LastObservedAtUtc { get; }
    public long Version { get; }

    public static MembershipProjection Observe(OrganizationId organizationId, MemberId memberId, MemberRole role, string displayName, DateTimeOffset now) =>
        new(organizationId, memberId, role, displayName, now, now, 1);

    public static MembershipProjection Restore(OrganizationId organizationId, MemberId memberId, MemberRole role, string displayName, DateTimeOffset firstObserved, DateTimeOffset lastObserved, long version) =>
        new(organizationId, memberId, role, displayName, firstObserved, lastObserved, version);

    public MembershipProjection Transition(MemberRole role, string displayName, DateTimeOffset now)
    {
        if (now < LastObservedAtUtc) throw new ArgumentOutOfRangeException(nameof(now));
        return new(OrganizationId, MemberId, role, displayName, FirstObservedAtUtc, now, checked(Version + 1));
    }

    public bool HasSameObservedState(MemberRole role, string displayName) => Role == role && DisplayName == displayName.Trim();

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.", name) : value.Trim();
}
