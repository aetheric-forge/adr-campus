using System.Text.Json;
using Xunit;

namespace AdrCampus.Plugin.Tests;

public sealed class OfficeDefinitionPortabilityTests
{
    [Fact]
    public void AnotherPackageGeneratesItsTemplateAndYamlAndEnforcesItsOwnRules()
    {
        var execution = new ExecutionReference("booking.reserve", 2, "booking.reservation");
        var definition = new OfficeDefinition(
            new("booking", "Booking Office", "1.0.0", "An independent package."),
            Domains: [new("reservations", "Reservations", "Reserve a room.", Capabilities: ["reserve-room"])],
            Organizations: [],
            Roles: [],
            Capabilities: [new("reserve-room", "Reserve Room", "Reserve a room.", Execution: execution)],
            Resources: [],
            Workflows: [new("reservation", "Reservation", "Complete a reservation.", Execution: execution)],
            Policies: [],
            Dependencies: [new("ICalendar", "The owner's room calendar.", true)],
            Package: new BookingPackage());

        var template = definition.CreateTemplate();
        Assert.Equal("Booking Office", template.Descriptor.Name);
        var capability = Assert.Single(template.Capabilities);
        Assert.Equal("Reserve Room", capability.Name);
        Assert.Contains(capability, Assert.Single(template.Domains).RequiredCapabilities);
        Assert.Equal("Reservation", Assert.Single(template.Workflows).Name);

        var yaml = definition.ToYaml();
        Assert.StartsWith("# Generated from the 'booking' package definition;", yaml);
        // The generated YAML uses JSON flow syntax after its header comments.
        using var document = JsonDocument.Parse(yaml[yaml.IndexOf('{')..]);
        var root = document.RootElement;
        Assert.Equal("booking", root.GetProperty("descriptor").GetProperty("id").GetString());
        Assert.Equal("ICalendar", root.GetProperty("dependencies")[0].GetProperty("contract").GetString());
        Assert.Equal("booking.reserve", root.GetProperty("capabilities")[0].GetProperty("execution").GetProperty("operation").GetString());
        Assert.False(root.TryGetProperty("package", out _));

        var missingBinding = definition with { Dependencies = [] };
        Assert.Contains("'ICalendar' binding", Assert.Throws<InvalidOperationException>(missingBinding.Validate).Message);

        var invalidExecution = execution with { Version = 99 };
        var invalid = definition with
        {
            Capabilities = [definition.Capabilities[0] with { Execution = invalidExecution }],
            Workflows = [definition.Workflows[0] with { Execution = invalidExecution }]
        };
        Assert.Equal("Unsupported booking execution.", Assert.Throws<InvalidOperationException>(invalid.Validate).Message);
    }

    private sealed class BookingPackage : IOfficePackage
    {
        public string Id => "booking";
        public IReadOnlyCollection<string> RequiredParentContracts { get; } = ["ICalendar"];

        public void ValidateExecution(ExecutionReference execution)
        {
            if (execution != new ExecutionReference("booking.reserve", 2, "booking.reservation"))
                throw new InvalidOperationException("Unsupported booking execution.");
        }
    }
}
