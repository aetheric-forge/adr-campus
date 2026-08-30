# Supersede a Decision

## User Story

As an organization member, I want to propose a new ADR that replaces an accepted decision so that the organization can change its architecture without erasing why the earlier decision was made.

## Value

Architectural decisions change as constraints, knowledge, and goals evolve. Supersession preserves the earlier decision as valid history, identifies the decision now in effect, and connects both records so that a future reader can follow the evolution of the architecture.

## Actors

The initiating actor is an authenticated active organization member who authors the replacement ADR.

The deciding actor is an authenticated active organization maintainer who accepts or rejects the proposed replacement under the rules of the review-and-decide journey.

The author and deciding maintainer may be the same person in the first release.

## Preconditions

To begin a replacement:

- the organization exists;
- the initiating actor is authenticated and is an active member;
- the earlier ADR belongs to the organization; and
- the earlier ADR has status `Accepted`.

To complete supersession:

- the replacement ADR has status `Proposed`;
- it identifies exactly one earlier ADR that it intends to supersede;
- the earlier ADR still has status `Accepted`; and
- the deciding actor is currently an active maintainer.

## Primary Scenario

1. The member opens an ADR that is currently `Accepted`.
2. The member chooses to propose a replacement.
3. ADR Campus creates a new private draft, identifies the member as its author, and records the accepted ADR as its intended supersession target.
4. ADR Campus gives the new draft its own stable identifier and leaves the accepted ADR unchanged.
5. The author writes the replacement's title, context, decision, and consequences using the drafting journey.
6. The author previews and proposes the replacement using the proposal journey.
7. ADR Campus makes the intended supersession relationship visible on the proposed replacement while the earlier ADR remains `Accepted` and in effect.
8. A maintainer reviews the exact proposed replacement and its target using the review-and-decide journey.
9. The maintainer selects acceptance and confirms.
10. As one operation, ADR Campus changes the replacement from `Proposed` to `Accepted`, changes the earlier ADR from `Accepted` to `Superseded`, and records the supersession relationship in both directions.
11. ADR Campus uses the replacement's decision time as the supersession time and records the deciding maintainer under the ordinary acceptance rules.
12. ADR Campus presents the replacement as the current decision and the earlier ADR as historical, with a navigable relationship between them.

## Intended and Completed Supersession

A replacement draft or proposal identifies an **intended supersession target**. This expresses what the author is asking the organization to replace; it does not change the authority or status of the target.

Completed supersession exists only when the replacement is accepted. At that moment:

- the replacement records that it supersedes the earlier ADR;
- the earlier ADR records that it is superseded by the replacement; and
- both records share the same supersession time.

A rejected replacement retains its intended target as historical context, but no completed supersession relationship is created and the target remains in effect.

## Replacement Draft

The replacement is a new ADR, not a new revision of the accepted ADR. It has its own:

- stable identifier;
- title, context, decision, and consequences;
- author and creation time; and
- lifecycle and decision metadata.

The author may change or remove the intended supersession target while the replacement remains a private draft. Any selected target must be an `Accepted` ADR in the same organization at the time it is saved.

Removing the target turns the record into an ordinary draft. A draft may identify at most one intended supersession target in the first release.

When the draft is proposed, its intended target becomes read-only along with the proposed content.

## Effect of Each Outcome

### While Draft or Proposed

The target remains `Accepted` and continues to represent the current organizational decision. A draft is private. A proposed replacement is shared and clearly states that it intends to supersede the target if accepted.

### Accepted

The replacement becomes `Accepted` and part of the current architectural record. The target becomes `Superseded` and moves to the historical record. Both ADRs remain readable and link to one another.

### Rejected

The replacement becomes `Rejected` under the ordinary decision rules. The target remains `Accepted` and in effect. The rejected record shows that it was proposed as a replacement, but the target does not claim to have been superseded.

## Supersession Chains

An accepted replacement may itself be superseded later. ADR Campus preserves every direct relationship so a member can navigate the chain in either direction.

