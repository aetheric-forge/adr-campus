using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Tests.Domain;

public sealed class AdrDraftTests
{
    private static readonly AdrId DraftId = new(Guid.Parse("4a05c89a-21eb-4f15-b37d-75a2adb68a14"));
    private static readonly OrganizationId OrganizationId = new("aetheric-forge");
    private static readonly MemberId AuthorId = new("keycloak-subject-1");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreatesAnIncompletePrivateDraftWithStableMetadata()
    {
        var content = new DraftContent("Choose a database");
        var draft = AdrDraft.Create(DraftId, OrganizationId, AuthorId, content, CreatedAt);

        Assert.Equal(DraftId, draft.Id);
        Assert.Equal(OrganizationId, draft.OrganizationId);
        Assert.Equal(AuthorId, draft.AuthorId);
        Assert.Equal(AdrLifecycleStatus.Draft, draft.Status);
        Assert.Equal(CreatedAt, draft.CreatedAtUtc);
        Assert.Equal(CreatedAt, draft.ModifiedAtUtc);
        Assert.Equal(1, draft.Version);
        Assert.Empty(draft.Content.Context);
        Assert.Empty(draft.Content.Decision);
        Assert.Empty(draft.Content.Consequences);
    }

    [Fact]
    public void RevisionPreservesIdentityAuthorshipCreationAndStatus()
    {
        var original = AdrDraft.Create(
            DraftId,
            OrganizationId,
            AuthorId,
            new DraftContent("Choose a database"),
            CreatedAt);
        var modifiedAt = CreatedAt.AddMinutes(10);

        var revised = original.Revise(
            new DraftContent("Choose PostgreSQL", "Context", "Decision", "Consequences"),
            expectedVersion: 1,
            modifiedAt);

        Assert.Equal(original.Id, revised.Id);
        Assert.Equal(original.OrganizationId, revised.OrganizationId);
        Assert.Equal(original.AuthorId, revised.AuthorId);
        Assert.Equal(original.CreatedAtUtc, revised.CreatedAtUtc);
        Assert.Equal(AdrLifecycleStatus.Draft, revised.Status);
        Assert.Equal(modifiedAt, revised.ModifiedAtUtc);
        Assert.Equal(2, revised.Version);
        Assert.Equal("Choose PostgreSQL", revised.Content.Title.Value);
    }

    [Fact]
    public void RevisionDoesNotMutateThePreviouslySavedValue()
    {
        var original = AdrDraft.Create(
            DraftId,
            OrganizationId,
            AuthorId,
            new DraftContent("Choose a database"),
            CreatedAt);

        _ = original.Revise(new DraftContent("Choose PostgreSQL"), 1, CreatedAt.AddMinutes(1));

        Assert.Equal("Choose a database", original.Content.Title.Value);
        Assert.Equal(1, original.Version);
    }

    [Fact]
    public void RejectsAStaleRevision()
    {
        var draft = AdrDraft.Create(
            DraftId,
            OrganizationId,
            AuthorId,
            new DraftContent("Choose a database"),
            CreatedAt);

        var exception = Assert.Throws<DraftConcurrencyException>(() =>
            draft.Revise(new DraftContent("Choose PostgreSQL"), expectedVersion: 0, CreatedAt.AddMinutes(1)));

        Assert.Equal(DraftId, exception.DraftId);
        Assert.Equal(0, exception.ExpectedVersion);
        Assert.Equal(1, exception.CurrentVersion);
    }

    [Fact]
    public void RejectsAModificationTimeThatMovesBackwards()
    {
        var draft = AdrDraft.Create(
            DraftId,
            OrganizationId,
            AuthorId,
            new DraftContent("Choose a database"),
            CreatedAt);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            draft.Revise(new DraftContent("Choose PostgreSQL"), 1, CreatedAt.AddSeconds(-1)));
    }
}
