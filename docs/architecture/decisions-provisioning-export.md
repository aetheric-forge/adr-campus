# Decisions provisioning export

`DecisionsDefinition.Current` remains the authority for the Decisions package. Two deliberately separate artifacts are generated from it:

- `institution/decisions-institution.yaml` describes the executable package, including operation and interaction references.
- `institution/provisioning/decisions.yaml` projects its resource requirements into the pinned runtime provisioner's schema. `decisions.bindings.yaml` beside it contains generated **example** parent sources.

Generate or verify the provisioning pair from the repository root:

```sh
dotnet run --project tools/AdrCampus.Definition -- --write-provisioning institution/provisioning
dotnet run --project tools/AdrCampus.Definition -- --check-provisioning institution/provisioning
```

The existing `--write` and `--check` commands continue to manage the package artifact. Both formats use YAML 1.2 JSON flow syntax. Neither projection changes the running package or introduces a second hand-maintained definition.

## Projection rules

Descriptor identity/version, entries, descriptions, role capability references and parent resource ownership are retained. Domain `capabilities` becomes `requiredCapabilities`. Operation/interaction `execution` references are validated by the package and then excluded from the provisioning projection: the provisioner does not load code or register interactions. Policies remain descriptions, not authority grants.

Only dependencies marked `Required` become provisioning dependencies. For the current review package these are `ILibrary` and `IWorkbench`. Optional Archive, Post Office and Registrar entries describe wider host responsibilities; exporting them as mandatory would invent deployment requirements for this package slice. They remain in the package artifact and are not asserted to be configured by this export. The projection supplies empty `initialState.configuration`; it does not invent organization settings or membership mappings.

`OfficeProvisioningExport.Bindings` takes an explicit environment and exactly one nonempty source per required parent contract. Missing, blank and extra sources fail rather than producing an incomplete or misleading binding. The command-line example uses `owner.library` and `owner.workbench`; these are logical placeholders, not discovered live capabilities. Deployment-specific bindings must be resolved and reviewed separately, and definition/bindings documents must come from the same pinned source revision for the current single-institution API.

## Verified scope and remaining work

The plugin tests load the generated pair using the real `InstitutionYamlReader` and pass its output to `ProvisioningPlanner`. The resulting plan has exactly two parent checks and no owned-resource provisioning steps. Tests compare both checked-in artifacts to generator output and verify that a missing Workbench binding is rejected by the planner.

The runtime pin is updated to `b343383` (merged secret persistence and preflight fixes), providing the reader/planner used by these tests. Provisioning assemblies are test dependencies only; the plugin's production dependency graph is unchanged.

A valid plan is not a successful deployment: live owner capability resolution, including Workbench support, is the next integration slice. Resource verification does not mount the Decisions organization, install its package, configure membership, or expose its UI. The package still supports one office per service provider with its existing storage names. No live infrastructure execution is performed by generation or these compatibility tests.

Validation for this change: Release solution build succeeded and all 245 tests passed (no skips), including four new export tests. Generator write/check, stale-artifact rejection, and the existing package artifact check passed. Build output includes dependency advisories for SharpCompress 0.30.1 and Snappier 1.0.0, plus runtime compiler warnings; dependency remediation is outside this export change.
