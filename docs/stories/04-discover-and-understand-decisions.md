# Discover and Understand Decisions

## User Story

As an organization member, I want to find and read architectural decisions so that I can understand the organization's current architecture and the reasoning and history behind it.

## Value

An ADR record is useful only when people can find the applicable decision and understand it without relying on the memories of its original participants. ADR Campus must make current decisions easy to identify while preserving proposed, rejected, and superseded records as organizational history.

## Actor

The primary actor is an authenticated active member of the organization.

A maintainer uses the same discovery capabilities because a maintainer has all member capabilities.

## Preconditions

- The organization exists.
- The actor is authenticated.
- The actor is an active member of the organization.

The organization may have no shared ADRs yet. An empty decision record is a valid state.

## Primary Scenario

1. The member opens the organization's architectural decision record.
2. ADR Campus shows the current view, containing ADRs with `Accepted` status.
3. Each result clearly shows its identifier, title, status, author, and relevant proposal or decision date.
4. The member enters a text query, selects one or more lifecycle statuses, or selects a result-column heading to refine and sort the results.
5. ADR Campus shows a server-paginated page of matching shared ADRs in a stable order and explains the active query, filters, and sort.
6. The member selects an ADR.
7. ADR Campus displays its title, context, decision, consequences, lifecycle status, authorship, proposal metadata, decision metadata when present, and lifecycle history.
8. When the ADR supersedes or is superseded by another ADR, the member follows the relationship to that record.
9. The member can return to the result set without losing the query, filters, sort, and page they used.

## Shared Decision Record

The shared decision record contains ADRs with these statuses:

- `Proposed`;
- `Accepted`;
- `Rejected`; and
- `Superseded`.

An ADR in `Draft` status is private working material and never appears in another member's browse results, search results, counts, filters, suggestions, or direct record access. An expired draft never appears in discovery.

The current architectural record contains only ADRs with `Accepted` status. `Proposed`, `Rejected`, and `Superseded` ADRs remain discoverable but are not represented as current decisions.

## Browse Views

The default view is **Current**, containing only `Accepted` ADRs.

The member can select:

- **Current** for accepted decisions presently in effect;
- **Proposed** for decisions awaiting a maintainer outcome;
- **Historical** for rejected and superseded records; or
- **All shared records** for every non-draft ADR.

Within **All shared records**, the member may filter by any combination of `Proposed`, `Accepted`, `Rejected`, and `Superseded` statuses. Selecting no status filter is equivalent to including all shared statuses.

Results are ordered by their most recent shared lifecycle event, newest first, with the stable ADR identifier used to break ties. The relevant event is proposal for a `Proposed` ADR, decision for an `Accepted` or `Rejected` ADR, and completed supersession for a `Superseded` ADR. Draft creation, editing, recovery, and author reassignment do not affect this ordering. Repeating the same browse request against unchanged records produces the same order.

Each result column that supports sorting has an interactive heading. The member can sort by:

- stable identifier;
- title;
- status;
- author; or
- relevant lifecycle date.

Selecting a sortable heading makes that column the primary sort. Selecting it again reverses the direction. ADR Campus visibly identifies the active sort column and direction and always uses the stable ADR identifier as the final tie-breaker. Sorting is performed by the server over the complete matching result set, not only over the records on the current page.

For the relevant lifecycle date column, a `Proposed` ADR uses its proposal date, an `Accepted` or `Rejected` ADR uses its decision date, and a `Superseded` ADR uses its supersession date. Every shared ADR therefore has an applicable value for this column.

## Search

Search operates within the selected browse view and status filters.

A non-empty search query:

- is normalized by removing leading and trailing whitespace;
- contains between 3 and 200 characters;
- contains at least one letter or number;
- may contain ordinary text; and
- may not contain control characters.

An empty query clears text search and returns to filtered browsing.

Search is case-insensitive and matches shared ADRs against:

- stable identifier;
- title;
- context;
- decision;
- consequences;
- author display name;
- proposer display name;
- decider display name; and
- acceptance notes or rejection reasons; and
- relevant lifecycle date.

The relevant lifecycle date uses the same event defined for browsing and sorting: proposal for `Proposed`, decision for `Accepted` or `Rejected`, and completed supersession for `Superseded`. Date search performs literal phrase matching against the ISO `YYYY-MM-DD` form and the displayed abbreviated or full English month forms, such as `Sep 2, 2026` and `September 2, 2026`. Partial phrases such as a four-digit year may match those representations. ADR Campus does not interpret the phrase as a general date expression or date range.

