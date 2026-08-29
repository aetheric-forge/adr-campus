# User Story Overview

## ADR Campus

ADR Campus is a simple application for recording the architectural decisions of an organization.

Architectural decisions are valuable beyond the moment in which they are made. They explain the context a team faced, the options it considered, the decision it reached, and the consequences it accepted. ADR Campus gives those decisions a durable home and a small, understandable process through which they can be proposed, reviewed, accepted, and eventually superseded.

This document defines the people, lifecycle, and journeys that shape the first useful version of ADR Campus. The individual story documents refine those journeys into behavior and acceptance criteria. Together, they describe what the application must enable without prescribing how it must be implemented.

## Product Goal

ADR Campus enables a basic organization to maintain a trustworthy and navigable record of its architectural decisions.

A successful first release allows an organization to answer:

- What architectural decisions have we made?
- Why did we make them?
- Who proposed and approved them?
- Which decisions are currently in effect?
- Which later decisions replaced earlier ones?

The application is intended to make the decision record easy to maintain as part of ordinary engineering work. It is not intended to model every governance process an organization might adopt.

## Basic Organization

For the purposes of the first release, an organization is a named group of people sharing one architectural decision record.

An organization has:

- a stable identity and display name;
- members derived from a configured SSO group who can participate in its decision process;
- maintainers derived from a configured SSO group who are entrusted to decide proposals; and
- a collection of ADRs belonging to that organization.

There are two participant roles:

### Member

A member can read the organization's ADRs, create a draft, revise a draft they authored, and propose that draft for review.

### Maintainer

A maintainer has all member capabilities and can accept or reject a proposed ADR. Maintainers also administer the ADR-specific consequences of membership changes, such as recovering drafts whose authors have left.

Users, organization membership, and maintainer assignments are administered exclusively through the organization's SSO control plane. ADR Campus consumes those identities and groups but does not provide a second user-management system.

This is a deliberately modest governance model. More elaborate approval rules, teams, quorums, and organization hierarchies are outside the first-release scope.

## ADR

An Architectural Decision Record captures a single architectural decision in a form that can be understood later.

Each ADR has:

- an identifier that is stable within its organization;
- a title;
- a status;
- the context in which the decision is being made;
- the decision itself;
- the consequences of the decision;
- an author;
- relevant lifecycle dates and participants; and
- links to ADRs it supersedes or by which it is superseded, when applicable.

The exact editorial format may evolve, but the record must preserve enough context to make the decision intelligible without relying on the memories of its original participants.

## ADR Lifecycle

An ADR moves through a small, explicit lifecycle:

`Draft -> Proposed -> Accepted`

`Draft -> Proposed -> Rejected`

`Accepted -> Superseded`

### Draft

The ADR is being prepared. Its author may revise it, and it is not yet an organizational decision.

### Proposed

The ADR has been submitted for consideration. Its content is stable while maintainers review the proposal.

### Accepted

A maintainer has approved the proposal. The ADR represents a decision of the organization and becomes part of its current architectural record.

### Rejected

A maintainer has declined the proposal. The ADR remains in the historical record so that the organization can understand what was considered and why it was not adopted.

### Superseded

The accepted decision has been replaced by a later accepted ADR. The original record remains available and links to the decision that replaced it.

An accepted ADR is not rewritten to make history appear cleaner. Material changes to an accepted decision are made through a new ADR that supersedes it.

## Primary Journeys

The first release is organized around six user journeys.

### Draft a decision

A member captures the context, proposed decision, and expected consequences of an architectural choice. They can save incomplete work and revise it until it is ready for organizational review.

### Propose a decision

The author submits a complete draft for review. ADR Campus records when it was proposed and makes its status clear to the organization.

### Review and decide

A maintainer examines a proposed ADR and either accepts or rejects it. ADR Campus records the outcome, the responsible maintainer, the time of the decision, and an explanatory note when one is provided or required by the applicable story.

### Discover and understand decisions

A member browses and searches the organization's ADRs, distinguishes current decisions from historical proposals, and opens a record to understand its context, decision, consequences, authorship, and lifecycle.

### Supersede a decision

A member proposes a new ADR to replace an accepted decision. If the new ADR is accepted, ADR Campus marks the earlier ADR as superseded and connects both records so that the evolution of the architecture is understandable.

### Administer the organization

The deployment maps the organization to authoritative SSO member and maintainer groups. Members can see the current SSO-derived roster, while maintainers govern ADR-specific recovery and organization settings. User, membership, and role changes occur only in the SSO control plane.

## First-Release Outcome

The first release is complete when a basic organization can carry one decision through its full useful history:

1. The deployment establishes the organization and maps its authoritative SSO member and maintainer groups.
2. A member creates and revises an ADR draft.
3. The member proposes the ADR for review.
4. A maintainer accepts or rejects the proposal.
5. Members can find and read the resulting record.
6. A later accepted ADR can supersede the original accepted decision without erasing its history.

The experience should make the status and authority of every record unambiguous.

## Product Principles

### Preserve the reasoning

An ADR is more than its final decision. Context and consequences are first-class parts of the record.

### Preserve history

Organizational decisions are not silently rewritten or deleted when circumstances change. Their lifecycle and relationships tell the story of the architecture.

### Keep authority explicit

The application clearly distinguishes personal drafts and proposals from decisions accepted by the organization.

### Prefer a small, complete workflow

The first release supports a simple decision process from beginning to end. It does not attempt to become a general-purpose workflow or governance platform.

### Make the current state easy to find

Historical context must remain accessible, but a reader should be able to identify the decisions currently in effect without reconstructing the entire record.

## First-Release Boundaries

The following capabilities are outside the initial scope unless an individual story explicitly introduces them:

- multiple organizations for a single signed-in person;
- nested organizations, departments, or teams;
- custom roles or permission policies;
- configurable ADR lifecycles;
- voting, quorums, or multi-stage approval;
- threaded discussion and general-purpose collaboration;
- real-time co-authoring;
- attachments and rich document publishing;
- external issue-tracker or source-control integrations;
- notifications and subscriptions;
- anonymous or public participation;
- destructive deletion of decided ADRs; and
- analytics, compliance reporting, or policy enforcement.

These exclusions keep the initial stories focused. They do not preclude later evolution.

## Story Structure

Each journey may be refined into one or more story documents. A story should include:

- the actor and desired outcome;
- the value of the outcome;
- relevant preconditions;
- the primary scenario;
- alternate and failure scenarios where they affect product behavior;
- acceptance criteria stated in observable terms;
- applicable lifecycle and authorization rules; and
- open questions that require a product or architectural decision.

Implementation design belongs in the architecture documents. Stories may identify architectural implications, but should describe externally meaningful behavior rather than internal components.
