namespace AdrCampus.Core.Domain;

public sealed record DraftTitle
{
    public const int MinimumLength = 5;
    public const int MaximumLength = 160;

    public DraftTitle(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new DraftValidationException(DraftValidationCode.TitleRequired, "A title is required.");
        }
        if (normalized.Length < MinimumLength)
        {
            throw new DraftValidationException(
                DraftValidationCode.TitleTooShort,
                $"The title must contain at least {MinimumLength} characters.");
        }
        if (normalized.Length > MaximumLength)
        {
            throw new DraftValidationException(
                DraftValidationCode.TitleTooLong,
                $"The title must contain no more than {MaximumLength} characters.");
        }
        if (normalized.Any(char.IsControl))
        {
            throw new DraftValidationException(
                DraftValidationCode.TitleContainsControlCharacter,
                "The title cannot contain control characters.");
        }
        if (!normalized.Any(char.IsLetterOrDigit))
        {
            throw new DraftValidationException(
                DraftValidationCode.TitleRequiresLetterOrNumber,
                "The title must contain at least one letter or number.");
        }

        Value = normalized;
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public enum DraftValidationCode
{
    TitleRequired,
    TitleTooShort,
    TitleTooLong,
    TitleContainsControlCharacter,
    TitleRequiresLetterOrNumber
}

public sealed class DraftValidationException(DraftValidationCode code, string message)
    : ArgumentException(message, "title")
{
    public DraftValidationCode Code { get; } = code;
}
