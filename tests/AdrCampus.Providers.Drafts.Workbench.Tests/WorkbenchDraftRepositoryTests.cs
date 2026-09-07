using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;

namespace AdrCampus.Providers.Drafts.Workbench.Tests;

public sealed class WorkbenchDraftRepositoryTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Author = new("author-1");
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DraftsSurviveRepositoryRecomposition()
    {
        var staging = WorkbenchTestSupport.NewStagingProvider();
        var first = new WorkbenchDraftRepository(WorkbenchTestSupport.Artificer(staging));
        var draft = Draft();
        await first.CreateAsync(draft, OperationId.New());

        var recomposed = new WorkbenchDraftRepository(WorkbenchTestSupport.Artificer(staging));
        var loaded = await recomposed.GetByAuthorAsync(Organization, Author, draft.Id);

        Assert.Equal(draft, loaded);
        Assert.Single(await recomposed.ListByAuthorAsync(Organization, Author));
    }

    [Fact]
    public async Task OperationHistorySurvivesRepositoryRecomposition()
    {
        var staging = WorkbenchTestSupport.NewStagingProvider();
        var draft = Draft();
        var operationId = OperationId.New();
        await new WorkbenchDraftRepository(WorkbenchTestSupport.Artificer(staging)).CreateAsync(draft, operationId);

        var replay = await new WorkbenchDraftRepository(WorkbenchTestSupport.Artificer(staging)).CreateAsync(draft, operationId);

        Assert.Equal(DraftWriteStatus.AlreadyApplied, replay.Status);
    }

    [Fact]
    public async Task SavesAndReloadsARevision()
    {
        var staging = WorkbenchTestSupport.NewStagingProvider();
        var repository = new WorkbenchDraftRepository(WorkbenchTestSupport.Artificer(staging));
        var draft = Draft();
        await repository.CreateAsync(draft, OperationId.New());
        var revised = draft.Revise(new DraftContent("Choose PostgreSQL", "New context"), 1, Now.AddMinutes(1));

        var saved = await repository.SaveRevisionAsync(revised, 1, OperationId.New());
        var loaded = await new WorkbenchDraftRepository(WorkbenchTestSupport.Artificer(staging)).GetByAuthorAsync(Organization, Author, draft.Id);

        Assert.Equal(DraftWriteStatus.Saved, saved.Status);
        Assert.Equal(revised, loaded);
    }

    [Fact]
    public async Task ReplacementTargetSurvivesCreationRevisionAndRecomposition()
    {
        var staging = WorkbenchTestSupport.NewStagingProvider();
        var repository = new WorkbenchDraftRepository(WorkbenchTestSupport.Artificer(staging));
        var firstTarget = AdrId.New();
        var secondTarget = AdrId.New();
        var draft = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replace database decision"), Now, firstTarget);
        await repository.CreateAsync(draft, OperationId.New());
        var revised = draft.Revise(draft.Content, draft.Version, Now.AddMinutes(1), secondTarget);
        await repository.SaveRevisionAsync(revised, draft.Version, OperationId.New());

        var recomposed = new WorkbenchDraftRepository(WorkbenchTestSupport.Artificer(staging));
        var loaded = await recomposed.GetByAuthorAsync(Organization, Author, draft.Id);
        var summary = Assert.Single(await recomposed.ListByAuthorAsync(Organization, Author));

        Assert.Equal(secondTarget, loaded!.IntendedSupersessionTargetId);
        Assert.Equal(secondTarget, summary.IntendedSupersessionTargetId);
    }

    [Fact]
    public async Task RemoveAsyncDeletesTheDraftOnlyWhenVersionMatches()
    {
        var staging = WorkbenchTestSupport.NewStagingProvider();
        var repository = new WorkbenchDraftRepository(WorkbenchTestSupport.Artificer(staging));
        var draft = Draft();
        await repository.CreateAsync(draft, OperationId.New());

        var staleRemoval = await repository.RemoveAsync(Organization, Author, draft.Id, draft.Version + 1);
        var removal = await repository.RemoveAsync(Organization, Author, draft.Id, draft.Version);

        Assert.False(staleRemoval);
        Assert.True(removal);
        Assert.Null(await repository.GetByAuthorAsync(Organization, Author, draft.Id));
    }

    private static AdrDraft Draft() => AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Choose a database"), Now);
}
