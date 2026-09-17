using AethericContracts.Interactions;
using AdrCampus.Application.Proposals;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Proposals;
using AdrCampus.Core.Drafts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public sealed class ProposalReviewInteractionTests
{
    [Fact]
    public async Task AvailableActionsAreOwnedByPackageAndRequireMaintainerAuthority()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IInteractionProvider>();
        var actions = await provider.GetActionsAsync(proposal.Id.ToString());
        Assert.Equal(new[] { "reject", "accept" }, actions.Select(action => action.Id));
        Assert.All(actions, action => Assert.True(action.IsEnabled));
        host.Membership.Enabled = false;
        Assert.Empty(await provider.GetActionsAsync(proposal.Id.ToString()));
    }

    [Fact]
    public async Task SupersededTargetDisablesAcceptanceButStillOffersRejection()
    {
        using var host = new ReviewHost();
        var target = await host.SeedAsync();
        var repository = host.Services.GetRequiredService<IProposalRepository>();
        await repository.DecideAsync(ReviewHost.Organization, target.Id, target.ProposedAtUtc,
            ReviewHost.Maintainer, DecisionOutcome.Accepted, "", OperationId.New(), ReviewHost.Now.AddMinutes(1));
        async Task<AdrProposal> Replacement(string title)
        {
            var draft = AdrDraft.Create(AdrId.New(), ReviewHost.Organization, ReviewHost.Maintainer,
                new DraftContent(title, "Context", "Decision", "Consequences"), ReviewHost.Now, target.Id);
            await host.Services.GetRequiredService<IDraftRepository>().CreateAsync(draft, OperationId.New());
            return (await repository.ProposeAsync(ReviewHost.Organization, ReviewHost.Maintainer, draft.Id,
                draft.Version, OperationId.New(), ReviewHost.Now.AddMinutes(2))).Proposal!;
        }
        var winner = await Replacement("First replacement");
        var stale = await Replacement("Competing replacement");
        using var scope = host.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IInteractionProvider>();
        var session = provider.Open(stale.Id.ToString(), "accept");
        var prepared = await session.PrepareAsync(new Dictionary<string, string>());
        await repository.DecideAsync(ReviewHost.Organization, winner.Id, winner.ProposedAtUtc,
            ReviewHost.Maintainer, DecisionOutcome.Accepted, "", OperationId.New(), ReviewHost.Now.AddMinutes(3));

        var actions = await provider.GetActionsAsync(stale.Id.ToString());
        Assert.True(actions.Single(action => action.Id == "reject").IsEnabled);
        var acceptance = actions.Single(action => action.Id == "accept");
        Assert.False(acceptance.IsEnabled);
        Assert.Contains("no longer accepted", acceptance.UnavailableReason);
        var result = await session.ConfirmAsync(prepared.Confirmation!.Token);
        Assert.Equal(InteractionResultKind.Conflict, result.Kind);
        Assert.Contains(result.Messages, message => message.Text.Contains("target is no longer accepted"));
    }

    [Theory]
    [InlineData("accept", "Acceptance note", false)]
    [InlineData("reject", "Rejection reason", true)]
    public async Task PackageDescribesFieldsAndExactProposal(string action, string label, bool required)
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var session = Open(scope, proposal, action);
        var view = await session.LoadAsync();
        Assert.True(view.IsAvailable);
        Assert.Equal(label, Assert.Single(view.Fields).Label);
        Assert.Equal(required, view.Fields[0].Required);
        Assert.Equal(DecisionNoteValidator.MaximumLength, view.Fields[0].MaxLength);
        Assert.Contains(view.Sections, section => section.Heading == "Decision" && section.Text == proposal.Content.Decision);
        Assert.Null(view.Confirmation);
    }

    [Fact]
    public async Task InvalidInputStaysEditableAndCannotBeConfirmed()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var session = Open(scope, proposal, "reject");
        var view = await session.PrepareAsync(new Dictionary<string, string> { ["note"] = "" });
        Assert.Null(view.Confirmation);
        Assert.Contains(view.Messages, message => message.FieldId == "note");
        Assert.Equal(InteractionResultKind.Invalid, (await session.ConfirmAsync("invented-token")).Kind);
        Assert.Null((await host.Services.GetRequiredService<IProposalRepository>().GetAsync(ReviewHost.Organization, proposal.Id))!.FinalDecision);
    }

    [Fact]
    public async Task ConfirmationUsesNormalizedSnapshotAndCanBeRetried()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var session = Open(scope, proposal, "reject");
        var inputs = new Dictionary<string, string> { ["note"] = "  More evidence needed  " };
        var prepared = await session.PrepareAsync(inputs);
        Assert.Equal("More evidence needed", Assert.Single(prepared.Fields).Value);
        Assert.Contains(prepared.Confirmation!.Sections, section => section.Text == "More evidence needed");
        inputs["note"] = "changed after preview";
        Assert.Equal(InteractionResultKind.Completed, (await session.ConfirmAsync(prepared.Confirmation.Token)).Kind);
        Assert.Equal(InteractionResultKind.Completed, (await session.ConfirmAsync(prepared.Confirmation.Token)).Kind);
        var record = await host.Services.GetRequiredService<IProposalRepository>().GetAsync(ReviewHost.Organization, proposal.Id);
        Assert.Equal("More evidence needed", record!.FinalDecision!.Note);
    }

    [Fact]
    public async Task EditingInvalidatesPreviousConfirmation()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var session = Open(scope, proposal, "accept");
        var original = await session.PrepareAsync(new Dictionary<string, string>());
        var updated = await session.PrepareAsync(new Dictionary<string, string> { ["note"] = "Updated" });
        Assert.NotEqual(original.Confirmation!.Token, updated.Confirmation!.Token);
        Assert.Equal(InteractionResultKind.Invalid, (await session.ConfirmAsync(original.Confirmation.Token)).Kind);
        Assert.Equal(InteractionResultKind.Completed, (await session.ConfirmAsync(updated.Confirmation.Token)).Kind);
    }

    [Fact]
    public async Task AuthorizationIsRecheckedWhenConfirmationCommits()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var session = Open(scope, proposal, "accept");
        var prepared = await session.PrepareAsync(new Dictionary<string, string>());
        host.Membership.Enabled = false;
        Assert.Equal(InteractionResultKind.Unavailable, (await session.ConfirmAsync(prepared.Confirmation!.Token)).Kind);
        var unavailable = await session.LoadAsync();
        Assert.False(unavailable.IsAvailable);
        Assert.Empty(unavailable.Sections);
        Assert.Empty(unavailable.Fields);
    }

    [Fact]
    public async Task DirectPrepareWithoutAuthorityReturnsNoRecordOrConfirmation()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ReviewHost.Caller>().Member = new MemberId("ordinary-member");
        var session = Open(scope, proposal, "accept");
        var view = await session.PrepareAsync(new Dictionary<string, string>());
        Assert.False(view.IsAvailable);
        Assert.Empty(view.Sections);
        Assert.Null(view.Confirmation);
    }

    [Fact]
    public async Task CompetingDecisionReturnsPackageOwnedConflictMessage()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var first = Open(scope, proposal, "accept");
        var second = Open(scope, proposal, "reject");
        var a = await first.PrepareAsync(new Dictionary<string, string>());
        var b = await second.PrepareAsync(new Dictionary<string, string> { ["note"] = "Declined" });
        Assert.Equal(InteractionResultKind.Completed, (await first.ConfirmAsync(a.Confirmation!.Token)).Kind);
        var conflict = await second.ConfirmAsync(b.Confirmation!.Token);
        Assert.Equal(InteractionResultKind.Conflict, conflict.Kind);
        Assert.Contains(conflict.Messages, message => message.Text.Contains("Another maintainer"));
    }

    [Theory]
    [InlineData("not-a-guid", "accept")]
    [InlineData("00000000-0000-0000-0000-000000000000", "accept")]
    [InlineData("89fce846-2af2-4ead-8136-2ee7e46dd7fb", "unknown")]
    public async Task InvalidRoutesCannotBecomeDecisions(string id, string action)
    {
        using var host = new ReviewHost();
        using var scope = host.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IInteractionProvider>();
        var session = provider.Open(id, action);
        Assert.False((await session.LoadAsync()).IsAvailable);
        Assert.Null((await session.PrepareAsync(new Dictionary<string, string>())).Confirmation);
    }

    [Fact]
    public async Task LostReplyRetriesSameCommandEvenIfCommitAlreadySucceeded()
    {
        using var host = new ReviewHost();
        var proposal = await host.SeedAsync();
        using var scope = host.Services.CreateScope();
        var flaky = new LostReplyReview(scope.ServiceProvider.GetRequiredService<IProposalReview>());
        var session = new ProposalReviewInteractionProvider(flaky).Open(proposal.Id.ToString(), "accept");
        var prepared = await session.PrepareAsync(new Dictionary<string, string>());
        var uncertain = await session.ConfirmAsync(prepared.Confirmation!.Token);
        Assert.Equal(InteractionResultKind.RetryableFailure, uncertain.Kind);
        var attemptedEdit = await session.PrepareAsync(new Dictionary<string, string> { ["note"] = "Changed" });
        Assert.Null(attemptedEdit.Confirmation);
        Assert.Equal(InteractionResultKind.Completed, (await session.ConfirmAsync(prepared.Confirmation.Token)).Kind);
        Assert.Equal(2, flaky.Commands.Count);
        Assert.Equal(flaky.Commands[0], flaky.Commands[1]);
    }

    private static IInteractionSession Open(IServiceScope scope, AdrProposal proposal, string action) =>
        scope.ServiceProvider.GetRequiredService<IInteractionProvider>().Open(proposal.Id.ToString(), action);

    private sealed class LostReplyReview(IProposalReview inner) : IProposalReview
    {
        public List<ReviewDecision> Commands { get; } = [];
        public Task<PrepareDecisionResult> PrepareAsync(AdrId id, DecisionOutcome outcome, string note,
            CancellationToken cancellationToken = default) => inner.PrepareAsync(id, outcome, note, cancellationToken);
        public async Task<DecisionCommandResult> DecideAsync(ReviewDecision command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            var result = await inner.DecideAsync(command, cancellationToken);
            if (Commands.Count == 1) throw new IOException("Simulated response loss after persistence");
            return result;
        }
    }
}