For example, when ADR B supersedes ADR A and ADR C later supersedes ADR B:

- ADR A remains `Superseded` by ADR B;
- ADR B remains recorded as superseding ADR A and becomes `Superseded` by ADR C; and
- ADR C is `Accepted` and records that it supersedes ADR B.

Only the directly replaced ADR is the target of a supersession. ADR Campus does not rewrite earlier links or imply that ADR C directly superseded ADR A.

## Authorization and Lifecycle Rules

- Any active member may create a replacement draft from an accepted ADR.
- Only the active author may edit the replacement draft or change its intended target.
- A supersession target must belong to the same organization as the replacement.
- A supersession target must have `Accepted` status whenever the target is selected, the replacement is proposed, and the replacement is accepted.
- A replacement identifies at most one intended target.
- A proposed replacement's target is immutable.
- Only an active maintainer may accept or reject a proposed replacement.
- Acceptance completes the supersession; proposal alone does not.
- Accepting a replacement changes exactly one `Accepted` target to `Superseded`.
- Rejection does not change the target.
- Completed supersession preserves both ADRs and their existing content and metadata.
- A supersession relationship cannot cross organization boundaries.
- A supersession relationship cannot point from an ADR to itself.
- Completed supersession must not create a cycle.

## Concurrent Replacement Proposals

More than one member may independently propose a replacement for the same accepted ADR. The proposals remain valid for review while the target is still `Accepted`.

The first replacement successfully accepted completes the supersession. Any other proposal targeting the earlier ADR can no longer be accepted because its target is now `Superseded`. It remains `Proposed` until a maintainer rejects it with a reason. Reconsideration requires a new draft targeting the decision that is currently `Accepted`.

This rule avoids silently rejecting a proposal or retargeting immutable proposed content on the organization's behalf.

## Alternate and Failure Scenarios

### The selected target is not accepted

If the member attempts to select an ADR that is `Proposed`, `Rejected`, or `Superseded`, ADR Campus explains that only a currently accepted decision can be targeted and does not save the invalid relationship.

### The target belongs to another organization

ADR Campus refuses the relationship and reveals no target content the actor is not otherwise authorized to access.

### The target changes before proposal

If the selected target is no longer `Accepted` when the author attempts to propose the replacement, ADR Campus keeps the replacement in `Draft` status and requires the author to remove the target or select a currently accepted ADR before trying again.

### The target changes during review

If the target is no longer `Accepted` when a maintainer attempts to accept the replacement, ADR Campus refuses acceptance, preserves both records in their current states, and explains that the proposal no longer targets a current decision. The maintainer may reject the stale proposal with a reason.

### The replacement is rejected

ADR Campus records the rejection under the review-and-decide journey and leaves the target `Accepted` and unchanged.

### Supersession cannot be fully persisted

ADR Campus does not report success or expose a partial transition. The replacement and target remain in their last successfully persisted states so the maintainer can safely determine whether to retry.

### The maintainer retries after an uncertain outcome

ADR Campus ensures that repeating the same acceptance request does not duplicate relationships, decision events, or lifecycle transitions.

### A relationship would be invalid

If the relationship is self-referential, crosses organization boundaries, or would create a cycle, ADR Campus refuses the operation and preserves both ADRs unchanged.

## Acceptance Criteria

### Begin a replacement draft

Given an `Accepted` ADR and an authenticated active member of its organization,
when the member chooses to propose a replacement,
then ADR Campus creates exactly one new private ADR in `Draft` status with its own stable identifier, identifies the member as author, records the accepted ADR as its intended target, and leaves the accepted ADR unchanged.

### Edit the intended target while drafting

Given a replacement ADR in `Draft` status and its active author,
when the author selects another `Accepted` ADR in the same organization or removes the target,
then ADR Campus saves the valid choice without changing either possible target's status or content.

### Restrict the target

Given a replacement draft,
when its author attempts to select a target that is not `Accepted`, belongs to another organization, is the draft itself, or would create a cycle,
then ADR Campus refuses the relationship and preserves the last valid saved target.

