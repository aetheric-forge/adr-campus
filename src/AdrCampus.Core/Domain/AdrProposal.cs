namespace AdrCampus.Core.Domain;

public sealed record ProposalContent(DraftTitle Title, string Context, string Decision, string Consequences);

public sealed record ProposalValidationError(string Field, ProposalValidationCode Code, string Message);
public enum ProposalValidationCode { Required, TooLong, RequiresLetterOrNumber, ContainsControlCharacter }
public sealed record ProposalValidationResult(ProposalContent? Content, IReadOnlyList<ProposalValidationError> Errors)
{
    public bool IsValid => Content is not null && Errors.Count == 0;
}

public static class ProposalValidator
{
    public const int NarrativeMaximumLength = 4000;
    public static ProposalValidationResult Validate(DraftContent draft)
    {
        var errors = new List<ProposalValidationError>();
        var context = ValidateSection("Context", draft.Context, errors);
        var decision = ValidateSection("Decision", draft.Decision, errors);
        var consequences = ValidateSection("Consequences", draft.Consequences, errors);
        return errors.Count == 0
            ? new ProposalValidationResult(new ProposalContent(draft.Title, context, decision, consequences), errors)
            : new ProposalValidationResult(null, errors);
    }

    private static string ValidateSection(string field, string value, List<ProposalValidationError> errors)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0) errors.Add(new(field, ProposalValidationCode.Required, $"{field} is required."));
        else
        {
            if (normalized.Length > NarrativeMaximumLength) errors.Add(new(field, ProposalValidationCode.TooLong, $"{field} must contain no more than {NarrativeMaximumLength} characters."));
            if (!normalized.Any(char.IsLetterOrDigit)) errors.Add(new(field, ProposalValidationCode.RequiresLetterOrNumber, $"{field} must contain at least one letter or number."));
            if (normalized.Any(c => char.IsControl(c) && c is not '\r' and not '\n')) errors.Add(new(field, ProposalValidationCode.ContainsControlCharacter, $"{field} contains a control character that is not allowed."));
        }
        return normalized;
    }
}

public sealed record AdrProposal(
    AdrId Id,
    OrganizationId OrganizationId,
    MemberId AuthorId,
    MemberId ProposerId,
    ProposalContent Content,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ProposedAtUtc,
    long SourceDraftVersion,
    AdrDecision? FinalDecision = null)
{
    public AdrLifecycleStatus Status => FinalDecision?.Outcome == DecisionOutcome.Accepted
        ? AdrLifecycleStatus.Accepted
        : FinalDecision?.Outcome == DecisionOutcome.Rejected
            ? AdrLifecycleStatus.Rejected
            : AdrLifecycleStatus.Proposed;

    public AdrProposal Decide(DecisionOutcome outcome, MemberId deciderId, string note, DateTimeOffset decidedAtUtc)
    {
        if (FinalDecision is not null) throw new InvalidOperationException("The ADR already has a final decision.");
        var validation = DecisionNoteValidator.Validate(outcome, note);
        if (!validation.IsValid) throw new ArgumentException("The decision note is invalid.", nameof(note));
        return this with { FinalDecision = new AdrDecision(outcome, deciderId, decidedAtUtc, validation.Note!) };
    }
}

public enum DecisionOutcome { Accepted, Rejected }
public sealed record AdrDecision(DecisionOutcome Outcome, MemberId DeciderId, DateTimeOffset DecidedAtUtc, string Note);
public sealed record DecisionNoteValidationError(DecisionNoteValidationCode Code, string Message);
public enum DecisionNoteValidationCode { Required, TooLong, RequiresLetterOrNumber, ContainsControlCharacter }
public sealed record DecisionNoteValidationResult(string? Note, IReadOnlyList<DecisionNoteValidationError> Errors)
{
    public bool IsValid => Note is not null && Errors.Count == 0;
}
public static class DecisionNoteValidator
{
    public const int MaximumLength = 1000;
    public static DecisionNoteValidationResult Validate(DecisionOutcome outcome, string? note)
    {
        var normalized = (note ?? string.Empty).Trim();
        var errors = new List<DecisionNoteValidationError>();
        if (normalized.Length == 0 && outcome == DecisionOutcome.Rejected)
            errors.Add(new(DecisionNoteValidationCode.Required, "A rejection reason is required."));
        if (normalized.Length > MaximumLength)
            errors.Add(new(DecisionNoteValidationCode.TooLong, $"The decision note must contain no more than {MaximumLength} characters."));
        if (normalized.Length > 0 && !normalized.Any(char.IsLetterOrDigit))
            errors.Add(new(DecisionNoteValidationCode.RequiresLetterOrNumber, "The decision note must contain at least one letter or number."));
        if (normalized.Any(c => char.IsControl(c) && c is not '\r' and not '\n'))
            errors.Add(new(DecisionNoteValidationCode.ContainsControlCharacter, "The decision note contains a control character that is not allowed."));
        return errors.Count == 0 ? new(normalized, errors) : new(null, errors);
    }
}
