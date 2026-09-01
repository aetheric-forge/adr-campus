namespace AdrCampus.Web.Drafts;

public static class DiscoveryReturnContext
{
    public static bool IsSafe(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.StartsWith('/') &&
        !value.StartsWith("//") &&
        !value.Contains('\\') &&
        !value.Any(char.IsControl);
}