For the first release, the normalized query is treated as one plain-text phrase. An ADR matches when that phrase occurs in any searchable field. Search does not interpret operators, wildcards, regular expressions, or query syntax.

### Omnibar Suggestions

The search omnibar helps the member reach a likely record without first opening a complete result set.

- Fewer than 3 normalized characters do not trigger a suggestion request.
- After the query reaches 3 valid characters, ADR Campus waits approximately 300 milliseconds after the latest input before requesting suggestions.
- New input cancels an outstanding suggestion request or causes its response to be ignored.
- Suggestions respect the active browse view and status filters.
- ADR Campus displays at most 8 suggestions.
- An exact stable-identifier match ranks first, followed by title matches and then matches in other searchable content.
- When additional matches exist, ADR Campus offers a **View all results** action rather than expanding the suggestion list.
- Selecting a suggestion opens that ADR directly.
- Submitting the query or selecting **View all results** opens the complete paginated result set.

Suggestion ranking helps navigation but does not exclude records from a complete search.

Complete search results use the active result-column sort. When the member has not selected a sort, search results rank exact stable-identifier matches first, title matches second, and other content matches third; records within the same rank are ordered by most recent lifecycle event and then stable identifier. Highlighting or snippets may help the member understand a match but must not change which ADRs match.

## Pagination

Browse and complete search results are paginated by the server with 25 records per page.

ADR Campus shows the current page and whether earlier or later pages are available. Changing the view, filters, query, or sort returns the member to the first page. Moving between pages preserves the active view, filters, query, and sort.

The server applies authorization, matching, sorting, and pagination in that order. The interface does not retrieve the complete matching record set in order to search, sort, or paginate it locally.

## Result Presentation

Each browse or complete-search result provides enough information to distinguish records without opening each one:

- stable identifier;
- title;
- lifecycle status;
- author;
- proposal date for a proposed ADR;
- decision date and decider for an accepted or rejected ADR; and
- supersession date and replacement ADR for a superseded ADR.

Status must be conveyed in text and not by color alone.

ADR Campus distinguishes an empty organization, no results for the current filters, and no results for a search query. It does not imply that no ADRs exist when records are merely excluded by the current view or filters.

## ADR Detail

The detail view presents the complete shared record:

- stable identifier and title;
- current lifecycle status;
- context, decision, and consequences exactly as proposed;
- author and creation time;
- proposer and proposal time;
- decider, decision time, and acceptance note or rejection reason when present;
- supersession relationships and times when present; and
- an ordered lifecycle history, including an audited author reassignment when one occurred before proposal.

The detail view makes the authority of the record unambiguous:

- `Proposed` is awaiting a decision;
- `Accepted` is currently in effect;
- `Rejected` was considered but not adopted; and
- `Superseded` was once accepted but has been replaced.

Historical people remain identified on the ADR after they leave the organization. Their current display name is resolved retroactively from SSO when available. Identifying-property changes, such as an email-address change, are recorded with the effective identity values in lifecycle history so earlier events retain the earlier values and later events show the later values. Their recorded actions are part of the durable organizational history.

## Authorization and Visibility Rules

- Every active organization member may browse, search, and read every shared ADR in that organization.
- A person who is not an active member cannot browse, search, or directly access the organization's ADRs.
- Search and result counts must apply authorization before returning records or metadata.
- A draft is visible only through the author and recovery rules defined by the drafting journey, never through shared discovery.
- Lifecycle changes must be reflected in discovery without misrepresenting the record's current persisted status.
- Discovery is read-only and does not alter an ADR or its lifecycle.

## Alternate and Failure Scenarios

### The organization has no shared ADRs

ADR Campus explains that no decisions have been proposed yet and, when the member is permitted to draft, offers a clear path to begin one. It does not present an error.

### No records match the selected view or filters

ADR Campus reports that no shared ADRs match the current selection and identifies the active view and filters. The member can clear or change them.

### No records match the query

ADR Campus reports that no matching shared ADRs were found, preserves the query and filters, and provides a clear way to clear the query.

### The query is invalid

If a non-empty normalized query contains fewer than 3 or more than 200 characters, contains no letter or number, or contains a control character, ADR Campus explains the applicable constraint and does not execute the invalid search.

