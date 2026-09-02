using AdrCampus.Core.Administration;
using AdrCampus.Core.Domain;

namespace AdrCampus.Core.Tests;

public sealed class OrganizationAdministrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DisplayNameIsNormalizedAndConstrained()
    {
        Assert.Equal("Aetheric Forge", new OrganizationDisplayName("  Aetheric Forge  ").Value);
        Assert.Equal(OrganizationNameValidationCode.TooShort, Assert.Throws<OrganizationNameValidationException>(() => new OrganizationDisplayName("ab")).Code);
        Assert.Equal(OrganizationNameValidationCode.MissingLetterOrNumber, Assert.Throws<OrganizationNameValidationException>(() => new OrganizationDisplayName("---")).Code);
        Assert.Equal(OrganizationNameValidationCode.ContainsControlCharacter, Assert.Throws<OrganizationNameValidationException>(() => new OrganizationDisplayName("Forge\nCampus")).Code);
    }

    [Fact]
    public void RenamePreservesAuthorityAndAdvancesVersion()
    {
        var state = OrganizationAdministrationState.Bootstrap(new("forge"), new("Aetheric Forge"), "https://sso", "members", "maintainers", Now);
        var renamed = state.Rename(new("Forge Campus"), state.Version, Now.AddMinutes(1));
        Assert.Equal(2, renamed.Version);
        Assert.Equal("Forge Campus", renamed.DisplayName.Value);
        Assert.True(renamed.HasSameAuthorityConfiguration("https://sso", "members", "maintainers"));
        Assert.Equal(state.InitializedAtUtc, renamed.InitializedAtUtc);
    }
}
