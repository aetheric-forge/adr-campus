# Executable Decisions definition

The package now has one authoritative definition: `DecisionsDefinition.Current`. The factory derives
its runtime template from it, and package service registration follows its execution references.
`institution/decisions-institution.yaml` is generated documentation of that same definition.

This implements the generated-artifact option from ownership milestone 3. It is a package-local
composition contract, not a provisioner format or a general workflow engine. No runtime or shared
contracts change is needed for this slice.

## What drives execution

The `review-and-decide` capability and `proposal-review` workflow reference operation
`decisions.proposal-review`, version `1`, with interaction `decisions.review`. The package's explicit
dispatch table resolves that reference to `IProposalReview` and `ProposalReviewInteractionProvider`.
Duplicate references across capability and workflow register a single scoped provider. Strings cannot
instantiate arbitrary types. Caller identity and maintainer authority are still checked by the existing
review operation at execution time.

Template domains and roles resolve capability IDs to the declared capability objects. The mounted
template now includes capabilities, domains, organizations, roles, resources, workflows and policies.
The definition retains stable IDs even though the current runtime template types expose names rather
than those IDs. Consumers needing execution references should use the package definition.

Validation rejects unknown operation, interaction and capability references; unsupported operation
versions; duplicate entry IDs; workflows whose execution is not exposed by a capability; unsupported
required parent bindings; and unsupported resource ownership. Validation runs before template creation
and before interaction registration. Mounting checks that required Library and Workbench capabilities
are actually available from the owner, with a diagnostic naming the missing capability.

## What remains descriptive

Entries without `execution` describe application behavior; they do not claim that a package operation
has been registered for it. Policies are always descriptive and cannot carry execution references.
Editing policy prose neither grants a role nor implements a rule. Authoritative rules remain in the
application services and repositories.

Archive, Post Office and Registrar entries record remaining host responsibilities and are not
requirements of the proposal-review mount. Their `required: false` means this package slice does not
validate them; the wider web application still needs its configured infrastructure and identity.

Both resources currently have parent ownership. The old YAML's claim of an office-owned Workbench
was ahead of the implementation. Dedicated workspaces and multiple office instances remain milestone 4.
Existing stage and knowledge scheme names are unchanged. The old YAML-only draft-expiry initial state
has been removed: organization bootstrap remains authoritative for organization settings.

The sibling bindings YAML is an explicitly labelled deployment example. It is not loaded. Actual
bindings enter through `AddDecisionsOffice` and the owning institution's capabilities. This does not
claim to implement or validate the provisioner's deployment configuration format.

## Updating the definition

Edit `src/AdrCampus.Plugin/DecisionsDefinition.cs`, then run from the repository root:

```sh
dotnet run --project tools/AdrCampus.Definition -- --write institution/decisions-institution.yaml
dotnet run --project tools/AdrCampus.Definition -- --check institution/decisions-institution.yaml
dotnet test tests/AdrCampus.Plugin.Tests
```

The generated file uses YAML 1.2's JSON flow syntax, produced by the built-in JSON serializer. No YAML
parser dependency is needed. The test suite compares the checked-in artifact to the generator output,
so stale documentation fails the normal package tests. Editing YAML directly has no runtime effect.
Adding an executable workflow requires implementing and registering its operation in the package
before referencing it in the definition.

## Live acceptance status

On 2026-09-17, the merged application was started on Minerva for the requested authenticated browser
review. Startup failed with `Configuration value 'Organization:MemberGroupId' is required.` The
checkout's default identity/storage settings are empty and no Minerva user-secrets store was present.
No authenticated session or provisioned test deployment was available. This is a deployment setup
blocker, not evidence of a Registry implementation failure. Browser sign-in and proposal review remain
pending the provisioned environment. The earlier Playwright smoke test proves browser setup only.

Once that environment is available, verify sign-in, accept and reject on disposable test proposals,
required rejection notes, confirmation content, return navigation, and a second review after the
proposal has already been decided. Use a member-only test account to verify unavailable review actions
and protected direct routes. Package tests cover direct invocation and retry guarantees independently.
