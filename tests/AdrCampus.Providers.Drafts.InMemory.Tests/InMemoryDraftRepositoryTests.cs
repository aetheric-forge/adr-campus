using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Providers.Drafts.InMemory;

namespace AdrCampus.Providers.Drafts.InMemory.Tests;

public sealed class InMemoryDraftRepositoryTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly OrganizationId OtherOrganization = new("other-organization");
    private static readonly MemberId Author = new("author-1");
    private static readonly MemberId OtherAuthor = new("author-2");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatesAndReturnsADraftToItsAuthor()
    {
        var repository = new InMemoryDraftRepository();
        var draft = CreateDraft();

        var result = await repository.CreateAsync(draft, OperationId.New());
        var persisted = await repository.GetByAuthorAsync(Organization, Author, draft.Id);

        Assert.Equal(DraftWriteStatus.Created, result.Status);
        Assert.Equal(draft, persisted);
    }

    [Fact]
    public async Task DoesNotRevealADraftToAnotherAuthor()
    {
        var repository = new InMemoryDraftRepository();
        var draft = CreateDraft();
        await repository.CreateAsync(draft, OperationId.New());

        var persisted = await repository.GetByAuthorAsync(Organization, OtherAuthor, draft.Id);

        Assert.Null(persisted);
    }

    [Fact]
    public async Task DoesNotRevealADraftAcrossOrganizations()
    {
        var repository = new InMemoryDraftRepository();
        var draft = CreateDraft();
        await repository.CreateAsync(draft, OperationId.New());

        var persisted = await repository.GetByAuthorAsync(OtherOrganization, Author, draft.Id);

        Assert.Null(persisted);
    }

    [Fact]
    public async Task ReplaysTheSameCreateOperationIdempotently()
    {
        var repository = new InMemoryDraftRepository();
        var draft = CreateDraft();
        var operationId = OperationId.New();
        await repository.CreateAsync(draft, operationId);

        var replay = await repository.CreateAsync(draft, operationId);

        Assert.Equal(DraftWriteStatus.AlreadyApplied, replay.Status);
        Assert.Equal(draft, replay.Draft);
    }

    [Fact]
    public async Task RejectsReusingAnOperationIdForDifferentContent()
    {
        var repository = new InMemoryDraftRepository();
        var operationId = OperationId.New();
        await repository.CreateAsync(CreateDraft(), operationId);

        var differentDraft = CreateDraft(title: "Choose PostgreSQL");
        var result = await repository.CreateAsync(differentDraft, operationId);

        Assert.Equal(DraftWriteStatus.OperationMismatch, result.Status);
        Assert.Null(result.Draft);
    }

    [Fact]
    public async Task RejectsCreatingTheSameOrganizationScopedIdentifierTwice()
    {
        var repository = new InMemoryDraftRepository();
        var draft = CreateDraft();
        await repository.CreateAsync(draft, OperationId.New());

        var result = await repository.CreateAsync(draft, OperationId.New());

        Assert.Equal(DraftWriteStatus.Conflict, result.Status);
        Assert.Equal(draft, result.Draft);
    }

    [Fact]
    public async Task SavesARevisionUsingThePersistedVersion()
    {
        var repository = new InMemoryDraftRepository();
        var draft = CreateDraft();
        await repository.CreateAsync(draft, OperationId.New());
        var revised = draft.Revise(new DraftContent("Choose PostgreSQL"), 1, CreatedAt.AddMinutes(1));

        var result = await repository.SaveRevisionAsync(revised, 1, OperationId.New());

        Assert.Equal(DraftWriteStatus.Saved, result.Status);
        Assert.Equal(revised, await repository.GetByAuthorAsync(Organization, Author, draft.Id));
    }

    [Fact]
    public async Task PreservesTheNewerValueWhenAStaleRevisionIsSaved()
    {
        var repository = new InMemoryDraftRepository();
        var draft = CreateDraft();
        await repository.CreateAsync(draft, OperationId.New());
        var firstRevision = draft.Revise(new DraftContent("Choose PostgreSQL"), 1, CreatedAt.AddMinutes(1));
        await repository.SaveRevisionAsync(firstRevision, 1, OperationId.New());
        var staleRevision = draft.Revise(new DraftContent("Choose SQL Server"), 1, CreatedAt.AddMinutes(2));

        var result = await repository.SaveRevisionAsync(staleRevision, 1, OperationId.New());

        Assert.Equal(DraftWriteStatus.Conflict, result.Status);
        Assert.Equal(firstRevision, result.Draft);
        Assert.Equal(firstRevision, await repository.GetByAuthorAsync(Organization, Author, draft.Id));
    }

    [Fact]
    public async Task ReplaysTheSameRevisionIdempotentlyAfterItWasSaved()
    {
        var repository = new InMemoryDraftRepository();
        var draft = CreateDraft();
        await repository.CreateAsync(draft, OperationId.New());
        var revision = draft.Revise(new DraftContent("Choose PostgreSQL"), 1, CreatedAt.AddMinutes(1));
        var operationId = OperationId.New();
        await repository.SaveRevisionAsync(revision, 1, operationId);

        var replay = await repository.SaveRevisionAsync(revision, 1, operationId);

        Assert.Equal(DraftWriteStatus.AlreadyApplied, replay.Status);
        Assert.Equal(revision, replay.Draft);
    }

    [Fact]
    public async Task RejectsChangingAuthorshipDuringRevision()
    {
        var repository = new InMemoryDraftRepository();
        var draft = CreateDraft();
        await repository.CreateAsync(draft, OperationId.New());
        var impostorDraft = AdrDraft.Create(
                draft.Id,
                Organization,
                OtherAuthor,
                new DraftContent("Choose a database"),
                CreatedAt)
            .Revise(new DraftContent("Choose PostgreSQL"), 1, CreatedAt.AddMinutes(1));

        var result = await repository.SaveRevisionAsync(impostorDraft, 1, OperationId.New());

        Assert.Equal(DraftWriteStatus.Conflict, result.Status);
        Assert.Equal(draft, await repository.GetByAuthorAsync(Organization, Author, draft.Id));
    }

    [Fact]
    public async Task ListsOnlyTheRequestedAuthorsDraftsInDeterministicOrder()
    {
        var repository = new InMemoryDraftRepository();
        var older = CreateDraft(Guid.Parse("10000000-0000-0000-0000-000000000000"), "Older decision", Author);
        var newerHighId = CreateDraft(Guid.Parse("30000000-0000-0000-0000-000000000000"), "Newer decision B", Author, CreatedAt.AddMinutes(1));
        var newerLowId = CreateDraft(Guid.Parse("20000000-0000-0000-0000-000000000000"), "Newer decision A", Author, CreatedAt.AddMinutes(1));
        var someoneElses = CreateDraft(Guid.NewGuid(), "Private decision", OtherAuthor, CreatedAt.AddMinutes(2));
        foreach (var draft in new[] { older, newerHighId, newerLowId, someoneElses })
        {
            await repository.CreateAsync(draft, OperationId.New());
        }

        var results = await repository.ListByAuthorAsync(Organization, Author);

        Assert.Equal(new[] { newerLowId.Id, newerHighId.Id, older.Id }, results.Select(result => result.Id));
    }

    [Fact]
    public async Task HonorsCancellationBeforeEveryOperation()
    {
        var repository = new InMemoryDraftRepository();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repository.CreateAsync(CreateDraft(), OperationId.New(), source.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repository.GetByAuthorAsync(Organization, Author, AdrId.New(), source.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repository.ListByAuthorAsync(Organization, Author, source.Token));
    }

    private static AdrDraft CreateDraft(
        Guid? id = null,
        string title = "Choose a database",
        MemberId? author = null,
        DateTimeOffset? createdAt = null) =>
        AdrDraft.Create(
            new AdrId(id ?? Guid.Parse("4a05c89a-21eb-4f15-b37d-75a2adb68a14")),
            Organization,
            author ?? Author,
            new DraftContent(title),
            createdAt ?? CreatedAt);
}
