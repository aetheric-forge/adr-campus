using AdrCampus.Core.Domain;
using AdrCampus.Core.Proposals;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public sealed class ProposalReviewTests
{
    [Fact]
    public async Task MountedRecorderPreparesAndCommitsUsingExistingApplicationRules()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var recorder = Assert.IsAssignableFrom<IAdrCampusRecorder>(host.Office.Recorder);
        var review = recorder.OpenReview(scope.ServiceProvider.GetRequiredService<IProposalReviewCaller>(),
            host.Membership, host.Services.GetRequiredService<TimeProvider>());

        var prepared = await review.PrepareAsync(proposal.Id, DecisionOutcome.Accepted, "");
        var result = await review.DecideAsync(Command(proposal));

        Assert.True(prepared.IsReady);
        Assert.Equal(proposal.Content, prepared.Proposal!.Content);
        Assert.Equal(DecisionWriteStatus.Decided, result.Status);
        Assert.Equal(ReviewHost.Maintainer, result.Record!.FinalDecision!.DeciderId);
        Assert.Equal(ReviewHost.Organization, result.Record.OrganizationId);
        Assert.Equal(proposal.Content, result.Record.Content);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ordinary-member")]
    public async Task UnauthenticatedOrNonMaintainerCannotPrepareOrCommit(string? member)
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ReviewHost.Caller>().Member = member is null ? null : new MemberId(member);
        var review = scope.ServiceProvider.GetRequiredService<IProposalReview>();

        Assert.False((await review.PrepareAsync(proposal.Id, DecisionOutcome.Accepted, "")).IsAuthorized);
        Assert.Equal(DecisionWriteStatus.UnauthorizedOrNotFound, (await review.DecideAsync(Command(proposal))).Status);
        Assert.Null((await host.Services.GetRequiredService<IProposalRepository>()
            .GetAsync(ReviewHost.Organization, proposal.Id))!.FinalDecision);
    }

    [Fact]
    public async Task MembershipRevokedAfterPreparationIsRecheckedAtCommit()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var review = scope.ServiceProvider.GetRequiredService<IProposalReview>();
        Assert.True((await review.PrepareAsync(proposal.Id, DecisionOutcome.Accepted, "")).IsReady);
        host.Membership.Enabled = false;
        Assert.Equal(DecisionWriteStatus.UnauthorizedOrNotFound, (await review.DecideAsync(Command(proposal))).Status);
    }

    [Fact]
    public async Task SessionsDoNotCaptureAnotherRequestsCallerAndRecheckSignOut()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var first = host.Services.CreateScope();
        using var second = host.Services.CreateScope();
        var authorized = first.ServiceProvider.GetRequiredService<IProposalReview>();
        second.ServiceProvider.GetRequiredService<ReviewHost.Caller>().Member = new MemberId("ordinary-member");
        var denied = second.ServiceProvider.GetRequiredService<IProposalReview>();
        Assert.True((await authorized.PrepareAsync(proposal.Id, DecisionOutcome.Accepted, "")).IsReady);
        Assert.False((await denied.PrepareAsync(proposal.Id, DecisionOutcome.Accepted, "")).IsAuthorized);
        first.ServiceProvider.GetRequiredService<ReviewHost.Caller>().Member = null;
        Assert.Equal(DecisionWriteStatus.UnauthorizedOrNotFound, (await authorized.DecideAsync(Command(proposal))).Status);
    }

    [Fact]
    public async Task OfficeBindingPreventsReviewingAnotherOrganizationsProposal()
    {
        using var host = new ReviewHost();
        var foreign = await host.SeedAsync(new OrganizationId("another-office"));
        using var scope = host.Services.CreateScope();
        var review = scope.ServiceProvider.GetRequiredService<IProposalReview>();
        Assert.False((await review.PrepareAsync(foreign.Id, DecisionOutcome.Accepted, "")).IsFound);
        Assert.Equal(DecisionWriteStatus.UnauthorizedOrNotFound, (await review.DecideAsync(Command(foreign))).Status);
    }

    [Fact]
    public async Task RejectionRequiresReasonEvenWithoutPreparation()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var review = scope.ServiceProvider.GetRequiredService<IProposalReview>();
        Assert.False((await review.PrepareAsync(proposal.Id, DecisionOutcome.Rejected, "")).IsReady);
        var command = Command(proposal) with { Outcome = DecisionOutcome.Rejected };
        Assert.Equal(DecisionWriteStatus.Invalid, (await review.DecideAsync(command)).Status);
        var rejected = await review.DecideAsync(command with { Note = "Needs evidence" });
        Assert.Equal(DecisionWriteStatus.Decided, rejected.Status);
        Assert.Equal(DecisionOutcome.Rejected, rejected.Record!.FinalDecision!.Outcome);
    }

    [Fact]
    public async Task RetryPreservesOperationIdentityAndRejectsChangedPayload()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var review = scope.ServiceProvider.GetRequiredService<IProposalReview>();
        var command = Command(proposal);
        var first = await review.DecideAsync(command);
        var retry = await review.DecideAsync(command);
        Assert.Equal(DecisionWriteStatus.Decided, first.Status);
        Assert.Equal(DecisionWriteStatus.AlreadyApplied, retry.Status);
        Assert.Equal(first.Record, retry.Record);
        Assert.Equal(DecisionWriteStatus.OperationMismatch,
            (await review.DecideAsync(command with { Note = "Changed" })).Status);
    }

    [Fact]
    public async Task StaleAndCompetingDecisionsPreserveSingleFinalOutcome()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var review = scope.ServiceProvider.GetRequiredService<IProposalReview>();
        Assert.Equal(DecisionWriteStatus.Conflict, (await review.DecideAsync(Command(proposal) with
            { ExpectedProposedAtUtc = proposal.ProposedAtUtc.AddSeconds(-1) })).Status);
        var results = await Task.WhenAll(review.DecideAsync(Command(proposal)),
            review.DecideAsync(Command(proposal) with { Outcome = DecisionOutcome.Rejected, Note = "Declined" }));
        Assert.Single(results, result => result.Status == DecisionWriteStatus.Decided);
        Assert.Single(results, result => result.Status == DecisionWriteStatus.Conflict);
    }

    [Fact]
    public async Task CancellationIsPassedToCallerResolution()
    {
        using var host = new ReviewHost();
        using var scope = host.Services.CreateScope();
        var review = scope.ServiceProvider.GetRequiredService<IProposalReview>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => review.PrepareAsync(
            AdrId.New(), DecisionOutcome.Accepted, "", cancellation.Token));
    }

    private static ReviewDecision Command(AdrProposal proposal) => new(proposal.Id,
        proposal.ProposedAtUtc, DecisionOutcome.Accepted, "", OperationId.New());
}
