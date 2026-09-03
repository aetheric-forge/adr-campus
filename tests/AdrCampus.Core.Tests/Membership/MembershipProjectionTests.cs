using AdrCampus.Core.Domain;
using AdrCampus.Core.Membership;

namespace AdrCampus.Core.Tests;

public sealed class MembershipProjectionTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Member = new("member-1");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ObserveStartsAtVersionOne()
    {
        var projection = MembershipProjection.Observe(Organization, Member, MemberRole.Member, "Ada Lovelace", Now);
        Assert.Equal(1, projection.Version);
        Assert.Equal(MemberRole.Member, projection.Role);
        Assert.Equal(Now, projection.FirstObservedAtUtc);
        Assert.Equal(Now, projection.LastObservedAtUtc);
    }

    [Fact]
    public void TransitionAdvancesVersionAndPreservesFirstObserved()
    {
        var projection = MembershipProjection.Observe(Organization, Member, MemberRole.Member, "Ada Lovelace", Now);
        var promoted = projection.Transition(MemberRole.Maintainer, "Ada Lovelace", Now.AddMinutes(5));
        Assert.Equal(2, promoted.Version);
        Assert.Equal(MemberRole.Maintainer, promoted.Role);
        Assert.Equal(Now, promoted.FirstObservedAtUtc);
        Assert.Equal(Now.AddMinutes(5), promoted.LastObservedAtUtc);
    }

    [Fact]
    public void TransitionRejectsObservationsOlderThanTheLastOne()
    {
        var projection = MembershipProjection.Observe(Organization, Member, MemberRole.Member, "Ada Lovelace", Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => projection.Transition(MemberRole.Maintainer, "Ada Lovelace", Now.AddMinutes(-1)));
    }

    [Fact]
    public void HasSameObservedStateComparesRoleAndTrimmedDisplayName()
    {
        var projection = MembershipProjection.Observe(Organization, Member, MemberRole.Member, "Ada Lovelace", Now);
        Assert.True(projection.HasSameObservedState(MemberRole.Member, " Ada Lovelace "));
        Assert.False(projection.HasSameObservedState(MemberRole.Maintainer, "Ada Lovelace"));
        Assert.False(projection.HasSameObservedState(MemberRole.Member, "Ada L."));
    }
}
