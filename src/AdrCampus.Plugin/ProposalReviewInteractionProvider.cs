using AethericContracts.Interactions;
using AdrCampus.Application.Proposals;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Proposals;
using Microsoft.Extensions.Logging;

namespace AdrCampus.Plugin;

/// <summary>Decisions owns the review form and result language; hosts render the shared vocabulary.</summary>
public sealed class ProposalReviewInteractionProvider(IProposalReview review,
    ILogger<ProposalReviewInteractionProvider>? logger = null) : IInteractionProvider
{
    public const string ProviderId = "decisions.review";
    public string Id => ProviderId;
    public async Task<IReadOnlyList<InteractionAction>> GetActionsAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(subjectId, out var id) || id == Guid.Empty) return [];
        var prepared = await review.PrepareAsync(new AdrId(id), DecisionOutcome.Accepted, "", cancellationToken).ConfigureAwait(false);
        if (!prepared.IsAuthorized || !prepared.IsFound) return [];
        var canAccept = prepared.Proposal!.IntendedSupersessionTargetId is null || prepared.Target?.Status == AdrLifecycleStatus.Accepted;
        return [new("reject", "Reject"), new("accept", "Accept", canAccept,
            canAccept ? null : "This proposal's frozen target is no longer accepted; reject it with a reason.")];
    }

    public IInteractionSession Open(string subjectId, string actionId) => new Session(review, logger, subjectId, actionId);

    private sealed class Session : IInteractionSession
    {
        private readonly IProposalReview _review;
        private readonly ILogger? _logger;
        private readonly AdrId? _proposalId;
        private readonly DecisionOutcome? _outcome;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private ReviewDecision? _pending;
        private string? _token;
        private bool _uncertain;

        public Session(IProposalReview review, ILogger? logger, string subjectId, string actionId)
        {
            _review = review;
            _logger = logger;
            if (Guid.TryParse(subjectId, out var id) && id != Guid.Empty) _proposalId = new AdrId(id);
            _outcome = actionId?.ToLowerInvariant() switch
            {
                "accept" => DecisionOutcome.Accepted,
                "reject" => DecisionOutcome.Rejected,
                _ => null
            };
        }

        public async Task<InteractionView> LoadAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_proposalId is null || _outcome is null) return Unavailable();
                var prepared = await _review.PrepareAsync(_proposalId.Value, _outcome.Value, "", cancellationToken).ConfigureAwait(false);
                return prepared.IsAuthorized && prepared.IsFound
                    ? Describe(prepared, "", [])
                    : Unavailable();
            }
            finally { _gate.Release(); }
        }

        public async Task<InteractionView> PrepareAsync(IReadOnlyDictionary<string, string> inputs,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Do not replace an operation whose commit may already have succeeded.
                if (_uncertain) return Unavailable("Retry the previous confirmation before changing this decision.");
                _pending = null;
                _token = null;
                if (_proposalId is null || _outcome is null) return Unavailable();
                var note = inputs.GetValueOrDefault("note") ?? "";
                var prepared = await _review.PrepareAsync(_proposalId.Value, _outcome.Value, note, cancellationToken).ConfigureAwait(false);
                if (!prepared.IsAuthorized || !prepared.IsFound) return Unavailable();
                var view = Describe(prepared, note,
                    prepared.Errors.Select(error => new InteractionMessage(error.Message, "note")).ToArray());
                if (!prepared.IsReady) return view;

                _pending = new ReviewDecision(prepared.Proposal!.Id, prepared.Proposal.ProposedAtUtc,
                    _outcome.Value, prepared.Note!, OperationId.New());
                _token = Guid.NewGuid().ToString("N");
                return view with
                {
                    Fields = view.Fields.Select(field => field with { Value = prepared.Note! }).ToArray(),
                    Confirmation = new InteractionConfirmation(_token,
                        $"This {_outcome.Value.ToString().ToLowerInvariant()} decision is final. Confirm the exact proposal and note shown below.",
                        "Confirm final decision",
                        [new("Outcome", _outcome.Value.ToString()), new(view.Fields[0].Label,
                            string.IsNullOrEmpty(prepared.Note) ? "No note will be recorded." : prepared.Note)])
                };
            }
            finally { _gate.Release(); }
        }

        public async Task<InteractionResult> ConfirmAsync(string confirmationToken,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_pending is null || _token is null || !string.Equals(confirmationToken, _token, StringComparison.Ordinal))
                    return new(InteractionResultKind.Invalid, [new("Review the decision again before confirming.")]);
                _uncertain = true;
                DecisionCommandResult result;
                try
                {
                    result = await _review.DecideAsync(_pending, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    _logger?.LogError(error, "Proposal review confirmation could not be verified");
                    return new(InteractionResultKind.RetryableFailure,
                        [new("The decision result could not be confirmed. Retry to check the same operation.")]);
                }
                _uncertain = false;
                return result.Status switch
                {
                    DecisionWriteStatus.Decided or DecisionWriteStatus.AlreadyApplied =>
                        new(InteractionResultKind.Completed, [new("Decision recorded.")]),
                    DecisionWriteStatus.UnauthorizedOrNotFound =>
                        new(InteractionResultKind.Unavailable, [new("The proposal or maintainer access is no longer available.")]),
                    DecisionWriteStatus.Conflict =>
                        new(InteractionResultKind.Conflict, [new("Another maintainer has already decided this proposal. Return to the record to see its current state.")]),
                    DecisionWriteStatus.TargetNotAccepted =>
                        new(InteractionResultKind.Conflict, [new("The intended target is no longer accepted. No records were changed; you may reject the stale proposal with a reason.")]),
                    DecisionWriteStatus.InvalidRelationship =>
                        new(InteractionResultKind.Conflict, [new("The supersession relationship would be invalid. No records were changed.")]),
                    DecisionWriteStatus.OperationMismatch =>
                        new(InteractionResultKind.Conflict, [new("The operation does not match its earlier attempt. Return to the record and review the decision again.")]),
                    _ => new(InteractionResultKind.Invalid, result.Errors.Count > 0
                        ? result.Errors.Select(error => new InteractionMessage(error.Message, "note")).ToArray()
                        : [new("The decision could not be recorded. Review the inputs and try again.")])
                };
            }
            finally { _gate.Release(); }
        }

        private InteractionView Describe(PrepareDecisionResult prepared, string note, IReadOnlyList<InteractionMessage> errors)
        {
            var proposal = prepared.Proposal!;
            var accepting = _outcome == DecisionOutcome.Accepted;
            var sections = new List<InteractionSection>
            {
                new("Context", proposal.Content.Context),
                new("Decision", proposal.Content.Decision),
                new("Consequences", proposal.Content.Consequences)
            };
            if (prepared.Target is not null)
                sections.Add(new("Intended supersession target",
                    $"{prepared.Target.Title.Value} · {prepared.Target.Status}. " +
                    (accepting ? "The target remains accepted until this replacement is accepted." : "Rejection leaves the target unchanged.")));
            return new($"{(accepting ? "Accept" : "Reject")} {proposal.Content.Title.Value}",
                accepting ? "You may record an optional note before reviewing the final outcome." : "Record the reason for rejection before reviewing the final outcome.",
                sections, [new("note", accepting ? "Acceptance note" : "Rejection reason",
                    $"{(accepting ? "Optional" : "Required")} · maximum {DecisionNoteValidator.MaximumLength:N0} characters",
                    !accepting, DecisionNoteValidator.MaximumLength, note)], "Review decision", errors);
        }

        private static InteractionView Unavailable(string? message = null) => new("Proposal unavailable",
            message ?? "This proposal is no longer awaiting your decision, or maintainer access is unavailable.",
            [], [], "", [], IsAvailable: false);
    }
}
