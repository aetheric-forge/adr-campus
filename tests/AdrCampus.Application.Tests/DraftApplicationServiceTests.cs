using AdrCampus.Application.Drafts;
using AdrCampus.Application.Identity;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Discovery;
using AdrCampus.Providers.Drafts.InMemory;

namespace AdrCampus.Application.Tests;

public sealed class DraftApplicationServiceTests
{
    private static readonly OrganizationId Organization = new("aetheric-forge");
    private static readonly MemberId Author = new("author-1");
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActiveMemberCreatesAndReadsTheirDraft()
    {
        var service = CreateService(isMember: true);
        var command = Command("  Choose PostgreSQL  ");

        var created = await service.CreateAsync(command);
        var listed = await service.ListMineAsync(Organization, Author);
        var loaded = await service.GetMineAsync(Organization, Author, command.DraftId);

        Assert.Equal(CreateDraftStatus.Created, created.Status);
        Assert.Equal("Choose PostgreSQL", created.Draft!.Content.Title.Value);
        Assert.Single(listed.Drafts);
        Assert.Equal(GetDraftStatus.Success, loaded.Status);
    }

    [Fact]
    public async Task InvalidTitleReturnsCorrectableFeedbackWithoutCreatingADraft()
    {
        var service = CreateService(isMember: true);
        var result = await service.CreateAsync(Command("bad"));
        var listed = await service.ListMineAsync(Organization, Author);

        Assert.Equal(CreateDraftStatus.Invalid, result.Status);
        Assert.Equal(DraftValidationCode.TitleTooShort, result.ValidationCode);
        Assert.Empty(listed.Drafts);
    }

    [Fact]
    public async Task NonMemberCannotCreateListOrReadDrafts()
    {
        var service = CreateService(isMember: false);
        var command = Command("Choose PostgreSQL");

        Assert.Equal(CreateDraftStatus.Unauthorized, (await service.CreateAsync(command)).Status);
        Assert.False((await service.ListMineAsync(Organization, Author)).IsAuthorized);
        Assert.Equal(GetDraftStatus.Unauthorized, (await service.GetMineAsync(Organization, Author, command.DraftId)).Status);
    }

    [Fact]
    public async Task RetryingTheSameBrowserCommandIsIdempotent()
    {
        var service = CreateService(isMember: true);
        var command = Command("Choose PostgreSQL");
        await service.CreateAsync(command);

        var replay = await service.CreateAsync(command);

        Assert.Equal(CreateDraftStatus.AlreadyApplied, replay.Status);
    }

    [Fact]
    public async Task ActiveAuthorRevisesTheirDraft()
    {
        var service = CreateService(isMember: true);
        var command = Command("Choose a database");
        var created = await service.CreateAsync(command);

        var revised = await service.ReviseAsync(new ReviseDraftCommand(command.DraftId, OperationId.New(), Organization, Author, created.Draft!.Version, "Choose PostgreSQL", "Updated", null, null));

        Assert.Equal(ReviseDraftStatus.Saved, revised.Status);
        Assert.Equal(2, revised.Draft!.Version);
        Assert.Equal("Choose PostgreSQL", revised.Draft.Content.Title.Value);
    }

    [Fact]
    public async Task ActiveMemberCreatesReplacementForAcceptedDecision()
    {
        var accepted = AcceptedProposal(Organization);
        var service = CreateService(true, accepted);
        var command = Command("Replace the database decision") with { IntendedSupersessionTargetId = accepted.Id };

        var created = await service.CreateAsync(command);

        Assert.Equal(CreateDraftStatus.Created, created.Status);
        Assert.Equal(accepted.Id, created.Draft!.IntendedSupersessionTargetId);
        Assert.Equal(AdrLifecycleStatus.Accepted, accepted.Status);
    }

