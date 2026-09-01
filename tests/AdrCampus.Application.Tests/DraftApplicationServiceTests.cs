using AdrCampus.Application.Drafts;
using AdrCampus.Application.Identity;
using AdrCampus.Core.Domain;
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

    private static DraftApplicationService CreateService(bool isMember) => new(
        new InMemoryDraftRepository(),
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
}
