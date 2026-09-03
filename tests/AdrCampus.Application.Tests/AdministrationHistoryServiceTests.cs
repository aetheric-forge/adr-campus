using AdrCampus.Application.Administration;
using AdrCampus.Application.Identity;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Membership;

namespace AdrCampus.Application.Tests;

public sealed class AdministrationHistoryServiceTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Maintainer = new("maintainer-1");
    private static readonly MemberId Former = new("former-author");
    private static readonly MemberId Newer = new("new-author");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RequiresMaintainer()
    {
        var service = Create(maintainer: false);
        var result = await service.ListAsync(Organization, Maintainer);
        Assert.False(result.IsAuthorized);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task MergesAndSortsEventsFromAllThreeSourcesNewestFirst()
    {
        var organizationRepository = new StubOrganizationRepository([Evt(AdministrationEventType.OrganizationBootstrapped, Now.AddDays(-3))]);
        var membershipRepository = new StubMembershipRepository([Evt(AdministrationEventType.MemberAdded, Now.AddDays(-2))]);
        var recoveryRepository = new StubRecoveryRepository([Evt(AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-1))]);
        var service = new AdministrationHistoryService(organizationRepository, membershipRepository, recoveryRepository, new StubAuthority(true), new StubNames());

        var result = await service.ListAsync(Organization, Maintainer);

        Assert.Equal(
            [AdministrationEventType.DraftRecoveryStarted, AdministrationEventType.MemberAdded, AdministrationEventType.OrganizationBootstrapped],
            result.Items.Select(i => i.Type));
    }

    [Fact]
    public async Task ResolvesActorAndSubjectDisplayNames()
    {
        var evt = new AdministrationEvent(Guid.NewGuid(), Organization, AdministrationEventType.OrganizationRenamed, Now, "ADR Campus", ActorId: Maintainer, PreviousValue: "Old Name", NewValue: "New Name");
        var service = Create(maintainer: true, organizationEvents: [evt]);

        var result = await service.ListAsync(Organization, Maintainer);

        Assert.Equal("Maintainer One", Assert.Single(result.Items).ActorDisplayName);
    }

    [Fact]
    public async Task ResolvesFormerAndNewAuthorNamesForReassignmentEvents()
    {
        var evt = new AdministrationEvent(Guid.NewGuid(), Organization, AdministrationEventType.DraftReassigned, Now, "ADR Campus", ActorId: Maintainer, SubjectId: Newer, PreviousValue: Former.Value, NewValue: Newer.Value, DraftId: AdrId.New());
        var service = Create(maintainer: true, recoveryEvents: [evt]);

        var item = Assert.Single((await service.ListAsync(Organization, Maintainer)).Items);

        Assert.Equal("Former Person", item.PreviousValue);
        Assert.Equal("New Person", item.NewValue);
        Assert.NotEqual(Former.Value, item.PreviousValue);
        Assert.NotEqual(Newer.Value, item.NewValue);
    }

    private static AdministrationEvent Evt(AdministrationEventType type, DateTimeOffset at) => new(Guid.NewGuid(), Organization, type, at, "test");

    private static AdministrationHistoryService Create(bool maintainer, IReadOnlyList<AdministrationEvent>? organizationEvents = null, IReadOnlyList<AdministrationEvent>? membershipEvents = null, IReadOnlyList<AdministrationEvent>? recoveryEvents = null) =>
        new(new StubOrganizationRepository(organizationEvents ?? []), new StubMembershipRepository(membershipEvents ?? []), new StubRecoveryRepository(recoveryEvents ?? []), new StubAuthority(maintainer), new StubNames());

    private sealed class StubAuthority(bool maintainer) : IMemberAuthority
    {
        public Task<bool> IsActiveMemberAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(maintainer);
        public Task<bool> IsActiveMaintainerAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(maintainer);
    }

    private sealed class StubNames : IMemberDisplayNameDirectory
    {
        public Task<MemberNameResolution> ResolveAsync(OrganizationId organizationId, IReadOnlyCollection<MemberId> memberIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MemberNameResolution(true, memberIds.ToDictionary(id => id.Value, id => id == Maintainer ? "Maintainer One" : id == Former ? "Former Person" : "New Person")));
    }

    private sealed class StubOrganizationRepository(IReadOnlyList<AdministrationEvent> events) : IOrganizationAdministrationRepository
    {
        public Task<OrganizationAdministrationState?> GetAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrganizationAdministrationWriteResult> BootstrapAsync(OrganizationAdministrationState state, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrganizationAdministrationWriteResult> RenameAsync(OrganizationAdministrationState state, long expectedVersion, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdministrationEvent>> ListEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) => Task.FromResult(events);
    }

    private sealed class StubMembershipRepository(IReadOnlyList<AdministrationEvent> events) : IMembershipRepository
    {
        public Task<IReadOnlyList<MembershipProjection>> ListAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MembershipWriteResult> ApplyAsync(MembershipProjection next, long? expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdministrationEvent>> ListEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) => Task.FromResult(events);
    }

    private sealed class StubRecoveryRepository(IReadOnlyList<AdministrationEvent> events) : IDraftRecoveryRepository
    {
        public Task<RecoveryWriteResult> StartRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, DateTimeOffset deadlineUtc, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RecoveryWriteResult> CancelRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RecoveryEligibleDraft>> ListEligibleAsync(OrganizationId organizationId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReassignDraftResult> ReassignAsync(OrganizationId organizationId, AdrId draftId, MemberId formerAuthorId, MemberId newAuthorId, long expectedVersion, DateTimeOffset now, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdministrationEvent>> ListRecoveryEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) => Task.FromResult(events);
    }
}
