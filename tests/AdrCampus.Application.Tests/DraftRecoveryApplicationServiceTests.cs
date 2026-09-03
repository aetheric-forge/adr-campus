using AdrCampus.Application.Drafts;
using AdrCampus.Application.Identity;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;

namespace AdrCampus.Application.Tests;

public sealed class DraftRecoveryApplicationServiceTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Maintainer = new("maintainer-1");
    private static readonly MemberId FormerAuthor = new("former-author");
    private static readonly MemberId NewAuthor = new("new-author");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartRecoveryStartsExactlyOnceForEachDraftAndIsIdempotent()
    {
        var draft1 = MakeDraft();
        var draft2 = MakeDraft();
        var repository = new StubDraftRecoveryRepository();
        repository.Drafts[draft1.Id.Value] = draft1;
        repository.Drafts[draft2.Id.Value] = draft2;
        var drafts = new StubDraftRepository(FormerAuthor, [Summary(draft1), Summary(draft2)]);
        var service = Create(drafts, repository);

        await service.StartRecoveryForDepartedMemberAsync(Organization, FormerAuthor, Now);
        await service.StartRecoveryForDepartedMemberAsync(Organization, FormerAuthor, Now.AddMinutes(1));

        Assert.Equal(2, repository.Events.Count);
        Assert.All(repository.Drafts.Values, d => Assert.Equal(Now.AddDays(30), d.RecoveryDeadlineUtc));
    }

    [Fact]
    public async Task CancelRecoveryForReturningMemberClearsOpenWindowsOnly()
    {
        var inRecovery = MakeDraft().StartRecovery(Now.AddDays(30), Now);
        var notInRecovery = MakeDraft();
        var repository = new StubDraftRecoveryRepository();
        repository.Drafts[inRecovery.Id.Value] = inRecovery;
        repository.Drafts[notInRecovery.Id.Value] = notInRecovery;
        var drafts = new StubDraftRepository(FormerAuthor, [Summary(inRecovery), Summary(notInRecovery)]);
        var service = Create(drafts, repository);

        await service.CancelRecoveryForReturningMemberAsync(Organization, FormerAuthor, Now.AddDays(1));

        Assert.Null(repository.Drafts[inRecovery.Id.Value].RecoveryDeadlineUtc);
        Assert.Single(repository.Events);
        Assert.Equal(AdministrationEventType.DraftRecoveryCancelled, repository.Events[0].Type);
    }

    [Fact]
    public async Task ReturningMemberCannotRestoreAnExpiredDraft()
    {
        var expired = MakeDraft().StartRecovery(Now.AddDays(-1), Now.AddDays(-31));
        var repository = new StubDraftRecoveryRepository();
        repository.Drafts[expired.Id.Value] = expired;
        var drafts = new StubDraftRepository(FormerAuthor, [Summary(expired)]);
        var service = Create(drafts, repository);

        await service.CancelRecoveryForReturningMemberAsync(Organization, FormerAuthor, Now);

        Assert.NotNull(repository.Drafts[expired.Id.Value].RecoveryDeadlineUtc);
        Assert.Equal(FormerAuthor, repository.Drafts[expired.Id.Value].AuthorId);
        Assert.Empty(repository.Events);
    }

    [Fact]
    public async Task ListEligibleRequiresMaintainerAndResolvesFormerAuthorNames()
    {
        var eligible = MakeDraft().StartRecovery(Now.AddDays(30), Now);
        var repository = new StubDraftRecoveryRepository();
        repository.Drafts[eligible.Id.Value] = eligible;
        var service = Create(new StubDraftRepository(FormerAuthor, []), repository, maintainer: true);

        var authorized = await service.ListEligibleAsync(Organization, Maintainer);
        var unauthorized = await Create(new StubDraftRepository(FormerAuthor, []), repository, maintainer: false).ListEligibleAsync(Organization, Maintainer);

        Assert.True(authorized.IsAuthorized);
        Assert.Single(authorized.Items);
        Assert.Equal("Former Author", authorized.Items[0].FormerAuthorDisplayName);
        Assert.False(unauthorized.IsAuthorized);
        Assert.Empty(unauthorized.Items);
    }

    [Fact]
    public async Task ListEligibleExcludesExpiredDrafts()
    {
        var expired = MakeDraft().StartRecovery(Now.AddDays(-1), Now.AddDays(-31));
        var repository = new StubDraftRecoveryRepository();
        repository.Drafts[expired.Id.Value] = expired;
        var service = Create(new StubDraftRepository(FormerAuthor, []), repository);

        var result = await service.ListEligibleAsync(Organization, Maintainer);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ReassignRequiresMaintainerAndActiveRecipient()
    {
        var draft = MakeDraft().StartRecovery(Now.AddDays(30), Now);
        var repository = new StubDraftRecoveryRepository();
        repository.Drafts[draft.Id.Value] = draft;
        var command = new DraftReassignmentCommand(Organization, Maintainer, draft.Id, FormerAuthor, NewAuthor, draft.Version, OperationId.New());

        var notMaintainer = await Create(new StubDraftRepository(FormerAuthor, []), repository, maintainer: false).ReassignAsync(command);
        var notActiveRecipient = await Create(new StubDraftRepository(FormerAuthor, []), repository, maintainer: true, recipientActive: false).ReassignAsync(command);

        Assert.Equal(DraftReassignmentStatus.Unauthorized, notMaintainer.Status);
        Assert.Equal(DraftReassignmentStatus.RecipientNotActiveMember, notActiveRecipient.Status);
        Assert.Equal(draft, repository.Drafts[draft.Id.Value]);
    }

    [Fact]
    public async Task ReassignSucceedsAndIsIdempotentOnRetryWithTheSameOperation()
    {
        var draft = MakeDraft().StartRecovery(Now.AddDays(30), Now);
        var repository = new StubDraftRecoveryRepository();
        repository.Drafts[draft.Id.Value] = draft;
        var service = Create(new StubDraftRepository(FormerAuthor, []), repository);
        var command = new DraftReassignmentCommand(Organization, Maintainer, draft.Id, FormerAuthor, NewAuthor, draft.Version, OperationId.New());

        var first = await service.ReassignAsync(command);
        var retry = await service.ReassignAsync(command);

        Assert.Equal(DraftReassignmentStatus.Reassigned, first.Status);
        Assert.Equal(DraftReassignmentStatus.AlreadyApplied, retry.Status);
        Assert.Equal(NewAuthor, repository.Drafts[draft.Id.Value].AuthorId);
        Assert.Null(repository.Drafts[draft.Id.Value].RecoveryDeadlineUtc);
        Assert.Single(repository.Events);
    }

    [Fact]
    public async Task ReassignRejectsAnExpiredWindow()
    {
        var draft = MakeDraft().StartRecovery(Now.AddDays(-1), Now.AddDays(-31));
        var repository = new StubDraftRecoveryRepository();
        repository.Drafts[draft.Id.Value] = draft;
        var service = Create(new StubDraftRepository(FormerAuthor, []), repository);
        var command = new DraftReassignmentCommand(Organization, Maintainer, draft.Id, FormerAuthor, NewAuthor, draft.Version, OperationId.New());

        var result = await service.ReassignAsync(command);

        Assert.Equal(DraftReassignmentStatus.Expired, result.Status);
    }

    [Fact]
    public async Task ReassignRejectsAStaleVersion()
    {
        var draft = MakeDraft().StartRecovery(Now.AddDays(30), Now);
        var repository = new StubDraftRecoveryRepository();
        repository.Drafts[draft.Id.Value] = draft;
        var service = Create(new StubDraftRepository(FormerAuthor, []), repository);
        var command = new DraftReassignmentCommand(Organization, Maintainer, draft.Id, FormerAuthor, NewAuthor, draft.Version + 1, OperationId.New());

        var result = await service.ReassignAsync(command);

        Assert.Equal(DraftReassignmentStatus.Conflict, result.Status);
    }

    private static AdrDraft MakeDraft() => AdrDraft.Create(AdrId.New(), Organization, FormerAuthor, new DraftContent("Choose a database"), Now.AddDays(-40));
    private static DraftSummary Summary(AdrDraft draft) => new(draft.Id, draft.Content.Title, draft.CreatedAtUtc, draft.ModifiedAtUtc, draft.Version);

    private static DraftRecoveryApplicationService Create(StubDraftRepository drafts, StubDraftRecoveryRepository repository, bool maintainer = true, bool recipientActive = true) =>
        new(drafts, repository, new StubAuthority(maintainer, recipientActive), new StubDisplayNames(), new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private sealed class StubAuthority(bool maintainer, bool recipientActive) : IMemberAuthority
    {
        public Task<bool> IsActiveMemberAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult(memberId == Maintainer ? maintainer : recipientActive);
        public Task<bool> IsActiveMaintainerAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(maintainer);
    }

    private sealed class StubDisplayNames : IMemberDisplayNameDirectory
    {
        public Task<MemberNameResolution> ResolveAsync(OrganizationId organizationId, IReadOnlyCollection<MemberId> memberIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MemberNameResolution(true, memberIds.ToDictionary(id => id.Value, _ => "Former Author")));
    }

    private sealed class StubDraftRepository(MemberId author, IReadOnlyList<DraftSummary> drafts) : IDraftRepository
    {
        public Task<DraftWriteResult> CreateAsync(AdrDraft draft, OperationId operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AdrDraft?> GetByAuthorAsync(OrganizationId organizationId, MemberId authorId, AdrId draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DraftSummary>> ListByAuthorAsync(OrganizationId organizationId, MemberId authorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(authorId == author ? drafts : (IReadOnlyList<DraftSummary>)[]);
        public Task<DraftWriteResult> SaveRevisionAsync(AdrDraft draft, long expectedPersistedVersion, OperationId operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubDraftRecoveryRepository : IDraftRecoveryRepository
    {
        public Dictionary<Guid, AdrDraft> Drafts { get; } = [];
        public List<AdministrationEvent> Events { get; } = [];
        private readonly Dictionary<Guid, (Guid DraftId, string FormerAuthorId, string NewAuthorId, long ExpectedVersion, AdrDraft Draft)> operations = [];

        public Task<RecoveryWriteResult> StartRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, DateTimeOffset deadlineUtc, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default)
        {
            if (!Drafts.TryGetValue(draftId.Value, out var current)) return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.NotFound, null));
            if (current.AuthorId != authorId) return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.Conflict, current));
            if (current.RecoveryDeadlineUtc is not null) return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.AlreadyApplied, current));
            if (current.Version != expectedVersion) return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.Conflict, current));
            var next = current.StartRecovery(deadlineUtc, administrationEvent.OccurredAtUtc);
            Drafts[draftId.Value] = next; Events.Add(administrationEvent);
            return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.Applied, next));
        }

        public Task<RecoveryWriteResult> CancelRecoveryAsync(OrganizationId organizationId, AdrId draftId, MemberId authorId, long expectedVersion, AdministrationEvent administrationEvent, CancellationToken cancellationToken = default)
        {
            if (!Drafts.TryGetValue(draftId.Value, out var current)) return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.NotFound, null));
            if (current.AuthorId != authorId) return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.Conflict, current));
            if (current.IsExpired(administrationEvent.OccurredAtUtc)) return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.Expired, current));
            if (current.RecoveryDeadlineUtc is null) return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.AlreadyApplied, current));
            if (current.Version != expectedVersion) return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.Conflict, current));
            var next = current.CancelRecovery(administrationEvent.OccurredAtUtc);
            Drafts[draftId.Value] = next; Events.Add(administrationEvent);
            return Task.FromResult(new RecoveryWriteResult(RecoveryWriteStatus.Applied, next));
        }

        public Task<IReadOnlyList<RecoveryEligibleDraft>> ListEligibleAsync(OrganizationId organizationId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RecoveryEligibleDraft>>(Drafts.Values
                .Where(d => d.OrganizationId == organizationId && d.RecoveryDeadlineUtc is not null && d.RecoveryDeadlineUtc > now)
                .Select(d => new RecoveryEligibleDraft(d.Id, d.Content.Title, d.AuthorId, d.RecoveryDeadlineUtc!.Value, d.Version))
                .ToArray());

        public Task<ReassignDraftResult> ReassignAsync(OrganizationId organizationId, AdrId draftId, MemberId formerAuthorId, MemberId newAuthorId, long expectedVersion, DateTimeOffset now, AdministrationEvent administrationEvent, OperationId operationId, CancellationToken cancellationToken = default)
        {
            if (operations.TryGetValue(operationId.Value, out var prior))
            {
                var same = prior.DraftId == draftId.Value && prior.FormerAuthorId == formerAuthorId.Value && prior.NewAuthorId == newAuthorId.Value && prior.ExpectedVersion == expectedVersion;
                return Task.FromResult(same ? new ReassignDraftResult(ReassignDraftStatus.AlreadyApplied, prior.Draft) : new ReassignDraftResult(ReassignDraftStatus.OperationMismatch, null));
            }
            if (!Drafts.TryGetValue(draftId.Value, out var current)) return Task.FromResult(new ReassignDraftResult(ReassignDraftStatus.NotFound, null));
            if (current.AuthorId != formerAuthorId || current.Version != expectedVersion || current.RecoveryDeadlineUtc is null) return Task.FromResult(new ReassignDraftResult(ReassignDraftStatus.Conflict, current));
            if (current.IsExpired(now)) return Task.FromResult(new ReassignDraftResult(ReassignDraftStatus.Expired, current));
            var reassigned = current.Reassign(newAuthorId, now);
            Drafts[draftId.Value] = reassigned; Events.Add(administrationEvent);
            operations[operationId.Value] = (draftId.Value, formerAuthorId.Value, newAuthorId.Value, expectedVersion, reassigned);
            return Task.FromResult(new ReassignDraftResult(ReassignDraftStatus.Reassigned, reassigned));
        }

        public Task<IReadOnlyList<AdministrationEvent>> ListRecoveryEventsAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdministrationEvent>>(Events.Where(e => e.OrganizationId == organizationId).ToArray());
    }
}
