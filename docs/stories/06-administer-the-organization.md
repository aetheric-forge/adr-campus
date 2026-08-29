# Administer the Organization

## User Story

As an organization member, I want ADR Campus to reflect the membership and authority established in our SSO control plane so that the correct people can participate without creating a second user-management system.

As an organization maintainer, I want to administer the ADR-specific consequences of membership changes so that drafts and historical attribution remain governed when people join or leave.

## Value

Identity, user lifecycle, group membership, and role assignment belong to the organization's SSO control plane. ADR Campus consumes that authority rather than duplicating it. This gives members one organizational identity, gives access changes a single source of truth, and keeps application administration focused on the ADR record itself.

## Actors and Authorities

### SSO control plane

The SSO control plane is the sole authority for:

- user identity and authentication;
- creating, disabling, and deleting user accounts;
- organization membership;
- maintainer assignment; and
- auditing changes to users and SSO groups.

### Member

An authenticated person in the configured SSO member group is an active ADR Campus member. A member can see the current organization roster and exercise the member capabilities defined by the other journeys.

### Maintainer

An authenticated active member in the configured SSO maintainer group is an ADR Campus maintainer. A maintainer can exercise the decision and ADR-recovery capabilities defined by the other journeys and may rename the organization.

Membership in the maintainer group does not grant access unless the person is also an active member. The maintainer group is a specialization of the member group, not an independent source of application access.

### Deployment operator

The deployment operator configures the SSO authority, member group, maintainer group, and initial organization. This bootstrap authority is external to ordinary ADR Campus membership.

## Control-Plane Boundary

ADR Campus does not provide controls to:

- create or delete a user;
- add or remove an organization member;
- activate or deactivate a user;
- grant or revoke maintainer authority;
- change a person's SSO profile; or
- modify SSO groups.

Those actions occur only in the SSO control plane. ADR Campus may explain where membership is managed or link an authorized operator to that control plane, but it must not proxy or reproduce those mutations.

ADR Campus may retain stable references and historical display information needed to attribute actions after a person leaves. Those records are not user accounts and do not grant access.

## Organization Bootstrap

ADR Campus supports one basic organization in the first release.

The deployment configuration identifies:

- the SSO authority and tenant or realm;
- exactly one member group;
- exactly one maintainer group;
- the organization's initial display name; and
- a stable application identity for the organization.

Before the organization becomes available, ADR Campus verifies that the configured groups can be resolved and that at least one enabled identity belongs to both the member and maintainer groups. Invalid bootstrap configuration does not create a partially usable organization.

The first person to visit ADR Campus receives no special authority merely because the application is uninitialized.

## Organization Name

The display name:

- is normalized by removing leading and trailing whitespace;
- contains between 3 and 100 characters;
- contains at least one letter or number;
- may contain ordinary text; and
- may not contain control characters.

An active maintainer may rename the organization within ADR Campus. Renaming does not change its stable identity, SSO group mappings, membership, ADR ownership, or decision history.

ADR Campus records the previous name, new name, responsible maintainer, and change time in its application-administration history.

## Current Member Roster

Every active organization member can view a current roster derived from the configured SSO groups.

The roster shows:

- the person's current SSO display name;
- whether the person is a Member or Maintainer; and
- only additional identity information that the SSO integration explicitly designates as safe for organization members.

The roster contains enabled identities in the member group. A person in the maintainer group is labeled Maintainer only when they are also in the member group.

The roster does not expose private draft counts, recovery details, administrative history, tokens, raw claims, group identifiers, or SSO-management controls. It does not imply that a former member's historical ADR attribution has been removed.

ADR Campus queries the SSO groups through an authorized server-side integration. It does not send privileged SSO credentials or an unrestricted directory to the member's client.

## Effective Membership and Authority

SSO group state is authoritative whenever ADR Campus authorizes an operation.

- An enabled identity in the member group is an active Member.
- An active Member also in the maintainer group is a Maintainer.
- An identity absent from the member group or disabled in SSO has no organization access.
- An identity removed only from the maintainer group retains Member access but loses maintainer authority.
- Adding a person to the maintainer group without adding them to the member group grants no ADR Campus access.

ADR Campus must not continue granting authority merely because a previous page, session, token, cache entry, or local projection described older group membership. The architecture may use bounded caching, but a protected mutation must be authorized against sufficiently current SSO-derived authority and must fail closed when authority cannot be established.

## Observed Membership Changes

ADR Campus maintains only the local projection required to apply ADR lifecycle rules and preserve attribution. When it observes an SSO transition, it records:

- the affected stable SSO identity reference;
- the previous and resulting effective ADR Campus membership or role state;
- the SSO authority as the source;
- the time ADR Campus observed the transition; and
- any ADR-specific recovery effects initiated by the transition.

