using System.Text.Json;
using System.Text.Json.Serialization;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Abstractions.Models;
using AethericForge.Runtime.Institutions.Abstractions.Primitives;

namespace AdrCampus.Plugin;

public sealed record OfficeDescriptor(string Id, string Name, string Version, string Description);
public sealed record ExecutionReference(string Operation, int Version, string Interaction);
public sealed record ParentRequirement(string Contract, string Reason, bool Required);

// Entries without Execution are descriptive; policy prose never grants authority or executes rules.
public sealed record OfficeEntry(string Id, string Name, string Description,
    IReadOnlyList<string>? Capabilities = null, string? Type = null, string? Ownership = null,
    ExecutionReference? Execution = null);

public sealed record OfficeDefinition(OfficeDescriptor Descriptor,
    IReadOnlyList<OfficeEntry> Domains, IReadOnlyList<OfficeEntry> Organizations,
    IReadOnlyList<OfficeEntry> Roles, IReadOnlyList<OfficeEntry> Capabilities,
    IReadOnlyList<OfficeEntry> Resources, IReadOnlyList<OfficeEntry> Workflows,
    IReadOnlyList<OfficeEntry> Policies, IReadOnlyList<ParentRequirement> Dependencies)
{
    [JsonIgnore]
    public IEnumerable<ExecutionReference> Executions => Capabilities.Concat(Workflows)
        .Where(x => x.Execution is not null).Select(x => x.Execution!).Distinct();

    public void Validate()
    {
        if (Descriptor.Id != "decisions" || !Version.TryParse(Descriptor.Version, out _))
            throw new InvalidOperationException("Decisions definition requires id 'decisions' and a valid package version.");
        var groups = new[] { Domains, Organizations, Roles, Capabilities, Resources, Workflows, Policies };
        foreach (var entries in groups)
        {
            if (entries.Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name)) ||
                entries.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                throw new InvalidOperationException("Definition entries require non-empty, unique IDs and names within each category.");
        }
        var capabilities = Capabilities.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in Domains.Concat(Roles))
            foreach (var reference in entry.Capabilities ?? [])
                if (!capabilities.Contains(reference))
                    throw new InvalidOperationException($"Definition '{entry.Id}' references unknown capability '{reference}'.");
        foreach (var entry in Domains.Concat(Organizations).Concat(Roles).Concat(Resources).Concat(Policies))
            if (entry.Execution is not null)
                throw new InvalidOperationException($"'{entry.Id}' cannot declare execution; only capabilities and workflows can.");
        foreach (var resource in Resources)
            if (string.IsNullOrWhiteSpace(resource.Type) || resource.Ownership != "parent")
                throw new InvalidOperationException($"Resource '{resource.Id}' requires a type and parent ownership; office-owned provisioning is not supported yet.");
        foreach (var execution in Executions)
            DecisionsOperations.Validate(execution);
        if (Dependencies.Select(x => x.Contract).Distinct(StringComparer.Ordinal).Count() != Dependencies.Count)
            throw new InvalidOperationException("Duplicate parent capability requirements.");
        foreach (var dependency in Dependencies.Where(x => x.Required))
            if (dependency.Contract is not ("ILibrary" or "IWorkbench"))
                throw new InvalidOperationException($"Unknown required parent binding '{dependency.Contract}'.");
        // The repository composition needs both even if a presentation entry is removed.
        foreach (var contract in new[] { "ILibrary", "IWorkbench" })
            if (!Dependencies.Any(x => x.Contract == contract && x.Required))
                throw new InvalidOperationException($"Decisions repository composition requires the '{contract}' binding.");
        foreach (var workflow in Workflows.Where(x => x.Execution is not null))
            if (!Capabilities.Any(x => x.Execution == workflow.Execution))
                throw new InvalidOperationException($"Workflow '{workflow.Id}' must reference an execution exposed by a capability.");
    }

    public IInstitutionTemplate CreateTemplate()
    {
        Validate();
        var builder = InstitutionTemplateBuilder.Create()
            .WithDescriptor(Descriptor.Name, Version.Parse(Descriptor.Version), Descriptor.Description);
        var capabilities = Capabilities.ToDictionary(x => x.Id, x => new CapabilityDefinition(x.Name, x.Description));
        foreach (var capability in capabilities.Values) builder.AddCapability(capability);
        foreach (var entry in Domains)
            builder.AddDomain(new DomainDefinition(entry.Name, entry.Description,
                (entry.Capabilities ?? []).Select(id => capabilities[id])));
        foreach (var entry in Organizations) builder.AddOrganization(new OrganizationDefinition(entry.Name, entry.Description));
        foreach (var entry in Roles)
            builder.AddRole(new RoleDefinition(entry.Name, entry.Description,
                (entry.Capabilities ?? []).Select(id => capabilities[id])));
        foreach (var entry in Resources) builder.AddResource(new ResourceDefinition(entry.Name, entry.Type!, entry.Description));
        foreach (var entry in Workflows) builder.AddWorkflow(new WorkflowDefinition(entry.Name, entry.Description));
        foreach (var entry in Policies) builder.AddPolicy(new PolicyDefinition(entry.Name, entry.Description));
        // Organization settings are initialized by application bootstrap, not by this template.
        return builder.Build();
    }

    public string ToYaml()
    {
        Validate();
        // JSON flow syntax is valid YAML 1.2; use the built-in serializer, with no YAML runtime dependency.
        return "# Generated from DecisionsDefinition.Current; edit the C# definition and regenerate.\n" +
            "# YAML 1.2 (JSON flow syntax). Execution references are validated package links.\n" +
            "# Entries without execution, including all policies, are descriptive only.\n" +
            JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            }) + "\n";
    }
}
