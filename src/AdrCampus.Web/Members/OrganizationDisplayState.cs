namespace AdrCampus.Web.Members;

public sealed class OrganizationDisplayState
{
    public event Action<string>? Changed;
    public void Set(string displayName) => Changed?.Invoke(displayName);
}
