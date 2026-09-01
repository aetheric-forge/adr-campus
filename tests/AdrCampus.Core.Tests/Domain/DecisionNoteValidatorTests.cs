using AdrCampus.Core.Domain;
namespace AdrCampus.Core.Tests.Domain;
public sealed class DecisionNoteValidatorTests
{
    [Fact] public void AcceptancePermitsAnEmptyNote() { var result = DecisionNoteValidator.Validate(DecisionOutcome.Accepted, "   "); Assert.True(result.IsValid); Assert.Equal(string.Empty, result.Note); }
    [Fact] public void RejectionRequiresAReason() { var result = DecisionNoteValidator.Validate(DecisionOutcome.Rejected, "   "); Assert.False(result.IsValid); Assert.Contains(result.Errors, e => e.Code == DecisionNoteValidationCode.Required); }
    [Fact] public void NormalizesAValidNote() { Assert.Equal("Because the constraint is unmet.", DecisionNoteValidator.Validate(DecisionOutcome.Rejected, "  Because the constraint is unmet.  ").Note); }
    [Fact] public void RejectsInvalidLengthPunctuationAndControls() { Assert.Contains(DecisionNoteValidator.Validate(DecisionOutcome.Accepted, new string('a', 1001)).Errors, e => e.Code == DecisionNoteValidationCode.TooLong); Assert.Contains(DecisionNoteValidator.Validate(DecisionOutcome.Accepted, "---").Errors, e => e.Code == DecisionNoteValidationCode.RequiresLetterOrNumber); Assert.Contains(DecisionNoteValidator.Validate(DecisionOutcome.Accepted, "bad\tnote").Errors, e => e.Code == DecisionNoteValidationCode.ContainsControlCharacter); }
}
