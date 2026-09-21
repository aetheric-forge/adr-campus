# Decisions deployment owner and inherited capabilities

`DecisionsDeploymentOwner` binds the one supported Decisions office to an explicit existing owner: HTTPS repository and pinned revision, Library-owning institution ID, and the Workbench registration's environment, owning institution and resource ID. Resource owners may be ancestors of the immediate owner.

`ToParentIdentity()` produces an independently declared JSON shape matching runtime's single-institution v2 `Parent` field. `ExportBindings(environment)` validates that owner configuration and emits the Decisions provisioning bindings using the same `owner.library` and `owner.workbench` sources. No credentials are embedded in either artifact. The later designer/submission adapter must retain this owner snapshot alongside the approved definition and resolve `mongo`/`redis` verification credentials server-side.

The initial integration fixes the Workbench stage to **`adr-campus-workbench`** and keeps the Library scheme **`adr-campus`**, matching the existing host/repositories. This PR changes no storage keys or provider names and migrates no data. It does not enable multiple office instances. Selecting a different physical Mongo database or Redis endpoint/database can still select different data: the deployment operator must preserve the existing infrastructure configuration as well as the logical names.

Runtime's [Workbench parent resolver](../../runtime/docs/provisioning/workbench-parent-capability.md) verifies the existing nonexpiring Redis ownership marker without creating, claiming, probing or modifying a workspace. Library retains runtime's scoped-user verification. These verify provisioning prerequisites, not application mounting, authorization, or app-level read/write readiness.

A legacy ADR workspace may contain data without a provisioner registration. Such a workspace deliberately fails verification; this PR cannot establish historical ownership from the presence of draft keys. An explicit, backed-up adoption/migration procedure remains necessary for those deployments. Do not rename the stage or create an empty replacement merely to make deployment succeed.

The new owner configuration is not yet consumed by the live web host or a submit endpoint. That integration belongs to the designer/submission and package-mounting slices. Current package registration and the existing app's behavior remain unchanged.

Tests round-trip the owner into the actual runtime Parent contract, check the exact Workbench location and fixed names, reject unpinned/incomplete owners, and plan the exported bindings against the real reader/planner. Runtime tests exercise real Redis registrations and the existing Mongo Library resolver using isolated fixtures.

Validation: all 248 ADR Campus tests passed. The runtime companion's 25 focused Workbench, Library and composite-resolver tests passed with no skips against isolated Redis and MongoDB. Redis tests used database 2 and verified authentication failure, ownership mismatch, expiring/malformed registration, unregistered legacy data, cancellation, and unchanged draft data. No application deployment or legacy-data migration was attempted.

Runtime is pinned to `dbbaebd065dc9a4904ad08e7acd40d813791e51d`, based on current main `1a1d74e` (durable RabbitMQ transport) plus the companion Workbench verifier. Merge the runtime companion before this pin update.
