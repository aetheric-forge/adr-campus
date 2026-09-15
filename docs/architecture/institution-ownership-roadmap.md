# Institution Ownership and Host Integration Roadmap

Status: proposal for review with Brian and Claude
Date: 2026-09-15
Review baseline: ADR Campus `0a8a270`; runtime `aeaf565`

## Goal

A developer should be able to own and evolve one institution's behavior without routinely editing the integration website or maintaining a separate set of bespoke pages for that behavior.

For Decisions, the runtime classification is Organization: it derives authority from an owning Institution. This roadmap uses “feature package” for the developer-owned delivery unit; it does not propose changing that classification.

Success means that a supported change to a Decisions interaction can be delivered by updating its package and deployment configuration, with no Decisions-specific changes to the host's source code.

This is a proposed implementation direction, not a description of completed capabilities. The review covered source and contracts; it did not include an application run or test execution.

## Current position

Recent changes provide a useful foundation:

- Decisions mounts under an owner through `IOrganizationFactory`.
- The owner supplies shared Archive, Library, Post Office, and identity capabilities.
- The YAML definition separates office semantics from deployment bindings.
- Existing application services already separate decision behavior from Razor presentation.

The remaining gaps are concrete:

| Area | Current evidence | Consequence |
| --- | --- | --- |
| Operational boundary | `AdrCampusRecorder` has no operations; the plugin does not reference application or provider projects. | Installing the plugin mounts an organization but does not supply its working decision lifecycle. |
| Composition | `ForgeCampusExtensions` and `Program.cs` register feature repositories, services, and background work. | The host still knows how to assemble Decisions. |
| Definition | YAML describes capabilities and policies; the factory builds a descriptor-only template. There is no YAML loader. | Definition changes do not drive or validate running behavior. |
| Presentation | Razor pages call application services directly; navigation and forms are explicit ADR code. | Interaction changes can still require website changes. |
| Resource ownership | YAML declares an owned draft workspace; the host composes Workbench and the repository uses a fixed stage name. | Logical ownership is not yet fully expressed by package composition and bindings. |
| Verification | Factory tests check construction and registration. | They do not yet demonstrate a usable feature in an independent host. |

## Proposed ownership boundary

| Owner | Responsibilities |
| --- | --- |
| Decisions developer / feature package | Domain rules, commands and queries, read models, authoritative validation and authorization requirements, feature service composition, resource requirements, interaction descriptions, package compatibility declarations and tests. |
| Runtime / shared platform | Institutional composition and resolution, operation and interaction contracts, lifecycle mechanisms, owner-scoped resource binding, generic rendering primitives and compatibility validation. Exact project placement remains to be agreed. |
| Integration host | Application shell, navigation composition, authenticated request context, generic interaction rendering, package discovery/mounting and presentation of failures. |
| Deployment owner | Package versions, enabled offices, owner identities, identity mappings, infrastructure providers, credentials and resource bindings. |

Key rules:

1. The host must not need to register Decisions application services individually or know the accept/reject workflow.
2. The package must not construct a replacement Campus or assume a particular database or identity vendor.
3. The package declares authority requirements and enforces them through trusted runtime context. Hiding an action in the UI is not authorization.
4. Shared infrastructure can implement an office-owned logical workspace. Ownership does not require a dedicated server or database.
5. Feature-specific labels, fields and interaction meaning belong with the package; reusable layout and accessibility behavior belong with the shared presentation layer.

Moving bespoke Razor pages into a package could remove host edits, but would leave their maintenance with the institution developer. The main path should therefore support common interactions through structured descriptions. Specialized views can be an explicit extension when the shared presentation vocabulary is insufficient.

## Milestone 1 — Make the package operational

Expose one real Decisions operation through the organizational boundary, backed by the existing application service.

Start with preparing and committing a proposal decision. Define the trusted caller and owner context, inputs, read model, validation outcomes, conflict outcomes and retry semantics. Decide whether Recorder is the appropriate surface or delegates to another public contract.

Move the composition needed for this slice behind a package-owned entry point. Declare and validate required parent capabilities with useful errors.

Acceptance:

- A small host can mount the package and execute the operation without registering Decisions internals.
- Existing authorization, immutable-record, concurrency and retry guarantees remain intact.
- Missing capabilities produce an actionable diagnostic.
- Domain behavior remains testable without the website.

