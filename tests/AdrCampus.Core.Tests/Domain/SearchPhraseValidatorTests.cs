using AdrCampus.Core.Discovery;
namespace AdrCampus.Core.Tests.Domain;
public sealed class SearchPhraseValidatorTests
{
    [Fact] public void EmptyPhraseClearsSearch() { var result = SearchPhraseValidator.Validate("   "); Assert.True(result.IsValid); Assert.Equal(string.Empty, result.Phrase); }
    [Fact] public void NormalizesAValidPhrase() { Assert.Equal("Postgres provider", SearchPhraseValidator.Validate("  Postgres provider  ").Phrase); }
    [Fact] public void RejectsTooShortAndTooLongPhrases() { Assert.Contains(SearchPhraseValidator.Validate("ab").Errors, error => error.Code == SearchValidationCode.TooShort); Assert.Contains(SearchPhraseValidator.Validate(new string('a', 201)).Errors, error => error.Code == SearchValidationCode.TooLong); }
    [Fact] public void RequiresLetterOrNumberAndRejectsControls() { Assert.Contains(SearchPhraseValidator.Validate("---").Errors, error => error.Code == SearchValidationCode.RequiresLetterOrNumber); Assert.Contains(SearchPhraseValidator.Validate("bad\tquery").Errors, error => error.Code == SearchValidationCode.ContainsControlCharacter); }
}
