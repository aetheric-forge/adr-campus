using AdrCampus.Application.Identity;
using AdrCampus.Application.Proposals;
using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Proposals;
using AdrCampus.Providers.Drafts.Workbench;
using AdrCampus.Providers.Library;
using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Library.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Workbench.Services;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Library;
using AethericForge.Runtime.Models.Authorities;
using AethericForge.Runtime.Providers.Knowledge.InMemory;
using AethericForge.Runtime.Providers.Staging.InMemory;
using AethericForge.Runtime.Services.Knowledge;
using AethericForge.Runtime.Services.Library;
using AethericForge.Runtime.Services.Staging;
using AethericForge.Runtime.Services.Workbench;
using Microsoft.Extensions.DependencyInjection;

namespace AdrCampus.Application.Tests;

public sealed class ProposalApplicationServiceTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge"); private static readonly MemberId Author = new("author-1"); private static readonly DateTimeOffset Now = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
    [Fact] public async Task ActiveAuthorPreparesAndProposesExactDraftVersion() { var (service, drafts, _, draft, _) = await Setup(true); var prepared = await service.PrepareAsync(Organization, Author, draft.Id); var result = await service.ProposeAsync(new(Organization, Author, draft.Id, prepared.Draft!.Version, OperationId.New())); Assert.True(prepared.IsReady); Assert.True(result.IsSuccess); Assert.Null(await drafts.GetByAuthorAsync(Organization, Author, draft.Id)); }
    [Fact] public async Task NonMemberCannotPrepareProposeOrRead() { var (service, _, _, draft, _) = await Setup(false); Assert.False((await service.PrepareAsync(Organization, Author, draft.Id)).IsAuthorized); Assert.Equal(ProposalWriteStatus.UnauthorizedOrNotFound, (await service.ProposeAsync(new(Organization, Author, draft.Id, 1, OperationId.New()))).Status); Assert.False((await service.GetAsync(Organization, Author, draft.Id)).IsAuthorized); }
    [Fact] public async Task MaintainerCanPrepareAndDecideProposal() { var (service, _, _, draft, authority) = await Setup(true, true); var proposed = await service.ProposeAsync(new(Organization, Author, draft.Id, 1, OperationId.New())); var prepared = await service.PrepareDecisionAsync(Organization, Author, draft.Id, DecisionOutcome.Accepted, ""); var decided = await service.DecideAsync(new(Organization, draft.Id, proposed.Proposal!.ProposedAtUtc, Author, DecisionOutcome.Accepted, prepared.Note!, OperationId.New())); Assert.True(prepared.IsReady); Assert.True(decided.IsSuccess); Assert.Equal(DecisionOutcome.Accepted, decided.Record!.FinalDecision!.Outcome); Assert.True(authority.Maintainer); }
    [Fact] public async Task AuthorityIsRecheckedAtDecisionCommit() { var (service, _, _, draft, authority) = await Setup(true, true); var proposed = await service.ProposeAsync(new(Organization, Author, draft.Id, 1, OperationId.New())); Assert.True((await service.PrepareDecisionAsync(Organization, Author, draft.Id, DecisionOutcome.Accepted, "")).IsReady); authority.Maintainer = false; var result = await service.DecideAsync(new(Organization, draft.Id, proposed.Proposal!.ProposedAtUtc, Author, DecisionOutcome.Accepted, "", OperationId.New())); Assert.Equal(DecisionWriteStatus.UnauthorizedOrNotFound, result.Status); }
    [Fact] public async Task OrdinaryMemberCannotPrepareOrCommitDecision() { var (service, _, _, draft, _) = await Setup(true, false); var proposed = await service.ProposeAsync(new(Organization, Author, draft.Id, 1, OperationId.New())); Assert.False((await service.PrepareDecisionAsync(Organization, Author, draft.Id, DecisionOutcome.Accepted, "")).IsAuthorized); Assert.Equal(DecisionWriteStatus.UnauthorizedOrNotFound, (await service.DecideAsync(new(Organization, draft.Id, proposed.Proposal!.ProposedAtUtc, Author, DecisionOutcome.Accepted, "", OperationId.New()))).Status); }

    [Fact]
    public async Task ReplacementTargetIsShownDuringProposalAndDecisionPreparation()
    {
        var (drafts, proposals) = NewFixture();
        var targetDraft = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Existing decision", "Context", "Decision", "Consequences"), Now.AddDays(-2));
        await drafts.CreateAsync(targetDraft, OperationId.New());
        var targetProposal = (await proposals.ProposeAsync(Organization, Author, targetDraft.Id, 1, OperationId.New(), Now.AddDays(-1))).Proposal!;
        await proposals.DecideAsync(Organization, targetProposal.Id, targetProposal.ProposedAtUtc, new MemberId("maintainer"), DecisionOutcome.Accepted, "", OperationId.New(), Now);
        var replacement = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Replacement decision", "Context", "Decision", "Consequences"), Now, targetDraft.Id);
        await drafts.CreateAsync(replacement, OperationId.New());
        var authority = new Authority(true, true);
        var service = new ProposalApplicationService(drafts, proposals, proposals, authority, new Clock());

        var prepared = await service.PrepareAsync(Organization, Author, replacement.Id);
        var proposed = await service.ProposeAsync(new(Organization, Author, replacement.Id, replacement.Version, OperationId.New()));
        var decision = await service.PrepareDecisionAsync(Organization, Author, replacement.Id, DecisionOutcome.Accepted, "");

        Assert.Equal("Existing decision", prepared.Target!.Title.Value);
        Assert.Equal(targetDraft.Id, proposed.Proposal!.IntendedSupersessionTargetId);
        Assert.Equal(targetDraft.Id, decision.Target!.Id);
    }

    [Fact]
    public async Task ExpiredDraftCannotBePrepared()
    {
        var (drafts, proposals) = NewFixture();
        var draft = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Choose PostgreSQL", "Context", "Decision", "Consequences"), Now.AddDays(-40));
        await drafts.CreateAsync(draft, OperationId.New());
        var evt = new AdministrationEvent(Guid.NewGuid(), Organization, AdministrationEventType.DraftRecoveryStarted, Now.AddDays(-31), "SSO observation", SubjectId: Author, DraftId: draft.Id);
        await drafts.StartRecoveryAsync(Organization, draft.Id, Author, 1, Now.AddDays(-31), evt);
        var service = new ProposalApplicationService(drafts, proposals, proposals, new Authority(true, false), new Clock());

        var prepared = await service.PrepareAsync(Organization, Author, draft.Id);

        Assert.False(prepared.IsFound);
    }

    private static async Task<(ProposalApplicationService Service, IDraftRepository Drafts, LibraryProposalRepository Proposals, AdrDraft Draft, Authority Authority)> Setup(bool member, bool maintainer = false)
    {
        var (drafts, proposals) = NewFixture();
        var draft = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Choose PostgreSQL", "Context", "Decision", "Consequences"), Now);
        await drafts.CreateAsync(draft, OperationId.New());
        var authority = new Authority(member, maintainer);
        return (new ProposalApplicationService(drafts, proposals, proposals, authority, new Clock()), drafts, proposals, draft, authority);
    }

    /// <summary>
    /// Builds the same two-institution fixture ForgeCampusExtensions composes in production: drafts live
    /// in Workbench, proposals/decisions live in Library.
    /// </summary>
    private static (WorkbenchDraftRepository Drafts, LibraryProposalRepository Proposals) NewFixture()
    {
        var staging = new InMemoryStagingProvider("adr-campus-workbench");
        var artificer = new Artificer(new StagingService([staging]), new Team<IWorkbenchWorker>(Array.Empty<IWorkbenchWorker>()));
        var drafts = new WorkbenchDraftRepository(artificer);

        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeProvider>(new InMemoryKnowledgeProvider("adr-campus"));
        services.AddSingleton<ITeam<ICuratorClerk>>(new Team<ICuratorClerk>(Array.Empty<ICuratorClerk>()));
        services.AddSingleton<IKnowledgeService, KnowledgeService>();
        services.AddSingleton<ITeam<ILibraryClerk>>(new Team<ILibraryClerk>(Array.Empty<ILibraryClerk>()));
        services.AddSingleton<ILibrarian, Librarian>();
        services.AddSingleton<ICurator, Curator>();
        var provider = services.BuildServiceProvider();
        var template = InstitutionTemplateBuilder.Create().WithDescriptor("Library", new Version(1, 0, 0), "test library").Build();
        var context = new LibraryContext(template, provider);
        var library = ActivatorUtilities.CreateInstance<AethericForge.Runtime.Institutions.Library.Library>(provider, context);

        return (drafts, new LibraryProposalRepository(library, drafts));
    }

    private sealed class Authority(bool member, bool maintainer) : IMemberAuthority { public bool Maintainer { get; set; } = maintainer; public Task<bool> IsActiveMemberAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(member); public Task<bool> IsActiveMaintainerAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(member && Maintainer); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now.AddMinutes(1); }
}
