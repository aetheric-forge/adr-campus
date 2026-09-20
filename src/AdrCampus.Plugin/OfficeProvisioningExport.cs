using System.Text.Json;

namespace AdrCampus.Plugin;

/// <summary>Resource/dependency projection of a validated package definition. Execution references
/// stay in the package artifact; this export neither installs the package nor assigns authority.</summary>
public static class OfficeProvisioningExport
{
    private static object Entry(OfficeEntry entry) => new { id = entry.Id, name = entry.Name, description = entry.Description };

    public static string Definition(OfficeDefinition definition)
    {
        definition.Validate();
        return Serialize(new
        {
            descriptor = new { id = definition.Descriptor.Id, name = definition.Descriptor.Name,
                version = definition.Descriptor.Version, description = definition.Descriptor.Description },
            dependencies = definition.Dependencies.Where(x => x.Required).Select(x => new { contract = x.Contract, reason = x.Reason }),
            domains = definition.Domains.Select(x => new { id = x.Id, name = x.Name, description = x.Description,
                requiredCapabilities = x.Capabilities ?? [] }),
            organizations = definition.Organizations.Select(Entry),
            roles = definition.Roles.Select(x => new { id = x.Id, name = x.Name, description = x.Description,
                capabilities = x.Capabilities ?? [] }),
            capabilities = definition.Capabilities.Select(Entry),
            resources = definition.Resources.Select(x => new { id = x.Id, name = x.Name, description = x.Description,
                type = x.Type, ownership = x.Ownership }),
            workflows = definition.Workflows.Select(Entry),
            policies = definition.Policies.Select(Entry),
            initialState = new { configuration = new Dictionary<string, string>() }
        });
    }

    public static string Bindings(OfficeDefinition definition, string environment,
        IReadOnlyDictionary<string, string> parentSources)
    {
        definition.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentNullException.ThrowIfNull(parentSources);
        var required = definition.Dependencies.Where(x => x.Required).Select(x => x.Contract).ToArray();
        if (parentSources.Count != required.Length || required.Any(key =>
            !parentSources.TryGetValue(key, out var source) || string.IsNullOrWhiteSpace(source)))
            throw new ArgumentException("Supply exactly the required parent contracts with nonempty sources.", nameof(parentSources));
        return Serialize(new
        {
            institution = definition.Descriptor.Id,
            version = definition.Descriptor.Version,
            deployment = new { name = environment },
            bindings = required.Order(StringComparer.Ordinal).ToDictionary(key => key, key => new { source = parentSources[key] })
        });
    }

    private static string Serialize(object document) =>
        "# Generated provisioning projection; edit the package definition and regenerate.\n" +
        JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }) + "\n";
}
