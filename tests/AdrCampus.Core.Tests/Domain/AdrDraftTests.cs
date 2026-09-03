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

    [Fact]
    public void ReplacementTargetIsPrivateDraftStateAndCanBeChangedOrRemoved()
    {
        var firstTarget = AdrId.New();
        var secondTarget = AdrId.New();
        var draft = AdrDraft.Create(DraftId, OrganizationId, AuthorId, new DraftContent("Replace database decision"), CreatedAt, firstTarget);

        var changed = draft.Revise(draft.Content, 1, CreatedAt.AddMinutes(1), secondTarget);
        var removed = changed.Revise(changed.Content, 2, CreatedAt.AddMinutes(2));

        Assert.Equal(firstTarget, draft.IntendedSupersessionTargetId);
        Assert.Equal(secondTarget, changed.IntendedSupersessionTargetId);
        Assert.Null(removed.IntendedSupersessionTargetId);
    }

    [Fact]
    public void ReplacementCannotTargetItself()
    {
        Assert.Throws<ArgumentException>(() => AdrDraft.Create(DraftId, OrganizationId, AuthorId, new DraftContent("Replace database decision"), CreatedAt, DraftId));
    }

    [Fact]
    public void StartRecoverySetsDeadlineAndAdvancesVersion()
    {
        var draft = AdrDraft.Create(DraftId, OrganizationId, AuthorId, new DraftContent("Choose a database"), CreatedAt);
        var deadline = CreatedAt.AddDays(30);

        var inRecovery = draft.StartRecovery(deadline, CreatedAt.AddMinutes(5));

        Assert.Equal(deadline, inRecovery.RecoveryDeadlineUtc);
        Assert.Equal(2, inRecovery.Version);
        Assert.Equal(AuthorId, inRecovery.AuthorId);
        Assert.False(draft.IsExpired(CreatedAt));
    }

    [Fact]
    public void StartRecoveryRejectsADraftAlreadyInRecovery()
    {
        var draft = AdrDraft.Create(DraftId, OrganizationId, AuthorId, new DraftContent("Choose a database"), CreatedAt)
            .StartRecovery(CreatedAt.AddDays(30), CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => draft.StartRecovery(CreatedAt.AddDays(60), CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void CancelRecoveryIsIdempotentAndClearsTheDeadline()
    {
        var draft = AdrDraft.Create(DraftId, OrganizationId, AuthorId, new DraftContent("Choose a database"), CreatedAt);
        var inRecovery = draft.StartRecovery(CreatedAt.AddDays(30), CreatedAt.AddMinutes(1));

        var cancelled = inRecovery.CancelRecovery(CreatedAt.AddMinutes(2));
        var noOp = draft.CancelRecovery(CreatedAt.AddMinutes(3));

        Assert.Null(cancelled.RecoveryDeadlineUtc);
        Assert.Equal(3, cancelled.Version);
        Assert.Same(draft, noOp);
    }

    [Fact]
    public void ReassignChangesAuthorAndClearsRecoveryWhilePreservingContent()
    {
        var newAuthor = new MemberId("keycloak-subject-2");
        var draft = AdrDraft.Create(DraftId, OrganizationId, AuthorId, new DraftContent("Choose a database", "Context", "Decision", "Consequences"), CreatedAt)
            .StartRecovery(CreatedAt.AddDays(30), CreatedAt.AddMinutes(1));

        var reassigned = draft.Reassign(newAuthor, CreatedAt.AddMinutes(2));

        Assert.Equal(newAuthor, reassigned.AuthorId);
        Assert.Null(reassigned.RecoveryDeadlineUtc);
        Assert.Equal(draft.Id, reassigned.Id);
        Assert.Equal(draft.CreatedAtUtc, reassigned.CreatedAtUtc);
        Assert.Equal(draft.Content, reassigned.Content);
        Assert.Equal(3, reassigned.Version);
    }

    [Fact]
    public void ReassignRequiresAnOpenRecoveryWindow()
    {
        var draft = AdrDraft.Create(DraftId, OrganizationId, AuthorId, new DraftContent("Choose a database"), CreatedAt);
        Assert.Throws<InvalidOperationException>(() => draft.Reassign(new MemberId("keycloak-subject-2"), CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void IsExpiredComparesAgainstTheDeadline()
    {
        var draft = AdrDraft.Create(DraftId, OrganizationId, AuthorId, new DraftContent("Choose a database"), CreatedAt)
            .StartRecovery(CreatedAt.AddDays(30), CreatedAt.AddMinutes(1));

        Assert.False(draft.IsExpired(CreatedAt.AddDays(29)));
        Assert.True(draft.IsExpired(CreatedAt.AddDays(30)));
        Assert.True(draft.IsExpired(CreatedAt.AddDays(31)));
    }
}