The SSO control plane remains the authoritative audit source for who changed a user or group and when that control-plane action occurred. ADR Campus must not invent or infer a human administrator for an externally observed change.

### Member added

When an enabled identity first appears in the member group, ADR Campus recognizes one active membership projection and grants Member capabilities. If the person also belongs to the maintainer group, maintainer authority becomes effective.

### Maintainer authority changed

When an active member enters or leaves the maintainer group, ADR Campus grants or revokes maintainer authority for subsequent protected operations. Existing ADR authorship, proposals, decisions, and attribution remain unchanged.

### Member removed or disabled

When an identity leaves the member group or becomes disabled:

- ADR Campus denies subsequent member and maintainer access;
- shared proposals and decided ADRs retain the person's historical attribution; and
- each private draft still authored by that person enters the 30-day maintainer recovery window defined by the drafting journey.

The recovery window begins when ADR Campus records the effective membership loss. ADR Campus does not backdate the deadline to an unverified external event time.

### Member returns

If the same stable SSO identity becomes an enabled member again, ADR Campus recognizes the same historical person rather than creating a duplicate identity.

If they return during a draft's recovery window before reassignment, they resume authorship and ordinary access to that draft and its recovery deadline is cancelled. Their previous maintainer authority returns only if they currently belong to the maintainer group.

Reactivation does not undo a completed draft reassignment and does not restore an expired draft.

## Final-Maintainer Safety

ADR Campus cannot mutate SSO groups and therefore cannot prevent a control-plane operator from removing the final maintainer.

The SSO control plane should enforce that at least one enabled identity belongs to both configured groups. ADR Campus also detects the condition when reading current authority. If no active maintainer remains:

- ordinary active members retain read and member capabilities;
- no person may perform maintainer-only actions;
- ADR Campus prominently reports the configuration problem to appropriately authorized operators; and
- recovery requires correcting the SSO groups, not bypassing authorization inside ADR Campus.

ADR Campus never promotes a member automatically to repair the condition.

## Application-Administration History

ADR Campus records application-owned administrative events:

- organization bootstrap;
- organization rename;
- observed effective membership and maintainer transitions; and
- draft recovery, reassignment, reactivation, and expiration effects.

This history is read-only and visible to active maintainers. It complements but does not replace the SSO control plane's user and group audit log.

Ordinary members may see the current roster and roles but not application-administration history or private draft-recovery information.

## Alternate and Failure Scenarios

### SSO group information is unavailable

ADR Campus does not use stale or absent authority to permit a protected mutation. It explains that current authorization cannot be established and fails closed. Read behavior may use a bounded, clearly identified last-known roster only if the architecture can do so without granting access or exposing unauthorized data.

### The member roster cannot be retrieved

ADR Campus explains that the current roster is unavailable and does not present a partial or stale roster as current. Failure to load the roster does not change membership or roles.

### Group configuration is invalid

If a configured group is missing, ambiguous, or belongs to the wrong SSO authority, ADR Campus refuses to infer a replacement group or broaden access. It reports the configuration problem to an appropriately authorized operator.

### The final maintainer disappears

ADR Campus permits no maintainer action based on former authority, reports the condition, and waits for the SSO control plane to restore at least one active maintainer.

### A stale session attempts an action

If a person's effective SSO-derived authority no longer permits an operation, ADR Campus refuses it even if the page was opened or the session began while the person had authority.

### A group transition is observed more than once

ADR Campus does not duplicate the local membership projection, recovery deadline, or application-history event when it processes the same effective SSO state repeatedly.

### Synchronization partially fails

ADR Campus does not report a completed local transition or start contradictory recovery effects. It preserves the last coherent projection and fails closed for affected protected mutations until current authority can be established.

## Acceptance Criteria

### Bootstrap from configured SSO groups

Given valid deployment configuration, resolvable member and maintainer groups, and at least one enabled identity in both groups,
when ADR Campus initializes the organization,
then it creates one stable organization projection, maps the configured groups, recognizes effective members and maintainers, and records bootstrap without creating or changing any SSO user or group.

### Reject invalid bootstrap configuration

Given a missing or ambiguous configured group or no enabled identity belonging to both groups,
when ADR Campus attempts initialization,
then it does not expose a partially authorized organization and reports the configuration problem without granting first-visitor authority.

### Never mutate SSO users or groups

Given any active member or maintainer,
when they use ADR Campus administration,
then ADR Campus offers no operation that creates, deletes, enables, disables, adds, removes, or changes the role of an SSO identity or group membership.

### Show the current member roster

Given an authenticated active member and available SSO group information,
when the member opens the roster,
then ADR Campus shows each enabled identity in the configured member group exactly once, labels those also in the maintainer group as Maintainers, and exposes no restricted SSO or draft-recovery information.

### Exclude non-members from the roster

