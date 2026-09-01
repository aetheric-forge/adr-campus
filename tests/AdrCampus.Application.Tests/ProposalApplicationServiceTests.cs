using AdrCampus.Application.Identity;
using AdrCampus.Application.Proposals;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Proposals;
using AdrCampus.Providers.Drafts.Workbench;
using AethericForge.Runtime.Providers.Staging.InMemory;
namespace AdrCampus.Application.Tests;
public sealed class ProposalApplicationServiceTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge"); private static readonly MemberId Author = new("author-1"); private static readonly DateTimeOffset Now = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);
    [Fact] public async Task ActiveAuthorPreparesAndProposesExactDraftVersion() { var (service, repository, draft) = await Setup(true); var prepared = await service.PrepareAsync(Organization, Author, draft.Id); var result = await service.ProposeAsync(new(Organization, Author, draft.Id, prepared.Draft!.Version, OperationId.New())); Assert.True(prepared.IsReady); Assert.True(result.IsSuccess); Assert.Null(await repository.GetByAuthorAsync(Organization, Author, draft.Id)); }
    [Fact] public async Task NonMemberCannotPrepareProposeOrRead() { var (service, _, draft) = await Setup(false); Assert.False((await service.PrepareAsync(Organization, Author, draft.Id)).IsAuthorized); Assert.Equal(ProposalWriteStatus.UnauthorizedOrNotFound, (await service.ProposeAsync(new(Organization, Author, draft.Id, 1, OperationId.New()))).Status); Assert.False((await service.GetAsync(Organization, Author, draft.Id)).IsAuthorized); }
    private static async Task<(ProposalApplicationService Service, WorkbenchDraftRepository Repository, AdrDraft Draft)> Setup(bool member) { var repository = new WorkbenchDraftRepository(new InMemoryStagingProvider("workbench")); var draft = AdrDraft.Create(AdrId.New(), Organization, Author, new DraftContent("Choose PostgreSQL", "Context", "Decision", "Consequences"), Now); await repository.CreateAsync(draft, OperationId.New()); return (new ProposalApplicationService(repository, repository, new Authority(member), new Clock()), repository, draft); }
    private sealed class Authority(bool member) : IMemberAuthority { public Task<bool> IsActiveMemberAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(member); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now.AddMinutes(1); }
}