### A suggestion response arrives out of order

If the member changes the query before an earlier suggestion request completes, ADR Campus ignores the obsolete response and does not replace suggestions for the current input with stale results.

### The selected ADR changed after results were shown

ADR Campus displays the current persisted record when the member opens it. If it no longer satisfies the previous filter because its lifecycle changed, ADR Campus does not present stale status as current and preserves a way back to the earlier result context.

### A linked ADR is unavailable

If an ADR contains a relationship to a record that cannot be retrieved, ADR Campus preserves the visible relationship information and explains that the linked record is unavailable. It does not discard or conceal the source ADR.

### Discovery fails

ADR Campus explains that records could not be retrieved and does not present an incomplete result set as complete. The member can safely retry without changing any ADR.

### A non-member attempts access

ADR Campus refuses browse, search, and direct-record access and reveals no ADR content, result counts, titles, statuses, or relationship information.

## Acceptance Criteria

### Show current decisions by default

Given an authenticated active organization member,
when the member opens the decision record without a saved view, query, or filter,
then ADR Campus displays only ADRs with `Accepted` status and identifies the view as Current.

### Browse each shared lifecycle group

Given shared ADRs in `Proposed`, `Accepted`, `Rejected`, and `Superseded` statuses,
when an active member selects Proposed, Current, Historical, or All shared records,
then ADR Campus returns exactly the statuses defined for that view and clearly labels every result's status.

### Filter shared records by status

Given an active member viewing all shared records,
when the member selects one or more lifecycle statuses,
then ADR Campus returns only shared ADRs having a selected status and identifies every active filter.

### Order browse results deterministically

Given multiple matching shared ADRs,
when ADR Campus returns browse results without a member-selected sort,
then it orders them by proposal date for `Proposed`, decision date for `Accepted` and `Rejected`, and supersession date for `Superseded`, all descending, and uses stable ADR identifier to break ties without considering draft reassignment.

### Rank complete search results

Given multiple ADRs matching a complete search and no member-selected sort,
when ADR Campus returns the results,
then it ranks exact stable-identifier matches before title matches and other content matches, orders records within each rank by most recent lifecycle event descending, and uses stable ADR identifier as the final tie-breaker.

### Sort by a result column

Given multiple matching shared ADRs,
when an active member selects a sortable column heading,
then ADR Campus sorts the complete matching result set by that column, visibly identifies the column and direction, returns to the first page, and uses stable ADR identifier as the final tie-breaker.

### Reverse a column sort

Given results already sorted by a selected column,
when the member selects the same column heading again,
then ADR Campus reverses the primary sort direction and preserves stable ADR identifier as the final tie-breaker.

### Sort by relevant lifecycle date

Given shared ADRs in more than one lifecycle status,
when an active member sorts by relevant lifecycle date,
then ADR Campus uses proposal date for `Proposed`, decision date for `Accepted` and `Rejected`, and supersession date for `Superseded` before applying the stable ADR identifier tie-breaker.

### Search shared ADR content

Given an active member, a selected view and filters, and a valid non-empty query of 3 to 200 characters,
when the normalized phrase occurs case-insensitively in any searchable field of a shared ADR within that selection,
then ADR Campus includes that ADR in the results.

### Exclude non-matches

Given an active member and a valid non-empty query,
when a shared ADR does not contain the normalized phrase in any searchable field or is outside the selected view and filters,
then ADR Campus excludes it from the results.

### Search by relevant lifecycle date

Given an active member, a selected view and filters, and a shared ADR with a relevant lifecycle date,
when the member searches using a literal phrase contained in its ISO date, displayed abbreviated month date, or displayed full month date,
then ADR Campus includes that ADR and applies the same view, filter, ranking, sorting, and pagination rules as any other searchable field.

### Treat search as plain text

Given a valid query containing punctuation or characters commonly used as search operators,
when an active member searches,
then ADR Campus treats the normalized query as a literal plain-text phrase and does not execute it as an operator, wildcard, or regular expression.

### Validate a query

Given a non-empty normalized query that contains fewer than 3 or more than 200 characters, contains no letter or number, or contains a control character,
when an active member attempts to search,
then ADR Campus explains the applicable constraint and does not execute the invalid query.

### Delay omnibar suggestions