## Milestone 2 — Present one interaction through a generic host

Use proposal review as the first complete example.

The package describes the proposal read model, accept/reject actions, note field, required versus optional input, confirmation step and structured outcomes. The host renders these using shared components and invokes the public operation.

Acceptance:

- A maintainer can review and decide a proposal through the generic host.
- Unauthorized callers cannot commit the action, including through direct invocation.
- Validation and stale-proposal conflicts are presented without host-specific Decisions logic.
- A supported change to the review interaction is delivered through the package without editing host source.

This milestone is the primary proof of the ownership boundary. Use it to discover the necessary contracts before generalizing to every workflow.

## Milestone 3 — Connect definitions to execution

Choose one authoritative definition path: load and validate YAML into the runtime template, or generate the descriptive artifact from an executable definition. Avoid maintaining two independent descriptions.

Connect capability and workflow identifiers to executable operations and interaction descriptions. Validate references and supported versions. Distinguish descriptive policy text from rules actually enforced by code.

Acceptance:

- The installed template exposes the intended capabilities, resources and interactions.
- Unknown operation references and missing required bindings fail validation.
- A descriptive policy cannot be mistaken for an implemented enforcement rule.
- Definition changes have a verified effect or a clear validation failure.

## Milestone 4 — Make instance ownership explicit

Supply owner and office instance identity during mounting. Bind resource handles from that context rather than relying on fixed host-wide names.

Separate package/type identity from mounted instance identity. Resolve how the current fixed `decisions` registration key should support multiple offices under one owner. Treat resource-name changes as potential data migrations.

Acceptance:

- Two offices can run in one test host with distinct logical workspaces.
- Drafts, operations and maintenance remain within the correct office.
- Existing records remain accessible through an explicit migration or preserved binding.
- Deployments can choose providers without changing feature code.

## Milestone 5 — Prove package delivery and broaden coverage

Exercise actual package loading in a host that has no direct Decisions implementation reference. Define compatibility checks for runtime contracts, definitions and presentation vocabulary.

Extend the proven pattern to drafting, discovery, supersession and administration incrementally. Retain working pages during migration until the corresponding behavior is verified.

Acceptance:

- An independent host discovers, mounts and presents the packaged review interaction.
- A compatible package upgrade updates it without host source changes.
- An incompatible package yields a clear diagnostic.
- Required-feature failures are visible; a skipped plugin must not silently appear to be a complete deployment.
- Further workflows reuse the contracts without adding Decisions-specific branches to the host.

## Decisions for Brian and Claude

1. Should Recorder expose commands and queries directly, or delegate to an operation surface?
2. Which interaction vocabulary is sufficient for the first review flow, and where should the shared renderer live?
3. Is YAML intended to be authoritative executable input, generated documentation, or a provisioner input with a defined translation contract?
4. How are mounted office identities and owned resource handles represented?
5. What is the smallest supported escape hatch for specialized presentation?
6. Which registrations are package responsibilities versus deployment bindings, especially membership integration and background work?

## Recommended next step

Agree on the ownership table, then implement Milestones 1 and 2 as one narrow demonstration. Defer a universal workflow engine, distributed deployment and live plugin replacement until a concrete requirement calls for them.

The decisive review question is:

> Can the Decisions developer change the proposal-review interaction and deliver it to a generic host without changing that host's source or maintaining a bespoke review page?

## Source references

- [Architecture overview](overview.md)
- [Product stories](../stories/overview.md)
- [Organization factory](../../src/AdrCampus.Plugin/DecisionsOrganizationFactory.cs)
- [Recorder](../../src/AdrCampus.Plugin/AdrCampusRecorder.cs)
- [Host composition](../../src/AdrCampus.Web/Hosting/ForgeCampusExtensions.cs)
- [Host startup](../../src/AdrCampus.Web/Program.cs)
- [Institution definition](../../institution/decisions-institution.yaml)
- [Deployment bindings](../../institution/decisions-institution.bindings.yaml)
- [Current review page](../../src/AdrCampus.Web/Components/Pages/DecideProposal.razor)
- [Factory tests](../../tests/AdrCampus.Plugin.Tests/DecisionsOrganizationFactoryTests.cs)
