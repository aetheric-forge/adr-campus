# Decisions proposal-review boundary

Status: first implementation slice for architecture review

## Purpose

Proposal review now has an operational surface on the mounted Decisions Recorder. A host can prepare
and commit a decision through the package without constructing the ADR application service or
registering its repositories individually. The existing review page exercises this boundary.

This implements the proposal-review slice of the ownership roadmap. It does not implement generic UI
rendering, executable YAML definitions, dynamic package discovery, or multiple office instances.

## Ownership

| Concern | Owner in this implementation |
| --- | --- |
| Decision rules, validation, current membership checks, conflict and retry behavior | Existing `ProposalApplicationService` and repositories, composed by the Decisions package |
| Office organization identity | Trusted deployment binding passed to `AddDecisionsOffice` |
| Authenticated caller | Host adapter implementing `IProposalReviewCaller` |
| Membership source | Host adapter implementing `IMemberAuthority`; Decisions checks it on each operation |
| Shared storage capabilities | Owning institution supplies `ILibrary` and `IWorkbench` |
| ADR repository composition and review session | Decisions factory and Recorder |
| Layout and current review page | Website; the page now calls `IProposalReview` |

`IAdrCampusRecorder` extends the runtime's `IRecorder` within ADR Campus. No runtime contract change is
needed. `OpenReview` creates a session from trusted host identity, membership, and clock adapters. The
session binds the deployment's organization identity; its command contains neither an actor ID nor an
organization ID. The caller adapter is consulted at each invocation, and the application service
rechecks maintainer authority at commit even if preparation succeeded earlier.

These are in-process host contracts, not authentication of arbitrary callers. A server endpoint must
resolve the adapters from its trusted context, never deserialize them or accept their identity from a
request payload. The Blazor adapter reads its circuit's authenticated principal and returns no caller
when authentication or the subject claim is missing.

## Host integration

Register the host's identity and membership adapters and call the package entry point:

```csharp
services.AddScoped<IProposalReviewCaller, AuthenticatedProposalReviewCaller>();
services.AddScoped<IMemberAuthority, KeycloakMemberAuthority>();
services.AddDecisionsOffice(
    new OrganizationId(configuration["Organization:Id"]!),
    sp => sp.GetRequiredService<ICampus>());
```

The owner still mounts the factory during institutional composition:

```csharp
var factory = new DecisionsOrganizationFactory();
owner.RegisterOrganization(factory.OrganizationId, factory.Create(owner, services));
```

The factory validates the deployment binding and the parent Library and Workbench capabilities. It
builds the repositories once. Scoped `IProposalReview` sessions and the legacy application services
share those same repository instances, avoiding a second storage path during migration.

Resolve `IProposalReview` in the request/circuit scope and use `PrepareAsync` and `DecideAsync`.
A host working directly with the mounted organization can obtain `IAdrCampusRecorder` from its
Recorder and call `OpenReview` with trusted scoped adapters. Do not retain a review session on the
singleton organization.

`ReviewDecision` carries proposal identity, the expected proposal timestamp, outcome, note and operation
ID. Reuse the same operation ID and unchanged payload when retrying the same command. Results preserve
the existing structured validation, authorization, conflict and idempotency outcomes.

## Boundaries deliberately left for later milestones

- One `AddDecisionsOffice` registration is supported per service provider. A second registration fails
  explicitly. The existing `decisions` key and storage names are preserved; there is no data migration.
- The deployed organization ID is explicitly bound by the host. Multi-owner binding and dedicated
  logical workspace provisioning require a later design; the current Workbench remains parent-supplied.
- Only Library and Workbench are required by this operational slice. Archive, Post Office and identity
  configuration for the rest of ADR Campus remain in the existing host composition.
- Membership and maintenance composition beyond proposal review have not moved into the package.
- The YAML files remain descriptive and are not consumed by the factory. Their broader ownership
  intent is not proof of executable provisioning.
- Existing repository concurrency guarantees are unchanged: Library writes are serialized within one
  process. This work does not establish distributed transaction or multi-process write guarantees.
- Existing pages other than proposal review retain their application-service calls.
- A host still references the package at build time. Assembly discovery and compatibility checks are
  separate work, as is a renderer driven by interaction descriptions.

## Verification

The plugin tests use a small owning Institution with real in-memory-backed Library and Workbench
implementations. They do not reference the web project or require a Campus. They cover mounting,
missing capability diagnostics, prepare/commit behavior, missing or unauthorized callers, authority
revocation, scoped caller isolation, office binding, rejection validation, retry identity, stale and
competing decisions, and cancellation.

Run the plugin tests, the existing application/provider tests, and the website build when changing
this boundary. Browser sign-in and review against the deployment's real infrastructure remain a
separate live verification step.

Validation on 2026-09-16: all 206 solution tests passed (including 15 plugin tests), and the website
build succeeded. Restore/build reported advisories in the existing SharpCompress and Snappier dependency
set and warnings in unchanged runtime code. Live browser verification was not performed.

## Review questions

1. Is an ADR-specific Recorder extension the right first operational contract?
2. Is the explicit scoped session a suitable bridge to future generic interaction rendering?
3. Which remaining host bindings should move into the package next?
4. Should a future mount contract carry organization and office-instance identities directly?
