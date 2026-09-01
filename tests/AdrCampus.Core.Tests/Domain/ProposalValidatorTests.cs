using AdrCampus.Core.Domain;
namespace AdrCampus.Core.Tests.Domain;
public sealed class ProposalValidatorTests
{
    [Fact] public void NormalizesCompleteNarrativeSections() { var result = ProposalValidator.Validate(new DraftContent("Choose PostgreSQL", "  Context  ", "  Decision  ", "  Consequences  ")); Assert.True(result.IsValid); Assert.Equal("Context", result.Content!.Context); Assert.Equal("Decision", result.Content.Decision); Assert.Equal("Consequences", result.Content.Consequences); }
    [Fact] public void ReportsEveryIncompleteSection() { var result = ProposalValidator.Validate(new DraftContent("Choose PostgreSQL")); Assert.False(result.IsValid); Assert.Equal(new[] { "Context", "Decision", "Consequences" }, result.Errors.Select(e => e.Field)); }
    [Fact] public void RejectsNarrativeLongerThanFourThousandCharacters() { var result = ProposalValidator.Validate(new DraftContent("Choose PostgreSQL", new string('a', 4001), "Decision", "Consequences")); Assert.Contains(result.Errors, e => e.Field == "Context" && e.Code == ProposalValidationCode.TooLong); }
    [Fact] public void RequiresALetterOrNumber() { var result = ProposalValidator.Validate(new DraftContent("Choose PostgreSQL", "---", "Decision", "Consequences")); Assert.Contains(result.Errors, e => e.Code == ProposalValidationCode.RequiresLetterOrNumber); }
    [Fact] public void PermitsLineBreaksButRejectsOtherControlCharacters() { Assert.True(ProposalValidator.Validate(new DraftContent("Choose PostgreSQL", "Line one\nLine two", "Decision", "Consequences")).IsValid); var invalid = ProposalValidator.Validate(new DraftContent("Choose PostgreSQL", "Bad\tcontext", "Decision", "Consequences")); Assert.Contains(invalid.Errors, e => e.Code == ProposalValidationCode.ContainsControlCharacter); }
}
