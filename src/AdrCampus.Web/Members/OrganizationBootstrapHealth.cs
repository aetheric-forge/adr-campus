using AdrCampus.Application.Administration;

namespace AdrCampus.Web.Members;

public sealed class OrganizationBootstrapHealth
{
    public BootstrapOrganizationResult? Result { get; private set; }
    public void Record(BootstrapOrganizationResult result) => Result = result;
}
