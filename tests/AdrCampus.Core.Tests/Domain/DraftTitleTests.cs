using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Tests.Domain;

public sealed class DraftTitleTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiresATitle(string value)
    {
        var exception = Assert.Throws<DraftValidationException>(() => new DraftTitle(value));
        Assert.Equal(DraftValidationCode.TitleRequired, exception.Code);
    }

    [Fact]
    public void RejectsATitleShorterThanFiveCharacters()
    {
        var exception = Assert.Throws<DraftValidationException>(() => new DraftTitle("ADR1"));
        Assert.Equal(DraftValidationCode.TitleTooShort, exception.Code);
    }

    [Fact]
    public void RejectsATitleLongerThanOneHundredSixtyCharacters()
    {
        var exception = Assert.Throws<DraftValidationException>(() => new DraftTitle(new string('a', 161)));
        Assert.Equal(DraftValidationCode.TitleTooLong, exception.Code);
    }

    [Fact]
    public void RejectsATitleWithoutALetterOrNumber()
    {
        var exception = Assert.Throws<DraftValidationException>(() => new DraftTitle("---?!"));
        Assert.Equal(DraftValidationCode.TitleRequiresLetterOrNumber, exception.Code);
    }

    [Fact]
    public void RejectsControlCharacters()
    {
        var exception = Assert.Throws<DraftValidationException>(() => new DraftTitle("Valid\nTitle"));
        Assert.Equal(DraftValidationCode.TitleContainsControlCharacter, exception.Code);
    }

    [Fact]
    public void NormalizesSurroundingWhitespace()
    {
        var title = new DraftTitle("  Choose PostgreSQL  ");
        Assert.Equal("Choose PostgreSQL", title.Value);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(160)]
    public void AcceptsBoundaryLengths(int length)
    {
        var title = new DraftTitle(new string('a', length));
        Assert.Equal(length, title.Value.Length);
    }
}