Given an active member entering a search query,
when the normalized input contains fewer than 3 characters,
then ADR Campus requests no suggestions,
and when it contains at least 3 valid characters and remains unchanged for approximately 300 milliseconds,
then ADR Campus requests suggestions within the active view and filters.

### Bound omnibar suggestions

Given a valid omnibar query matching shared ADRs,
when suggestion results are returned,
then ADR Campus displays at most 8 authorized suggestions, ranks exact identifier matches before title matches and other content matches, and offers View all results when additional matches exist.

### Ignore obsolete suggestions

Given an outstanding suggestion request,
when the member changes the query before its response is applied,
then ADR Campus cancels the request or ignores its response and does not replace current suggestions with obsolete results.

### Open complete search results

Given a valid omnibar query,
when the member submits it or selects View all results,
then ADR Campus opens the complete server-paginated result set with the query, active view, and filters preserved.

### Paginate on the server

Given more than 25 authorized ADRs matching the active view, filters, and query,
when ADR Campus returns results,
then it returns the requested page of at most 25 records after authorization, matching, and sorting have been applied to the complete result set.

### Preserve context between pages

Given multiple pages of matching records,
when the member moves to another page,
then ADR Campus preserves the active view, filters, query, and sort and identifies the current page.

### Clear text search

Given active status filters and a non-empty query,
when the member clears the query,
then ADR Campus preserves the selected view and filters and displays the corresponding filtered browse results.

### Never discover drafts

Given an ADR in `Draft` status, including one in its recovery window,
when any member or maintainer browses, searches, receives result counts, or attempts shared direct-record access,
then ADR Campus does not reveal the draft or any of its content or metadata through discovery.

### Read a complete shared ADR

Given any shared ADR and an active member of its organization,
when the member opens it,
then ADR Campus displays its proposed content, current status, applicable lifecycle metadata, and ordered lifecycle history without changing the record.

### Follow a supersession relationship

Given a shared ADR related to another ADR by supersession,
when an active member follows the relationship,
then ADR Campus opens the related shared ADR and makes the direction of the relationship clear.

### Preserve historical attribution

Given a shared ADR whose author, proposer, or decider is no longer active,
when an active member reads the ADR,
then ADR Campus still identifies that person and their recorded action without representing them as a current member.

### Reflect identity-property changes

Given an SSO identity whose display name or other identifying property has changed,
when an active member reads an attributed ADR,
then ADR Campus presents the current display name retroactively and preserves the identifying-property value effective at each recorded lifecycle event.

### Distinguish empty states

Given either an organization with no shared ADRs, filters with no matching ADRs, or a query with no matches,
when ADR Campus shows the empty result,
then it accurately distinguishes which condition occurred and does not imply that filtered or unmatched records do not exist.

### Preserve result context

Given an active member who opens an ADR from filtered or searched results,
when the member returns to the results,
then ADR Campus restores the prior view, filters, query, sort, page, and result ordering when the underlying records have not changed.

### Begin a new session with Current

Given an active member beginning a new signed-in session,
when the member first opens the organization's decision record,
then ADR Campus begins with the Current view and default newest-first ordering rather than restoring a view from an earlier session.

### Restrict discovery to active members

Given a person who is not an active member of the organization,
when that person attempts to browse, search, or directly access a shared ADR,
then ADR Campus refuses access and reveals no ADR content or discovery metadata.

## Out of Scope

This journey does not define:

- public or anonymous access;
- cross-organization discovery;
- saved searches, bookmarks, or subscriptions;
- notifications;
- advanced query syntax, fuzzy matching, stemming, or semantic search;
- tags, categories, or custom metadata;
- comments or discussion;
- export, printing, or external publishing;
- analytics or reporting; or
- changes to ADR content or lifecycle.

## Architectural Implications

The architecture must support:

- authorization-aware reads that cannot leak private or organization-scoped data;
- a shared-record projection that excludes drafts and distinguishes current from historical decisions;
- case-insensitive plain-text search across the defined ADR, attribution, and relevant lifecycle-date fields;
- bounded, delayed omnibar suggestions that cannot be replaced by obsolete responses;
- composable view, status-filter, and query criteria;
- server-side pagination and sortable result columns with deterministic ordering;
- complete lifecycle history and durable historical attribution;
- navigable supersession relationships;
- accurate empty and failure states; and
- restoration of a member's result context after reading a record.

These implications identify required system behavior. They do not prescribe interface, indexing, storage, or query technologies.
