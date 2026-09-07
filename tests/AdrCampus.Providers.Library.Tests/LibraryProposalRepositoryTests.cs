using AdrCampus.Core.Discovery;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Proposals;
using AdrCampus.Providers.Drafts.InMemory;

namespace AdrCampus.Providers.Library.Tests;

public sealed class LibraryProposalRepositoryTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Author = new("author-1");
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);

    private static (LibraryProposalRepository Repository, InMemoryDraftRepository Drafts) NewRepository()
    {
        var drafts = new InMemoryDraftRepository();
        var repository = new LibraryProposalRepository(LibraryTestSupport.NewLibrary(), drafts);
        return (repository, drafts);
    }

    private static AdrDraft Draft() => AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Choose a database"), Now);
    private static AdrDraft CompleteDraft() => AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Choose a database", "Context", "Decision", "Consequences"), Now);

    private static async Task<AdrProposal> Proposed((LibraryProposalRepository Repository, InMemoryDraftRepository Drafts) fixture)
    {
        var draft = CompleteDraft();
        await fixture.Drafts.CreateAsync(draft, OperationId.New());
        return (await fixture.Repository.ProposeAsync(Organization, Author, draft.Id, draft.Version, OperationId.New(), Now.AddMinutes(1))).Proposal!;
    }

    private static async Task<AdrProposal> Accepted((LibraryProposalRepository Repository, InMemoryDraftRepository Drafts) fixture)
    {
        var proposal = await Proposed(fixture);
        return (await fixture.Repository.DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Accepted, "", OperationId.New(), Now.AddMinutes(2))).Record!;
    }

    private static async Task<AdrProposal> ProposedReplacement((LibraryProposalRepository Repository, InMemoryDraftRepository Drafts) fixture, AdrProposal target, int minute)
    {
        var draft = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent($"Replace {target.Content.Title.Value}", "Context", "Decision", "Consequences"), Now.AddMinutes(minute), target.Id);
        await fixture.Drafts.CreateAsync(draft, OperationId.New());
        return (await fixture.Repository.ProposeAsync(Organization, Author, draft.Id, 1, OperationId.New(), Now.AddMinutes(minute + 1))).Proposal!;
    }

    [Fact]
    public async Task ProposalPublishesToLibraryAndConsumesTheWorkbenchDraft()
    {
        var fixture = NewRepository();
        var draft = CompleteDraft();
        await fixture.Drafts.CreateAsync(draft, OperationId.New());

        var result = await fixture.Repository.ProposeAsync(Organization, Author, draft.Id, 1, OperationId.New(), Now.AddMinutes(1));

        Assert.Equal(ProposalWriteStatus.Proposed, result.Status);
        Assert.Null(await fixture.Drafts.GetByAuthorAsync(Organization, Author, draft.Id));
        Assert.Equal(result.Proposal, await fixture.Repository.GetAsync(Organization, draft.Id));
    }

    [Fact]
    public async Task InvalidProposalLeavesPrivateDraftUnchanged()
    {
        var fixture = NewRepository();
        var draft = Draft();
        await fixture.Drafts.CreateAsync(draft, OperationId.New());

        var result = await fixture.Repository.ProposeAsync(Organization, Author, draft.Id, 1, OperationId.New(), Now.AddMinutes(1));

        Assert.Equal(ProposalWriteStatus.Invalid, result.Status);
        Assert.NotNull(await fixture.Drafts.GetByAuthorAsync(Organization, Author, draft.Id));
        Assert.Null(await fixture.Repository.GetAsync(Organization, draft.Id));
    }

    [Fact]
    public async Task ProposalRejectsStalePreviewAndReplaysRetry()
    {
        var fixture = NewRepository();
        var draft = CompleteDraft();
        await fixture.Drafts.CreateAsync(draft, OperationId.New());

        Assert.Equal(ProposalWriteStatus.Conflict, (await fixture.Repository.ProposeAsync(Organization, Author, draft.Id, 0, OperationId.New(), Now)).Status);
        var operation = OperationId.New();
        await fixture.Repository.ProposeAsync(Organization, Author, draft.Id, 1, operation, Now);
        Assert.Equal(ProposalWriteStatus.AlreadyApplied, (await fixture.Repository.ProposeAsync(Organization, Author, draft.Id, 1, operation, Now.AddHours(1))).Status);
    }

    [Fact]
    public async Task ProposalFreezesReplacementTargetAndSurvivesReads()
    {
        var fixture = NewRepository();
        var target = await Accepted(fixture);
        var replacement = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replace database decision", "Context", "Decision", "Consequences"), Now.AddMinutes(3), target.Id);
        await fixture.Drafts.CreateAsync(replacement, OperationId.New());

        var result = await fixture.Repository.ProposeAsync(Organization, Author, replacement.Id, replacement.Version, OperationId.New(), Now.AddMinutes(4));
        var loaded = await fixture.Repository.GetAsync(Organization, replacement.Id);

        Assert.Equal(ProposalWriteStatus.Proposed, result.Status);
        Assert.Equal(target.Id, result.Proposal!.IntendedSupersessionTargetId);
        Assert.Equal(target.Id, loaded!.IntendedSupersessionTargetId);
        Assert.Equal(AdrLifecycleStatus.Accepted, (await fixture.Repository.GetAsync(Organization, target.Id))!.Status);
    }

    [Fact]
    public async Task ProposalAgainstNonAcceptedTargetLeavesReplacementPrivate()
    {
        var fixture = NewRepository();
        var target = await Proposed(fixture);
        var replacement = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replace database decision", "Context", "Decision", "Consequences"), Now.AddMinutes(2), target.Id);
        await fixture.Drafts.CreateAsync(replacement, OperationId.New());

        var result = await fixture.Repository.ProposeAsync(Organization, Author, replacement.Id, replacement.Version, OperationId.New(), Now.AddMinutes(3));

        Assert.Equal(ProposalWriteStatus.TargetNotEligible, result.Status);
        Assert.NotNull(await fixture.Drafts.GetByAuthorAsync(Organization, Author, replacement.Id));
        Assert.Null(await fixture.Repository.GetAsync(Organization, replacement.Id));
    }

    [Fact]
    public async Task AcceptingReplacementAtomicallyCompletesReciprocalSupersession()
    {
        var fixture = NewRepository();
        var target = await Accepted(fixture);
        var replacement = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replace database decision", "Context", "Decision", "Consequences"), Now.AddMinutes(3), target.Id);
        await fixture.Drafts.CreateAsync(replacement, OperationId.New());
        var proposed = (await fixture.Repository.ProposeAsync(Organization, Author, replacement.Id, 1, OperationId.New(), Now.AddMinutes(4))).Proposal!;

        var result = await fixture.Repository.DecideAsync(Organization, proposed.Id, proposed.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Accepted, "", OperationId.New(), Now.AddMinutes(5));

        var acceptedReplacement = await fixture.Repository.GetAsync(Organization, proposed.Id);
        var supersededTarget = await fixture.Repository.GetAsync(Organization, target.Id);
        Assert.Equal(DecisionWriteStatus.Decided, result.Status);
        Assert.Equal(AdrLifecycleStatus.Accepted, acceptedReplacement!.Status);
        Assert.Equal(target.Id, acceptedReplacement.Supersedes!.TargetId);
        Assert.Equal(AdrLifecycleStatus.Superseded, supersededTarget!.Status);
        Assert.Equal(acceptedReplacement.Id, supersededTarget.SupersededBy!.ReplacementId);
    }

    [Fact]
    public async Task SupersessionRetryReturnsOriginalOutcomeWithoutDuplicateState()
    {
        var fixture = NewRepository();
        var target = await Accepted(fixture);
        var proposed = await ProposedReplacement(fixture, target, 3);
        var operation = OperationId.New();

        var first = await fixture.Repository.DecideAsync(Organization, proposed.Id, proposed.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Accepted, "", operation, Now.AddMinutes(5));
        var retry = await fixture.Repository.DecideAsync(Organization, proposed.Id, proposed.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Accepted, "", operation, Now.AddHours(1));

        Assert.Equal(DecisionWriteStatus.AlreadyApplied, retry.Status);
        Assert.Equal(first.Record, retry.Record);
    }

    [Fact]
    public async Task FirstConcurrentReplacementWinsAndOtherRemainsProposed()
    {
        var fixture = NewRepository();
        var target = await Accepted(fixture);
        var first = await ProposedReplacement(fixture, target, 3);
        var second = await ProposedReplacement(fixture, target, 5);

        var attempts = await Task.WhenAll(
            fixture.Repository.DecideAsync(Organization, first.Id, first.ProposedAtUtc, new MemberId("maintainer-1"), DecisionOutcome.Accepted, "", OperationId.New(), Now.AddMinutes(7)),
            fixture.Repository.DecideAsync(Organization, second.Id, second.ProposedAtUtc, new MemberId("maintainer-2"), DecisionOutcome.Accepted, "", OperationId.New(), Now.AddMinutes(8)));

        Assert.Single(attempts, result => result.Status == DecisionWriteStatus.Decided);
        Assert.Single(attempts, result => result.Status == DecisionWriteStatus.TargetNotAccepted);
        Assert.Single((await fixture.Repository.ListAsync(Organization)).Where(proposal => proposal.Id == first.Id || proposal.Id == second.Id));
    }

    [Fact]
    public async Task InvalidDecisionPreservesProposal()
    {
        var fixture = NewRepository();
        var proposal = await Proposed(fixture);

        var result = await fixture.Repository.DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Rejected, " ", OperationId.New(), Now.AddMinutes(2));

        Assert.Equal(DecisionWriteStatus.Invalid, result.Status);
        Assert.Null((await fixture.Repository.GetAsync(Organization, proposal.Id))!.FinalDecision);
        Assert.Single(await fixture.Repository.ListAsync(Organization));
    }

    [Fact]
    public async Task FirstDecisionWinsAndOpposingDecisionConflicts()
    {
        var fixture = NewRepository();
        var proposal = await Proposed(fixture);

        var accepted = await fixture.Repository.DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer-1"), DecisionOutcome.Accepted, "", OperationId.New(), Now.AddMinutes(2));
        var rejected = await fixture.Repository.DecideAsync(Organization, proposal.Id, proposal.ProposedAtUtc, new MemberId("maintainer-2"), DecisionOutcome.Rejected, "Too late", OperationId.New(), Now.AddMinutes(3));

        Assert.Equal(DecisionWriteStatus.Decided, accepted.Status);
        Assert.Equal(DecisionWriteStatus.Conflict, rejected.Status);
        Assert.Equal(DecisionOutcome.Accepted, (await fixture.Repository.GetAsync(Organization, proposal.Id))!.FinalDecision!.Outcome);
        Assert.Empty(await fixture.Repository.ListAsync(Organization));
        Assert.Single(await fixture.Repository.ListDecidedAsync(Organization, DecisionOutcome.Accepted));
    }

    [Fact]
    public async Task SharedDiscoveryIsOrganizationScoped()
    {
        var fixture = NewRepository();
        var shared = await Proposed(fixture);
        var otherOrganization = new OrganizationId("other");
        var otherDraft = AdrDraft.Create(AdrId.New(), otherOrganization, Author, new DraftContent("Other decision", "Context", "Decision", "Consequences"), Now);
        await fixture.Drafts.CreateAsync(otherDraft, OperationId.New());
        await fixture.Repository.ProposeAsync(otherOrganization, Author, otherDraft.Id, otherDraft.Version, OperationId.New(), Now.AddMinutes(1));

        var results = await fixture.Repository.ListSharedAsync(Organization);

        Assert.Single(results);
        Assert.Equal(shared.Id, results[0].Id);
        Assert.DoesNotContain(results, record => record.OrganizationId == otherOrganization);
    }
}