Given an identity absent from the member group or disabled in SSO,
when ADR Campus builds the current roster,
then it does not present that identity as a current member even when historical ADRs still attribute actions to that person.

### Restrict roster access

Given a person who is not an active organization member,
when they attempt to retrieve the roster,
then ADR Campus refuses access and reveals no roster content or group metadata.

### Recognize member authority

Given an enabled identity in the configured member group,
when ADR Campus establishes current authority,
then it permits member capabilities and denies maintainer-only capabilities unless that identity is also in the maintainer group.

### Recognize maintainer authority

Given an enabled identity in both configured groups,
when ADR Campus establishes current authority,
then it permits member and maintainer capabilities for subsequent authorized operations.

### Require membership for maintainer authority

Given an enabled identity in the maintainer group but not the member group,
when that person attempts organization access,
then ADR Campus grants neither member nor maintainer capabilities.

### Apply SSO removal

Given an identity previously recognized as an active member,
when ADR Campus observes that the identity left the member group or became disabled,
then it denies subsequent organization access, preserves shared-record attribution, records the observed transition, and starts one 30-day recovery window for each private draft still authored by that person.

### Apply maintainer revocation

Given an active member previously recognized as a maintainer,
when ADR Campus establishes that the identity is no longer in the maintainer group,
then it preserves Member access, refuses subsequent maintainer operations, and records the observed effective role transition once.

### Recheck authority for protected mutations

Given a person whose page, session, token, or cached projection reflects former authority,
when they attempt a protected mutation after the SSO-derived authority has changed,
then ADR Campus uses sufficiently current authority, refuses the unauthorized operation, and persists no domain change.

### Fail closed when authority is unavailable

Given that sufficiently current SSO-derived authority cannot be established,
when a person attempts a protected mutation,
then ADR Campus refuses the operation and does not fall back to unbounded stale authority.

### Restore a returning member

Given a previously removed person's same stable SSO identity newly enabled in the member group,
when ADR Campus observes the transition,
then it restores Member access without duplicating identity or attribution and grants Maintainer capabilities only when the identity currently belongs to the maintainer group.

### Resume an unreassigned draft

Given a returning member with an unreassigned draft still inside its recovery window,
when ADR Campus restores their membership,
then it returns ordinary draft access to that author and cancels the draft's recovery deadline.

### Preserve reassignment and expiration

Given a returning member whose former draft was reassigned or expired during their absence,
when ADR Campus restores their membership,
then it does not restore authorship of the reassigned draft and does not restore the expired draft.

### Detect loss of the final maintainer

Given no enabled identity currently belonging to both configured groups,
when ADR Campus evaluates maintainer authority,
then it grants no maintainer capability, reports the configuration problem, and does not promote another member automatically.

### Rename without changing SSO authority

Given an active maintainer and a valid new organization display name,
when the maintainer confirms the rename,
then ADR Campus preserves its stable identity and SSO group mappings and records the previous name, new name, maintainer, and change time.

### Validate and normalize the organization name

Given an organization rename,
when the normalized name contains fewer than 3 or more than 100 characters, contains no letter or number, or contains a control character,
then ADR Campus persists no invalid name and explains the applicable constraint,
and when an otherwise valid name contains surrounding whitespace,
then ADR Campus removes that whitespace before persisting it.

### Process observed state idempotently

Given an effective SSO group state ADR Campus has already processed,
when synchronization presents the same state again,
then ADR Campus creates no duplicate membership projection, transition history, or draft-recovery effect.

## Out of Scope

This journey does not define:

- creation, deletion, activation, or modification of SSO users;
- mutation of SSO groups or group membership;
- invitations or identity enrollment;
- multiple organizations;
- departments, teams, or nested organization structure;
- custom application roles or permission policies;
- an ADR Campus user-management control plane;
- bulk identity administration;
- organization deletion;
- restoration of expired drafts;
- editing or deleting application-administration history; or
- replacement of the SSO control plane's audit log.

## Architectural Implications

The architecture must support:

- one configured SSO authority and stable mappings for member and maintainer groups;
- server-side group queries using least-privileged credentials;
- stable external identity references and historical attribution without local user accounts;
- current SSO-derived authorization for protected operations with fail-closed behavior;
- a bounded and explicit authority-cache policy;
- a local projection of effective membership changes only where ADR lifecycle behavior requires it;
- idempotent observation of SSO group transitions;
- coordinated membership loss, draft recovery, return, reassignment, and expiration;
- a member-visible current roster without privileged SSO data exposure;
- detection, but not automatic repair, of a missing active maintainer; and
- a clear separation between SSO audit history and ADR Campus application history.

These implications identify required system behavior. They do not prescribe SSO vendor, authentication protocol, directory API, synchronization schedule, cache duration, database, scheduler, or interface mechanisms.
