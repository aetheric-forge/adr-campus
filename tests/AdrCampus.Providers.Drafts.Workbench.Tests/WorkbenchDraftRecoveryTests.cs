using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Providers.Drafts.Workbench;
using AethericForge.Runtime.Providers.Staging.InMemory;

namespace AdrCampus.Providers.Drafts.Workbench.Tests;

public sealed class WorkbenchDraftRecoveryTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId FormerAuthor = new("former-author");
    private static readonly MemberId NewAuthor = new("new-author");
    private static readonly MemberId OtherAuthor = new("other-author");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static AdrDraft Draft() => AdrDraft.Create(AdrId.New(), Organization, FormerAuthor, new DraftContent("Choose a database"), Now.AddDays(-40));

    private static AdministrationEvent Event(AdministrationEventType type, DateTimeOffset at, AdrId draftId, string? previous = null, string? next = null) =>
        new(Guid.NewGuid(), Organization, type, at, "SSO observation", SubjectId: FormerAuthor, PreviousValue: previous, NewValue: next, DraftId: draftId);

    [Fact]
    public async Task StartRecoveryIsAtomicVersionedAndIdempotent()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var draft = Draft();
        await repository.CreateAsync(draft, OperationId.New());
        var deadline = Now.AddDays(30);

        var first = await repository.StartRecoveryAsync(Organization, draft.Id, FormerAuthor, draft.Version, deadline, Event(AdministrationEventType.DraftRecoveryStarted, Now, draft.Id, next: deadline.ToString("O")));
        var retry = await repository.StartRecoveryAsync(Organization, draft.Id, FormerAuthor, draft.Version, deadline, Event(AdministrationEventType.DraftRecoveryStarted, Now, draft.Id, next: deadline.ToString("O")));

        Assert.Equal(RecoveryWriteStatus.Applied, first.Status);
        Assert.Equal(RecoveryWriteStatus.AlreadyApplied, retry.Status);
        Assert.Equal(deadline, first.Draft!.RecoveryDeadlineUtc);
        Assert.Single(await repository.ListRecoveryEventsAsync(Organization));
    }

    [Fact]
    public async Task CancelRecoveryIsIdempotentAndPreservesOrdinaryAccess()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var draft = Draft();
        await repository.CreateAsync(draft, OperationId.New());
        var started = await repository.StartRecoveryAsync(Organization, draft.Id, FormerAuthor, draft.Version, Now.AddDays(30), Event(AdministrationEventType.DraftRecoveryStarted, Now, draft.Id));

        var cancelled = await repository.CancelRecoveryAsync(Organization, draft.Id, FormerAuthor, started.Draft!.Version, Event(AdministrationEventType.DraftRecoveryCancelled, Now.AddHours(1), draft.Id));
        var retry = await repository.CancelRecoveryAsync(Organization, draft.Id, FormerAuthor, started.Draft!.Version, Event(AdministrationEventType.DraftRecoveryCancelled, Now.AddHours(1), draft.Id));

        Assert.Equal(RecoveryWriteStatus.Applied, cancelled.Status);
        Assert.Equal(RecoveryWriteStatus.AlreadyApplied, retry.Status);
        Assert.Null(cancelled.Draft!.RecoveryDeadlineUtc);
        Assert.Equal(FormerAuthor, (await repository.GetByAuthorAsync(Organization, FormerAuthor, draft.Id))!.AuthorId);
    }

    [Fact]
    public async Task CancelRecoveryRefusesAnExpiredWindowAndDoesNotRestoreAccess()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var draft = Draft();
        await repository.CreateAsync(draft, OperationId.New());
        var started = await repository.StartRecoveryAsync(Organization, draft.Id, FormerAuthor, draft.Version, Now.AddDays(-1), Event(AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-31), draft.Id));

        var result = await repository.CancelRecoveryAsync(Organization, draft.Id, FormerAuthor, started.Draft!.Version, Event(AdministrationEventType.DraftRecoveryCancelled, Now, draft.Id));

        Assert.Equal(RecoveryWriteStatus.Expired, result.Status);
        Assert.NotNull(result.Draft!.RecoveryDeadlineUtc);
        Assert.Equal(FormerAuthor, (await repository.GetByAuthorAsync(Organization, FormerAuthor, draft.Id))!.AuthorId);
        Assert.Empty(await repository.ListEligibleAsync(Organization, Now));
    }

    [Fact]
    public async Task ListEligibleExcludesUnrelatedAndExpiredDrafts()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var eligible = Draft();
        var expired = Draft();
        var neverStarted = Draft();
        await repository.CreateAsync(eligible, OperationId.New());
        await repository.CreateAsync(expired, OperationId.New());
        await repository.CreateAsync(neverStarted, OperationId.New());
        await repository.StartRecoveryAsync(Organization, eligible.Id, FormerAuthor, eligible.Version, Now.AddDays(30), Event(AdministrationEventType.DraftRecoveryStarted, Now, eligible.Id));
        await repository.StartRecoveryAsync(Organization, expired.Id, FormerAuthor, expired.Version, Now.AddDays(-1), Event(AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-31), expired.Id));

        var result = await repository.ListEligibleAsync(Organization, Now);

        var item = Assert.Single(result);
        Assert.Equal(eligible.Id, item.Id);
        Assert.Equal(FormerAuthor, item.FormerAuthorId);
    }

    [Fact]
    public async Task ReassignIsAtomicVersionedIdempotentAndPreservesContent()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var draft = Draft();
        await repository.CreateAsync(draft, OperationId.New());
        var started = await repository.StartRecoveryAsync(Organization, draft.Id, FormerAuthor, draft.Version, Now.AddDays(30), Event(AdministrationEventType.DraftRecoveryStarted, Now, draft.Id));
        var operation = OperationId.New();
        var evt = Event(AdministrationEventType.DraftReassigned, Now.AddHours(1), draft.Id, FormerAuthor.Value, NewAuthor.Value);

        var first = await repository.ReassignAsync(Organization, draft.Id, FormerAuthor, NewAuthor, started.Draft!.Version, Now.AddHours(1), evt, operation);
        var retry = await new WorkbenchDraftRepository(staging).ReassignAsync(Organization, draft.Id, FormerAuthor, NewAuthor, started.Draft!.Version, Now.AddHours(1), evt, operation);

        Assert.Equal(ReassignDraftStatus.Reassigned, first.Status);
        Assert.Equal(ReassignDraftStatus.AlreadyApplied, retry.Status);
        Assert.Equal(NewAuthor, first.Draft!.AuthorId);
        Assert.Null(first.Draft.RecoveryDeadlineUtc);
        Assert.Equal(draft.Content, first.Draft.Content);
        Assert.Equal(draft.CreatedAtUtc, first.Draft.CreatedAtUtc);
        Assert.Equal(2, (await repository.ListRecoveryEventsAsync(Organization)).Count);
        Assert.NotNull(await repository.GetByAuthorAsync(Organization, NewAuthor, draft.Id));
        Assert.Null(await repository.GetByAuthorAsync(Organization, FormerAuthor, draft.Id));
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentReassignmentsWins()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var draft = Draft();
        await repository.CreateAsync(draft, OperationId.New());
        var started = await repository.StartRecoveryAsync(Organization, draft.Id, FormerAuthor, draft.Version, Now.AddDays(30), Event(AdministrationEventType.DraftRecoveryStarted, Now, draft.Id));

        var firstAttempt = repository.ReassignAsync(Organization, draft.Id, FormerAuthor, NewAuthor, started.Draft!.Version, Now.AddHours(1), Event(AdministrationEventType.DraftReassigned, Now.AddHours(1), draft.Id), OperationId.New());
        var secondAttempt = repository.ReassignAsync(Organization, draft.Id, FormerAuthor, OtherAuthor, started.Draft!.Version, Now.AddHours(1), Event(AdministrationEventType.DraftReassigned, Now.AddHours(1), draft.Id), OperationId.New());
        var results = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.Single(results, r => r.Status == ReassignDraftStatus.Reassigned);
        Assert.Single(results, r => r.Status == ReassignDraftStatus.Conflict);
        var final = await repository.GetByAuthorAsync(Organization, NewAuthor, draft.Id) ?? await repository.GetByAuthorAsync(Organization, OtherAuthor, draft.Id);
        Assert.NotNull(final);
    }

    [Fact]
    public async Task ReassignRejectsAnExpiredWindow()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var draft = Draft();
        await repository.CreateAsync(draft, OperationId.New());
        var started = await repository.StartRecoveryAsync(Organization, draft.Id, FormerAuthor, draft.Version, Now.AddDays(-1), Event(AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-31), draft.Id));

        var result = await repository.ReassignAsync(Organization, draft.Id, FormerAuthor, NewAuthor, started.Draft!.Version, Now, Event(AdministrationEventType.DraftReassigned, Now, draft.Id), OperationId.New());

        Assert.Equal(ReassignDraftStatus.Expired, result.Status);
    }

    [Fact]
    public async Task ListExpiredReturnsOnlyExpiredUnreassignedDraftsBoundedByBatchSize()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var expired1 = Draft();
        var expired2 = Draft();
        var notExpired = Draft();
        await repository.CreateAsync(expired1, OperationId.New());
        await repository.CreateAsync(expired2, OperationId.New());
        await repository.CreateAsync(notExpired, OperationId.New());
        await repository.StartRecoveryAsync(Organization, expired1.Id, FormerAuthor, expired1.Version, Now.AddDays(-1), Event(AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-31), expired1.Id));
        await repository.StartRecoveryAsync(Organization, expired2.Id, FormerAuthor, expired2.Version, Now.AddDays(-2), Event(AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-32), expired2.Id));
        await repository.StartRecoveryAsync(Organization, notExpired.Id, FormerAuthor, notExpired.Version, Now.AddDays(30), Event(AdministrationEventType.DraftRecoveryStarted, Now, notExpired.Id));

        var all = await repository.ListExpiredAsync(Organization, Now, batchSize: 10);
        var bounded = await repository.ListExpiredAsync(Organization, Now, batchSize: 1);

        Assert.Equal(2, all.Count);
        Assert.Contains(expired1.Id, all);
        Assert.Contains(expired2.Id, all);
        Assert.Single(bounded);
    }

    [Fact]
    public async Task PurgeBatchRemovesContentButRetainsAnExpirationEvent()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var expired = Draft();
        var notExpired = Draft();
        await repository.CreateAsync(expired, OperationId.New());
        await repository.CreateAsync(notExpired, OperationId.New());
        await repository.StartRecoveryAsync(Organization, expired.Id, FormerAuthor, expired.Version, Now.AddDays(-1), Event(AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-31), expired.Id));

        var purged = await repository.PurgeBatchAsync(Organization, [expired.Id, notExpired.Id], Now);

        Assert.Equal(1, purged);
        Assert.Null(await repository.GetByAuthorAsync(Organization, FormerAuthor, expired.Id));
        Assert.NotNull(await repository.GetByAuthorAsync(Organization, FormerAuthor, notExpired.Id));
        var events = await repository.ListRecoveryEventsAsync(Organization);
        var expirationEvent = Assert.Single(events, e => e.Type == AdministrationEventType.DraftExpired);
        Assert.Equal(expired.Id, expirationEvent.DraftId);
        Assert.Null(expirationEvent.PreviousValue);
    }

    [Fact]
    public async Task PurgingAnAlreadyPurgedDraftIsANoOp()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var repository = new WorkbenchDraftRepository(staging);
        var expired = Draft();
        await repository.CreateAsync(expired, OperationId.New());
        await repository.StartRecoveryAsync(Organization, expired.Id, FormerAuthor, expired.Version, Now.AddDays(-1), Event(AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-31), expired.Id));

        var first = await repository.PurgeBatchAsync(Organization, [expired.Id], Now);
        var retry = await repository.PurgeBatchAsync(Organization, [expired.Id], Now);

        Assert.Equal(1, first);
        Assert.Equal(0, retry);
        Assert.Single(await repository.ListRecoveryEventsAsync(Organization), e => e.Type == AdministrationEventType.DraftExpired);
    }

    [Fact]
    public async Task PurgeStateAndEventsSurviveRepositoryRecomposition()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var expired = Draft();
        await new WorkbenchDraftRepository(staging).CreateAsync(expired, OperationId.New());
        await new WorkbenchDraftRepository(staging).StartRecoveryAsync(Organization, expired.Id, FormerAuthor, expired.Version, Now.AddDays(-1), Event(AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-31), expired.Id));
        await new WorkbenchDraftRepository(staging).PurgeBatchAsync(Organization, [expired.Id], Now);

        var recomposed = new WorkbenchDraftRepository(staging);

        Assert.Null(await recomposed.GetByAuthorAsync(Organization, FormerAuthor, expired.Id));
        Assert.Empty(await recomposed.ListEligibleAsync(Organization, Now));
        Assert.Contains(await recomposed.ListRecoveryEventsAsync(Organization), e => e.Type == AdministrationEventType.DraftExpired);
    }

    [Fact]
    public async Task RecoveryStateAndEventsSurviveRepositoryRecomposition()
    {
        var staging = new InMemoryStagingProvider("recovery");
        var draft = Draft();
        await new WorkbenchDraftRepository(staging).CreateAsync(draft, OperationId.New());
        await new WorkbenchDraftRepository(staging).StartRecoveryAsync(Organization, draft.Id, FormerAuthor, draft.Version, Now.AddDays(30), Event(AdministrationEventType.DraftRecoveryStarted, Now, draft.Id));

        var recomposed = new WorkbenchDraftRepository(staging);
        var eligible = await recomposed.ListEligibleAsync(Organization, Now);
        var events = await recomposed.ListRecoveryEventsAsync(Organization);

        Assert.Single(eligible);
        Assert.Single(events);
        Assert.Equal(AdministrationEventType.DraftRecoveryStarted, events[0].Type);
    }
}
