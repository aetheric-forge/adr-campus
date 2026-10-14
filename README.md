# ADR Campus

ADR Campus is a small application for maintaining one organization's architectural decision record. It gives architectural decisions a durable home and a small, understandable process through which they can be proposed, reviewed, accepted, and eventually superseded.

The [product goal, roles, and lifecycle](docs/stories/overview.md) are authoritative for what the application does. The [delivery roadmap](docs/roadmap.md) tracks how it was built, release by release. The [architecture overview](docs/architecture/overview.md) explains how the application is composed.

## Running it locally

ADR Campus requires:

- a Keycloak realm with a configured client, and member/maintainer groups;
- a MongoDB instance for durable Archive and Library storage; and
- a Redis instance for durable Workbench storage (optional — the app falls back to in-memory staging when `ConnectionStrings:Redis` is unset, which does not survive a restart).

Configure these with [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) against `src/AdrCampus.Web`:

```sh
cd src/AdrCampus.Web
dotnet user-secrets set "Keycloak:Authority" "https://<host>"
dotnet user-secrets set "Keycloak:Realm" "<realm>"
dotnet user-secrets set "Keycloak:ClientId" "<client-id>"
dotnet user-secrets set "Keycloak:ClientSecret" "<client-secret>"
# Keycloak:AdminApiBaseAddress is optional — it defaults to "<host>/admin/" and only needs
# overriding if the Admin REST API is reachable at a different host/path than the realm itself.
dotnet user-secrets set "Organization:MemberGroupId" "<group-id-or-exact-name>"
dotnet user-secrets set "Organization:MaintainerGroupId" "<group-id-or-exact-name>"
dotnet user-secrets set "MongoDb:Host" "<host>"
dotnet user-secrets set "MongoDb:Port" "<port>"
dotnet user-secrets set "MongoDb:Username" "<username>"
dotnet user-secrets set "MongoDb:Password" "<password>"
dotnet user-secrets set "MongoDb:DatabaseName" "<database-name>"
dotnet user-secrets set "MongoDb:AuthenticationDatabase" "<authentication-database>"
dotnet user-secrets set "ConnectionStrings:Redis" "<host>:<port>[,password=...]"
```

`Organization:MemberGroupId` and `Organization:MaintainerGroupId` accept either a Keycloak group ID or an exact, unique group display name.

Then, from the repository root:

```sh
dotnet run --project src/AdrCampus.Web
```

The organization bootstraps automatically on startup from the configured groups. At least one enabled identity must belong to both the member and maintainer groups, or bootstrap fails and the application reports the condition rather than granting default authority.

## Building and testing

Initialize the pinned runtime and shared-contract submodules before building:

```sh
git submodule update --init --recursive
```

```sh
dotnet build
dotnet test
```

## Repository layout

- `src/AdrCampus.Core` — the ADR domain model and provider-facing contracts.
- `src/AdrCampus.Application` — application services: commands, queries, and authorization.
- `src/AdrCampus.Providers.*` — storage provider implementations (Workbench-backed and in-memory).
- `src/AdrCampus.Web` — the Blazor web host: composition root, authentication, and UI.
- `contracts/` — shared Aetheric contracts, including the interaction vocabulary used by proposal review.
- `src/AdrCampus.Interactions.Blazor` — reusable rendering for package-described interactions.
- `runtime/` — the Aetheric Forge Runtime this application is built on.
- `docs/` — product stories, architecture, and the delivery roadmap.
- `tests/` — automated tests for the above, mirroring the `src/` layout.

## Institution definition

The Decisions package owns its executable definition and generates the YAML artifact from it. See
[definition ownership and validation](docs/architecture/executable-decisions-definition.md) for regeneration
commands, deployment boundaries, and the pending live browser acceptance check.

A separate [provisioning export](docs/architecture/decisions-provisioning-export.md) derives resource requirements and example bindings from the same package definition and checks them against the runtime reader/planner.

[Existing owner capability bindings](docs/architecture/decisions-owner-capabilities.md) define the next deployment integration boundary for inherited Library and Workbench.

## Aetheric web shell module

`src/AdrCampus.Web.Module` adapts the current Decisions organization plugin for aetheric-web. It exposes the existing workflow pages at `/decisions`, uses namespaced CSS, and delegates organization creation to `DecisionsOrganizationFactory`. Its UI namespace is `AdrCampus.Shell`, separate from the standalone host. The existing standalone routes remain available.

The adapter requires an existing owning institution with Library and Workbench. The shell mounts it under its Architecture faculty, whose ancestor campus supplies these capabilities. Archive administration and Post Office maintenance use the owner's facilities. Decisions does not create a sovereign campus or provision a substitute owner.

Deployment settings retain `Organization:*`, scoped `Decisions:MongoDb`, `Decisions:S3`, and `Registry:Keycloak` credentials. Parent Library and Archive adapters use their own credentials; Decisions contributes only its existing `adr-campus` schemes under the owner's keyed provider registries. Existing `adr-campus` knowledge/Archive keys, `adr-campus-workbench` staging and organization IDs remain unchanged.

Membership/maintainer checks, authenticated proposal-review callers and existing office operations remain package-owned. Background services can be disabled for isolated validation with `Decisions:BackgroundServicesEnabled=false`; production defaults to the existing bootstrap, synchronization, and maintenance services.

Validate with `dotnet test AdrCampus.slnx --configuration Release` and publish `src/AdrCampus.Web` to produce both the standalone output and reusable module assets.
