using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Membership;
using AdrCampus.Providers.Drafts.Workbench;
using AethericForge.Runtime.Providers.Staging.InMemory;

namespace AdrCampus.Providers.Drafts.Workbench.Tests;

public sealed class WorkbenchMembershipRepositoryTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Ada = new("ada");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static AdministrationEvent Event(AdministrationEventType type, DateTimeOffset at, string? previous = null, string? next = null) =>
        new(Guid.NewGuid(), Organization, type, at, "SSO observation", SubjectId: Ada, PreviousValue: previous, NewValue: next);

    [Fact]
    public async Task NewProjectionAndEventSurviveRecomposition()
    {
        var staging = new InMemoryStagingProvider("membership");
        var repository = new WorkbenchMembershipRepository(staging);
        var projection = MembershipProjection.Observe(Organization, Ada, MemberRole.Member, "Ada Lovelace", Now);
        var write = await repository.ApplyAsync(projection, null, Event(AdministrationEventType.MemberAdded, Now, next: "Member"));
        Assert.Equal(MembershipWriteStatus.Applied, write.Status);

        var recomposed = new WorkbenchMembershipRepository(staging);
        var listed = await recomposed.ListAsync(Organization);
        Assert.Equal(projection, Assert.Single(listed));
        Assert.Single(await recomposed.ListEventsAsync(Organization));
    }

    [Fact]
    public async Task ReapplyingTheSameTransitionIsIdempotent()
    {
        var staging = new InMemoryStagingProvider("membership");
        var repository = new WorkbenchMembershipRepository(staging);
        var projection = MembershipProjection.Observe(Organization, Ada, MemberRole.Member, "Ada Lovelace", Now);
        await repository.ApplyAsync(projection, null, Event(AdministrationEventType.MemberAdded, Now, next: "Member"));
        var promoted = projection.Transition(MemberRole.Maintainer, "Ada Lovelace", Now.AddMinutes(1));
        var evt = Event(AdministrationEventType.MaintainerGranted, Now.AddMinutes(1), "Member", "Maintainer");

        var first = await repository.ApplyAsync(promoted, 1, evt);
        var retry = await new WorkbenchMembershipRepository(staging).ApplyAsync(promoted, 1, evt);

        Assert.Equal(MembershipWriteStatus.Applied, first.Status);
        Assert.Equal(MembershipWriteStatus.AlreadyApplied, retry.Status);
        Assert.Equal(2, (await repository.ListEventsAsync(Organization)).Count);
    }

    [Fact]
    public async Task StaleExpectedVersionConflicts()
    {
        var staging = new InMemoryStagingProvider("membership");
        var repository = new WorkbenchMembershipRepository(staging);
        var projection = MembershipProjection.Observe(Organization, Ada, MemberRole.Member, "Ada Lovelace", Now);
        await repository.ApplyAsync(projection, null, Event(AdministrationEventType.MemberAdded, Now, next: "Member"));
        var promoted = projection.Transition(MemberRole.Maintainer, "Ada Lovelace", Now.AddMinutes(1));

        var result = await repository.ApplyAsync(promoted, 5, Event(AdministrationEventType.MaintainerGranted, Now.AddMinutes(1), "Member", "Maintainer"));

        Assert.Equal(MembershipWriteStatus.Conflict, result.Status);
        Assert.Equal(projection, Assert.Single(await repository.ListAsync(Organization)));
    }

    [Fact]
    public async Task ConcurrentWritersDoNotDuplicateAnAppliedTransition()
    {
        var staging = new InMemoryStagingProvider("membership");
        var repository = new WorkbenchMembershipRepository(staging);
        var projection = MembershipProjection.Observe(Organization, Ada, MemberRole.Member, "Ada Lovelace", Now);
        await repository.ApplyAsync(projection, null, Event(AdministrationEventType.MemberAdded, Now, next: "Member"));
        var promoted = projection.Transition(MemberRole.Maintainer, "Ada Lovelace", Now.AddMinutes(1));
        var evt = Event(AdministrationEventType.MaintainerGranted, Now.AddMinutes(1), "Member", "Maintainer");

        var results = await Task.WhenAll(
            repository.ApplyAsync(promoted, 1, evt),
            new WorkbenchMembershipRepository(staging).ApplyAsync(promoted, 1, evt));

        Assert.Contains(MembershipWriteStatus.Applied, results.Select(r => r.Status));
        Assert.All(results, r => Assert.True(r.Status is MembershipWriteStatus.Applied or MembershipWriteStatus.AlreadyApplied));
        Assert.Equal(2, (await repository.ListEventsAsync(Organization)).Count);
    }
}
