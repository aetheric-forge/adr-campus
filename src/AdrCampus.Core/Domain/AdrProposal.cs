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
    long SourceDraftVersion)
{
    public AdrLifecycleStatus Status => AdrLifecycleStatus.Proposed;
}
