using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Providers.Drafts.Workbench;
using AethericForge.Runtime.Providers.Staging.InMemory;

namespace AdrCampus.Providers.Drafts.Workbench.Tests;

public sealed class WorkbenchDraftRepositoryTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Author = new("author-1");
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DraftsSurviveRepositoryRecomposition()
    {
        var staging = new InMemoryStagingProvider("workbench");
        var first = new WorkbenchDraftRepository(staging);
        var draft = Draft();
        await first.CreateAsync(draft, OperationId.New());

        var recomposed = new WorkbenchDraftRepository(staging);
        var loaded = await recomposed.GetByAuthorAsync(Organization, Author, draft.Id);

        Assert.Equal(draft, loaded);
        Assert.Single(await recomposed.ListByAuthorAsync(Organization, Author));
    }

    [Fact]
    public async Task OperationHistorySurvivesRepositoryRecomposition()
    {
        var staging = new InMemoryStagingProvider("workbench");
        var draft = Draft();
        var operationId = OperationId.New();
        await new WorkbenchDraftRepository(staging).CreateAsync(draft, operationId);

        var replay = await new WorkbenchDraftRepository(staging).CreateAsync(draft, operationId);

        Assert.Equal(DraftWriteStatus.AlreadyApplied, replay.Status);
    }

    [Fact]
    public async Task SavesAndReloadsARevision()
    {
        var staging = new InMemoryStagingProvider("workbench");
        var repository = new WorkbenchDraftRepository(staging);
        var draft = Draft();
        await repository.CreateAsync(draft, OperationId.New());
        var revised = draft.Revise(new DraftContent("Choose PostgreSQL", "New context"), 1, Now.AddMinutes(1));

        var saved = await repository.SaveRevisionAsync(revised, 1, OperationId.New());
        var loaded = await new WorkbenchDraftRepository(staging).GetByAuthorAsync(Organization, Author, draft.Id);

        Assert.Equal(DraftWriteStatus.Saved, saved.Status);
        Assert.Equal(revised, loaded);
    }

    private static AdrDraft Draft() => AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Choose a database"), Now);
}
