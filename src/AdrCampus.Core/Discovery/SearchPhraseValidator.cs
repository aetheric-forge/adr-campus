namespace AdrCampus.Core.Discovery;

public enum SearchValidationCode { TooShort, TooLong, RequiresLetterOrNumber, ContainsControlCharacter }
public sealed record SearchValidationError(SearchValidationCode Code, string Message);
public sealed record SearchValidationResult(string Phrase, IReadOnlyList<SearchValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class SearchPhraseValidator
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 200;

    public static SearchValidationResult Validate(string? phrase)
    {
        var normalized = (phrase ?? string.Empty).Trim();
        if (normalized.Length == 0) return new(normalized, []);
        var errors = new List<SearchValidationError>();
        if (normalized.Length < MinimumLength) errors.Add(new(SearchValidationCode.TooShort, $"Search must contain at least {MinimumLength} characters."));
        if (normalized.Length > MaximumLength) errors.Add(new(SearchValidationCode.TooLong, $"Search must contain no more than {MaximumLength} characters."));
        if (!normalized.Any(char.IsLetterOrDigit)) errors.Add(new(SearchValidationCode.RequiresLetterOrNumber, "Search must contain at least one letter or number."));
        if (normalized.Any(char.IsControl)) errors.Add(new(SearchValidationCode.ContainsControlCharacter, "Search contains a control character that is not allowed."));
        return new(normalized, errors);
    }
}
