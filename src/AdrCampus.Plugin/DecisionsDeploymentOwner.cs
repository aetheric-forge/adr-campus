using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AdrCampus.Plugin;

/// <summary>Explicit existing-owner configuration for the single Decisions office supported today.
/// Resource owners may be ancestors of the immediate owner. These are declarations awaiting live
/// verification, not credentials, capability grants, or instructions to create a workspace.</summary>
public sealed record DecisionsDeploymentOwner(string Repository, string Revision, string LibraryOwnerId,
    string WorkspaceEnvironment, string WorkspaceOwnerId, string WorkspaceResourceId)
{
    public const string WorkbenchStage = "adr-campus-workbench";
    public const string LibraryScheme = "adr-campus";

    public static ImmutableDictionary<string, string> ParentSources { get; } =
        new Dictionary<string, string> { ["ILibrary"] = "owner.library", ["IWorkbench"] = "owner.workbench" }.ToImmutableDictionary();

    public DeploymentParentIdentity ToParentIdentity()
    {
        if (!Uri.TryCreate(Repository, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || Revision is null || !Regex.IsMatch(Revision, @"\A[0-9a-f]{40}\z"))
            throw new ArgumentException("Select an existing owner with an HTTPS repository and pinned 40-character revision.");
        foreach (var id in new[] { LibraryOwnerId, WorkspaceOwnerId, WorkspaceResourceId })
            if (id is null || !Regex.IsMatch(id, @"\A[a-z][a-z0-9-]*\z"))
                throw new ArgumentException("Resource owner and resource IDs must be stable lowercase slugs.");
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkspaceEnvironment);
        var location = JsonSerializer.Serialize(new
        {
            Version = 1, Environment = WorkspaceEnvironment, Institution = WorkspaceOwnerId,
            Resource = WorkspaceResourceId, Stage = WorkbenchStage
        });
        return new(Repository, Revision, new Dictionary<string, string>
        {
            ["ILibrary"] = LibraryOwnerId, ["IWorkbench"] = location
        }.ToImmutableDictionary());
    }

    public string ExportBindings(string deploymentEnvironment)
    {
        _ = ToParentIdentity(); // Do not export a nominal owner binding from invalid owner configuration.
        return OfficeProvisioningExport.Bindings(DecisionsDefinition.Current, deploymentEnvironment, ParentSources);
    }
}

// Independent JSON shape of runtime's institution-deployment-requested v2 Parent field.
public sealed record DeploymentParentIdentity(string Repository, string Revision,
    ImmutableDictionary<string, string> ResourceLocations);
