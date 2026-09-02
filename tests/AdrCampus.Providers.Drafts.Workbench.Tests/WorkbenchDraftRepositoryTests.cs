using AdrCampus.Core.Domain;
using AdrCampus.Core.Discovery;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Proposals;
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

    [Fact]
    public async Task ReplacementTargetSurvivesCreationRevisionAndRecomposition()
    {
        var staging = new InMemoryStagingProvider("workbench");
        var repository = new WorkbenchDraftRepository(staging);
        var firstTarget = AdrId.New();
        var secondTarget = AdrId.New();
        var draft = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replace database decision"), Now, firstTarget);
        await repository.CreateAsync(draft, OperationId.New());
        var revised = draft.Revise(draft.Content, draft.Version, Now.AddMinutes(1), secondTarget);
        await repository.SaveRevisionAsync(revised, draft.Version, OperationId.New());

        var recomposed = new WorkbenchDraftRepository(staging);
        var loaded = await recomposed.GetByAuthorAsync(Organization, Author, draft.Id);
        var summary = Assert.Single(await recomposed.ListByAuthorAsync(Organization, Author));

        Assert.Equal(secondTarget, loaded!.IntendedSupersessionTargetId);
        Assert.Equal(secondTarget, summary.IntendedSupersessionTargetId);
    }

    [Fact]
    public async Task ProposalAtomicallyRemovesDraftAndCreatesSharedRecord()
    {
        var staging = new InMemoryStagingProvider("workbench"); var repository = new WorkbenchDraftRepository(staging); var draft = CompleteDraft(); await repository.CreateAsync(draft, OperationId.New());
        var result = await repository.ProposeAsync(Organization, Author, draft.Id, 1, OperationId.New(), Now.AddMinutes(1));
        Assert.Equal(ProposalWriteStatus.Proposed, result.Status); Assert.Null(await repository.GetByAuthorAsync(Organization, Author, draft.Id)); Assert.Equal(result.Proposal, await repository.GetAsync(Organization, draft.Id));
    }

    [Fact]
    public async Task InvalidProposalLeavesPrivateDraftUnchanged()
    {
        var staging = new InMemoryStagingProvider("workbench"); var repository = new WorkbenchDraftRepository(staging); var draft = Draft(); await repository.CreateAsync(draft, OperationId.New());
        var result = await repository.ProposeAsync(Organization, Author, draft.Id, 1, OperationId.New(), Now.AddMinutes(1));
        Assert.Equal(ProposalWriteStatus.Invalid, result.Status); Assert.NotNull(await repository.GetByAuthorAsync(Organization, Author, draft.Id)); Assert.Null(await repository.GetAsync(Organization, draft.Id));
    }

    [Fact]
    public async Task ProposalRejectsStalePreviewAndReplaysRetry()
    {
        var staging = new InMemoryStagingProvider("workbench"); var repository = new WorkbenchDraftRepository(staging); var draft = CompleteDraft(); await repository.CreateAsync(draft, OperationId.New());
        Assert.Equal(ProposalWriteStatus.Conflict, (await repository.ProposeAsync(Organization, Author, draft.Id, 0, OperationId.New(), Now)).Status);
        var operation = OperationId.New(); await repository.ProposeAsync(Organization, Author, draft.Id, 1, operation, Now); Assert.Equal(ProposalWriteStatus.AlreadyApplied, (await new WorkbenchDraftRepository(staging).ProposeAsync(Organization, Author, draft.Id, 1, operation, Now.AddHours(1))).Status);
    }

    [Fact]
    public async Task ProposalFreezesReplacementTargetAndSurvivesRecomposition()
    {
        var staging = new InMemoryStagingProvider("workbench");
        var repository = new WorkbenchDraftRepository(staging);
        var target = await Accepted(repository);
        var replacement = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replace database decision", "Context", "Decision", "Consequences"), Now.AddMinutes(3), target.Id);
        await repository.CreateAsync(replacement, OperationId.New());

        var result = await repository.ProposeAsync(Organization, Author, replacement.Id, replacement.Version, OperationId.New(), Now.AddMinutes(4));
        var loaded = await new WorkbenchDraftRepository(staging).GetAsync(Organization, replacement.Id);

        Assert.Equal(ProposalWriteStatus.Proposed, result.Status);
        Assert.Equal(target.Id, result.Proposal!.IntendedSupersessionTargetId);
        Assert.Equal(target.Id, loaded!.IntendedSupersessionTargetId);
        Assert.Equal(AdrLifecycleStatus.Accepted, (await repository.GetAsync(Organization, target.Id))!.Status);
    }

    [Fact]
    public async Task ProposalAgainstNonAcceptedTargetLeavesReplacementPrivate()
    {
        var repository = new WorkbenchDraftRepository(new InMemoryStagingProvider("workbench"));
        var target = await Proposed(repository);
        var replacement = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replace database decision", "Context", "Decision", "Consequences"), Now.AddMinutes(2), target.Id);
        await repository.CreateAsync(replacement, OperationId.New());

        var result = await repository.ProposeAsync(Organization, Author, replacement.Id, replacement.Version, OperationId.New(), Now.AddMinutes(3));

        Assert.Equal(ProposalWriteStatus.TargetNotEligible, result.Status);
        Assert.NotNull(await repository.GetByAuthorAsync(Organization, Author, replacement.Id));
        Assert.Null(await repository.GetAsync(Organization, replacement.Id));
    }

    [Fact]
    public async Task ReplacementCannotUseOrdinaryAcceptanceBeforeAtomicSupersessionIsAvailable()
    {
        var repository = new WorkbenchDraftRepository(new InMemoryStagingProvider("workbench"));
        var target = await Accepted(repository);
        var replacement = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replace database decision", "Context", "Decision", "Consequences"), Now.AddMinutes(3), target.Id);
        await repository.CreateAsync(replacement, OperationId.New());
        var proposed = (await repository.ProposeAsync(Organization, Author, replacement.Id, 1, OperationId.New(), Now.AddMinutes(4))).Proposal!;

        var result = await repository.DecideAsync(Organization, proposed.Id, proposed.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Accepted, "", OperationId.New(), Now.AddMinutes(5));

        Assert.Equal(DecisionWriteStatus.SupersessionPending, result.Status);
        Assert.Equal(AdrLifecycleStatus.Proposed, (await repository.GetAsync(Organization, proposed.Id))!.Status);
        Assert.Equal(AdrLifecycleStatus.Accepted, (await repository.GetAsync(Organization, target.Id))!.Status);
    }

    [Fact]
    public async Task RejectingReplacementRetainsIntentAndLeavesTargetAccepted()
    {
        var repository = new WorkbenchDraftRepository(new InMemoryStagingProvider("workbench"));
        var target = await Accepted(repository);
        var replacement = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replace database decision", "Context", "Decision", "Consequences"), Now.AddMinutes(3), target.Id);
        await repository.CreateAsync(replacement, OperationId.New());
        var proposed = (await repository.ProposeAsync(Organization, Author, replacement.Id, 1, OperationId.New(), Now.AddMinutes(4))).Proposal!;

        var rejected = await repository.DecideAsync(Organization, proposed.Id, proposed.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Rejected, "Keep the existing decision", OperationId.New(), Now.AddMinutes(5));

        Assert.Equal(AdrLifecycleStatus.Rejected, rejected.Record!.Status);
        Assert.Equal(target.Id, rejected.Record.IntendedSupersessionTargetId);
        Assert.Equal(AdrLifecycleStatus.Accepted, (await repository.GetAsync(Organization, target.Id))!.Status);
    }

    [Fact]
    public async Task InvalidDecisionPreservesProposal()
    {
        var repository = new WorkbenchDraftRepository(new InMemoryStagingProvider("workbench")); var proposal = await Proposed(repository);
        var result = await repository.DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Rejected, " ", OperationId.New(), Now.AddMinutes(2));
        Assert.Equal(DecisionWriteStatus.Invalid, result.Status); Assert.Null((await repository.GetAsync(Organization, proposal.Id))!.FinalDecision); Assert.Single(await repository.ListAsync(Organization));
    }

    [Fact]
    public async Task FirstDecisionWinsAndOpposingDecisionConflicts()
    {
        var repository = new WorkbenchDraftRepository(new InMemoryStagingProvider("workbench")); var proposal = await Proposed(repository);
        var accepted = await repository.DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer-1"), DecisionOutcome.Accepted, "", OperationId.New(), Now.AddMinutes(2));
        var rejected = await repository.DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer-2"), DecisionOutcome.Rejected, "Too late", OperationId.New(), Now.AddMinutes(3));
        Assert.Equal(DecisionWriteStatus.Decided, accepted.Status); Assert.Equal(DecisionWriteStatus.Conflict, rejected.Status); Assert.Equal(DecisionOutcome.Accepted, (await repository.GetAsync(Organization, proposal.Id))!.FinalDecision!.Outcome); Assert.Empty(await repository.ListAsync(Organization)); Assert.Single(await repository.ListDecidedAsync(Organization, DecisionOutcome.Accepted));
    }

    [Fact]
    public async Task DecisionRetrySurvivesRepositoryRecomposition()
    {
        var staging = new InMemoryStagingProvider("workbench"); var repository = new WorkbenchDraftRepository(staging); var proposal = await Proposed(repository); var operation = OperationId.New();
        var first = await repository.DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Rejected, "  Missing evidence  ", operation, Now.AddMinutes(2));
        var retry = await new WorkbenchDraftRepository(staging).DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Rejected, "  Missing evidence  ", operation, Now.AddHours(1));
        Assert.Equal(DecisionWriteStatus.AlreadyApplied, retry.Status); Assert.Equal(first.Record, retry.Record); Assert.Equal("Missing evidence", retry.Record!.FinalDecision!.Note);
    }

    [Fact]
    public async Task SharedDiscoveryIsOrganizationScopedAndExcludesDrafts()
    {
        var repository = new WorkbenchDraftRepository(new InMemoryStagingProvider("workbench"));
        var privateDraft = CompleteDraft();
        await repository.CreateAsync(privateDraft, OperationId.New());
        var shared = await Proposed(repository);
        var otherOrganization = new OrganizationId("other");
        var otherDraft = AdrDraft.Create(AdrId.New(), otherOrganization, Author, new DraftContent("Other decision", "Context", "Decision", "Consequences"), Now);
        await repository.CreateAsync(otherDraft, OperationId.New());
        await repository.ProposeAsync(otherOrganization, Author, otherDraft.Id, otherDraft.Version, OperationId.New(), Now.AddMinutes(1));

        var results = await repository.ListSharedAsync(Organization);

        Assert.Single(results);
        Assert.Equal(shared.Id, results[0].Id);
        Assert.DoesNotContain(results, record => record.Id == privateDraft.Id || record.OrganizationId == otherOrganization);
    }

    private static AdrDraft Draft() => AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Choose a database"), Now);
    private static AdrDraft CompleteDraft() => AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Choose a database", "Context", "Decision", "Consequences"), Now);
    private static async Task<AdrProposal> Proposed(WorkbenchDraftRepository repository) { var draft = CompleteDraft(); await repository.CreateAsync(draft, OperationId.New()); return (await repository.ProposeAsync(Organization, Author, draft.Id, draft.Version, OperationId.New(), Now.AddMinutes(1))).Proposal!; }
    private static async Task<AdrProposal> Accepted(WorkbenchDraftRepository repository) { var proposal = await Proposed(repository); return (await repository.DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Accepted, "", OperationId.New(), Now.AddMinutes(2))).Record!; }
}