### Share intended supersession on proposal

Given a complete replacement draft whose target remains `Accepted`,
when its active author successfully proposes it,
then ADR Campus freezes and shares the intended target with the proposed ADR and leaves the target `Accepted` and in effect.

### Prevent proposal against a stale target

Given a replacement draft whose intended target is no longer `Accepted`,
when its author attempts to propose it,
then ADR Campus keeps it in `Draft` status and requires the target to be removed or replaced with a currently accepted ADR.

### Complete supersession on acceptance

Given a proposed replacement, its still-`Accepted` target, and an active maintainer,
when the maintainer accepts the replacement under the review-and-decide journey,
then ADR Campus atomically changes the replacement to `Accepted`, changes the target to `Superseded`, records reciprocal relationships with the same supersession time, and makes the replacement the current decision.

### Preserve the earlier record

Given a completed supersession,
when an active member reads the superseded ADR,
then ADR Campus preserves its original content, author, proposal metadata, acceptance metadata, and lifecycle history and identifies the accepted ADR that replaced it.

### Preserve the replacement record

Given a completed supersession,
when an active member reads the accepted replacement,
then ADR Campus preserves its own content and lifecycle metadata and identifies the superseded ADR it replaced.

### Leave the target in effect on rejection

Given a proposed replacement and its accepted target,
when a maintainer rejects the replacement,
then ADR Campus records the replacement as `Rejected`, retains its intended-target history, leaves the target `Accepted`, and creates no completed supersession relationship.

### Prevent acceptance against a stale target

Given a proposed replacement whose intended target is no longer `Accepted`,
when a maintainer attempts to accept it,
then ADR Campus refuses acceptance, records no new decision, and preserves the replacement and target in their current states.

### Resolve concurrent replacements

Given multiple proposed replacements for the same accepted target,
when one replacement is successfully accepted,
then ADR Campus completes only that supersession and prevents every other proposal from being accepted against the now-superseded target.

### Preserve direct supersession chains

Given ADR B accepted as a replacement for ADR A and ADR C later accepted as a replacement for ADR B,
when an active member reads any record in the chain,
then ADR Campus preserves the direct A-to-B and B-to-C relationships without replacing them with an A-to-C relationship.

### Make supersession atomic

Given a replacement acceptance that cannot be fully persisted,
when the operation fails,
then ADR Campus does not report success and does not leave either ADR transitioned without the other or create a one-sided relationship.

### Make supersession idempotent

Given a replacement whose acceptance already completed supersession,
when an acceptance request with the same client-generated operation identifier is repeated,
then ADR Campus creates no duplicate decision event or relationship and preserves the original statuses, decider, decision time, and supersession time.

### Preserve discovery semantics

Given a completed supersession,
when an active member opens Current, Historical, or follows either relationship,
then Current contains the accepted replacement but not the superseded target, Historical contains the superseded target, and both detail views provide a navigable link to the other.

## Out of Scope

This journey does not define:

- replacing more than one ADR with a single proposal;
- replacing one ADR with multiple accepted ADRs as one operation;
- editing or retargeting a proposed replacement;
- automatically rejecting competing proposals;
- merging competing replacement proposals;
- direct supersession of a rejected or already-superseded ADR;
- undoing or reversing completed supersession;
- flattening a supersession chain; or
- cross-organization relationships.

## Architectural Implications

The architecture must support:

- a distinction between intended and completed supersession;
- validation of target organization, lifecycle state, identity, and graph integrity;
- immutable intended-target metadata after proposal;
- revalidation of the target when proposal and acceptance are persisted;
- an atomic and idempotent transition affecting the replacement, target, relationships, and history;
- deterministic handling of concurrent replacement proposals;
- immutable, reciprocal, and navigable direct relationships;
- preservation of supersession chains without rewriting history; and
- immediate, consistent reflection of completed supersession in current and historical discovery.

These implications identify required system behavior. They do not prescribe graph, transaction, locking, storage, or interface mechanisms.