    [Fact]
    public async Task InvalidReplacementTargetDoesNotCreateDraft()
    {
        var proposed = AcceptedProposal(Organization) with { FinalDecision = null };
        var service = CreateService(true, proposed);
        var command = Command("Replace the database decision") with { IntendedSupersessionTargetId = proposed.Id };

        var result = await service.CreateAsync(command);

        Assert.Equal(CreateDraftStatus.Invalid, result.Status);
        Assert.Equal(SupersessionTargetValidationCode.NotEligible, result.TargetValidationCode);
        Assert.Empty((await service.ListMineAsync(Organization, Author)).Drafts);
    }

    [Fact]
    public async Task AuthorCanChangeAndRemoveReplacementTarget()
    {
        var first = AcceptedProposal(Organization);
        var second = AcceptedProposal(Organization);
        var service = CreateService(true, first, second);
        var command = Command("Replace the database decision") with { IntendedSupersessionTargetId = first.Id };
        var created = (await service.CreateAsync(command)).Draft!;

        var changed = await service.ReviseAsync(new(created.Id, OperationId.New(), Organization, Author, created.Version, created.Content.Title.Value, created.Content.Context, created.Content.Decision, created.Content.Consequences, second.Id));
        var removed = await service.ReviseAsync(new(changed.Draft!.Id, OperationId.New(), Organization, Author, changed.Draft.Version, changed.Draft.Content.Title.Value, changed.Draft.Content.Context, changed.Draft.Content.Decision, changed.Draft.Content.Consequences));

        Assert.Equal(second.Id, changed.Draft.IntendedSupersessionTargetId);
        Assert.Null(removed.Draft!.IntendedSupersessionTargetId);
    }

    [Fact]
    public async Task EligibleTargetsContainOnlyAcceptedRecordsInTheOrganization()
    {
        var accepted = AcceptedProposal(Organization);
        var proposed = AcceptedProposal(Organization) with { FinalDecision = null };
        var other = AcceptedProposal(new OrganizationId("other"));
        var service = CreateService(true, accepted, proposed, other);

        var result = await service.ListEligibleSupersessionTargetsAsync(Organization, Author);

        Assert.True(result.IsAuthorized);
        Assert.Equal(accepted.Id, Assert.Single(result.Targets).Id);
    }

    private static DraftApplicationService CreateService(bool isMember, params AdrProposal[] records) => new(
        new InMemoryDraftRepository(),
        new StubSharedRecordRepository(records),
        new StubMemberAuthority(isMember),
        new FixedTimeProvider(Now));

    private static CreateDraftCommand Command(string title) => new(
        AdrId.New(), OperationId.New(), Organization, Author, title, "Context", "Decision", "Consequences");

    private sealed class StubMemberAuthority(bool isMember) : IMemberAuthority
    {
        public Task<bool> IsActiveMemberAsync(OrganizationId organizationId, MemberId memberId, CancellationToken cancellationToken = default) => Task.FromResult(isMember);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AdrProposal AcceptedProposal(OrganizationId organizationId)
    {
        var proposal = new AdrProposal(AdrId.New(), organizationId, Author, Author, new ProposalContent(new DraftTitle("Existing accepted decision"), "Context", "Decision", "Consequences"), Now.AddDays(-2), Now.AddDays(-1), 1);
        return proposal.Decide(DecisionOutcome.Accepted, new MemberId("maintainer"), "", Now);
    }

    private sealed class StubSharedRecordRepository(IReadOnlyList<AdrProposal> records) : ISharedRecordRepository
    {
        public Task<AdrProposal?> GetSharedAsync(OrganizationId organizationId, AdrId id, CancellationToken cancellationToken = default) => Task.FromResult(records.FirstOrDefault(record => record.OrganizationId == organizationId && record.Id == id));
        public Task<IReadOnlyList<AdrProposal>> ListSharedAsync(OrganizationId organizationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdrProposal>>(records.Where(record => record.OrganizationId == organizationId).ToArray());
    }
}
