# Production office bindings

Use `institution/provisioning/decisions.production.bindings.yaml` with the canonical
ADR repository definition and a catalog-backed existing owner. These bindings set
the deployment environment to `production`; they create no owned resources. The
worker verifies Library and Workbench through the selected owner's ancestry.
Membership IDs are supplied separately in `DecisionsConsumer` on the request.

The web adapter accepts `Decisions:Workbench:Stage` for the parent's actual
provisioned stage. The host must register its staging provider with that same
stage and use the parent's Redis endpoint/database. Draft reads, writes, locks,
recovery, and operation history then all use that stage. The default remains
`adr-campus-workbench` for existing installations; changing an existing office's
stage does not move its data.

For the new Administration office, the existing campus stage is
`orchard-sky-workbench`. Select it explicitly in the host configuration. Do not
adopt or move the older Decisions deployment's private workspace. The organization
ID distinguishes the new office's drafts and records within the parent facilities.
