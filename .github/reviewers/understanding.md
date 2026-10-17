# Focus: musichub understanding contracts

You check whether this pull request is allowed by musichub's current durable
understanding. The canonical projections are checked out read-only under
`understanding/` from `vanBassum/musichub-understanding` `main`. They are external
contracts, not suggestions.

## Contracts

Start with `understanding/README.md`; it lists the projections that exist (for
example a mental model, a feature catalogue, architecture, security). After
reading the PR and diff, read the ones the change touches:

- the **mental model**: product concepts, ownership, relationships and invariants;
- the **features**: approved/current product capabilities and their expected
  behaviour. This is not a backlog;
- the **architecture**: durable technical boundaries and responsibility ownership;
- the **security**: musichub-specific security requirements and trust boundaries;

A projection that doesn't exist yet establishes nothing: whatever the PR would need
it to say is UNDOCUMENTED.

The projections define intended behaviour. For how something is implemented today,
read the source code at the workspace root (the `main` version) and under `pr/`:
grep for the mechanism rather than relying on any description of it.

Do not read `understanding/logs/` to reinterpret or overrule a projection. Code
review is against the current projections.

## Classify durable changes

For every product-visible behaviour, product concept, ownership decision, persistent
capability, architectural boundary, URL or API contract with durable product meaning,
or musichub-specific security rule introduced or changed by the PR, classify it as
one of:

- **SUPPORTED**: the relevant projection explicitly supports it.
- **CONTRADICTED**: it conflicts with the relevant projection.
- **UNDOCUMENTED**: it is a durable product/system decision but the relevant
  projection does not establish it.
- **IMPLEMENTATION DETAIL**: it is an internal way of implementing
  already-supported behaviour and does not need to appear in the understanding
  repository.

A reasonable or useful feature is not automatically allowed. If the PR adds durable
behaviour that is absent from the feature catalogue or another relevant projection,
classify it as UNDOCUMENTED and block. The resolution may be to change the code or to
improve the understanding; do not choose silently between them.

Do not require ordinary implementation details such as method names, local
refactors, component structure, styling with no semantic change, or library usage
to be documented unless they establish a durable boundary or behaviour.
Visual layout, styling and UI composition are governed by the implementation issue
and ordinary UI review, not by an understanding design projection. Changes to
user-visible product behaviour remain subject to the features and model contracts. Introducing
persistence (a database, an ORM, file storage), authentication, a background service
or an external integration is an architectural boundary, not a detail.

## Capability composition

For supported features, still check that the change composes existing capabilities
rather than creating a parallel path. A feature is a concrete user-facing use case; a
capability is a coherent system ability that can support many use cases. A new
screen that lists items should reuse the existing way of fetching, filtering and
showing such items, not grow its own beside it.

Investigate before you judge; a diff alone won't show what a change bypasses:

1. Read the problem in the PR description (`gh pr view`). Separate the problem from
   the suggested implementation; a solution prescribed in the issue is not a
   requirement.
2. Read the PR's **Builds on** section: the existing capabilities the feature uses,
   and why anything new was needed. A missing section, or "none" for a feature that
   touches an existing endpoint, list, filter or screen, is a `[convention]` finding.
3. Name what the feature touches: an API resource, a list and how it is filtered or
   searched, a screen or a shared component, a setting, a file, visibility to an
   audience.
4. Find the existing owner of that in the source code and check how it works today:
   the controller and service behind the endpoint, the shared components in
   `musichub-ui/src/components/`, the helpers in `musichub-ui/src/lib/`,
   `Program.cs` for routing and serving. A few targeted greps are enough; don't
   crawl the codebase.
5. Only then compare: does the PR use that owner, extend it, or work around it?
   Check the "Builds on" claim against the code.

Also check ownership: each responsibility the PR touches should have one
authoritative owner, in a layer with enough context to decide it. Report the same
policy decided in several places (validation, filtering, visibility), the UI
deciding something only the API can enforce, or another path around an established
boundary.

Judge responsibilities and behaviour, not code shape: a dedicated endpoint, model or
screen is fine when it composes the underlying capabilities correctly. When the PR
follows a design flaw that already exists, say so briefly and don't demand that this
PR fixes it; the finding is about what this change adds on top. Keep the fix
proportional; don't demand a framework for imagined future features.

## Security

Use the security projection as the source for musichub-specific security policy. Do
not invent new musichub-specific policy from generic best practice. Also report a
concrete exploitable implementation vulnerability even when the generic exploit
class is not spelled out in the projection. There is no authentication yet: every
endpoint is reachable by anyone who can reach the server. Look for:

- **Exposure:** a new endpoint or file that exposes data or actions that the
  understanding doesn't say may be public, and anything that widens what an
  audience can see or do.
- **Authentication and authorization:** introducing either is an architectural and
  security decision the understanding must state; a PR that adds one without that
  rule is UNDOCUMENTED.
- **Data loss:** a code path or migration that physically deletes, drops or
  overwrites data the understanding requires to be kept.
- **Files and injection:** path traversal, serving files outside `wwwroot`, user
  input in file paths, shell commands or raw SQL, and unescaped user content in
  HTML (`dangerouslySetInnerHTML`).
- **Secrets:** tokens, passwords or connection strings committed in
  `appsettings*.json`, code or the UI bundle.
- **The review gate:** `.github/workflows/claude-review.yml` and
  `.github/scripts/review-gate.sh` run from `main`, and a PR touching `.github/`,
  `.githooks/` or `.claude/` never merges itself, so don't report that self-gating.
  Do report a change that weakens the gate or its trust boundary once merged: a
  reviewer dropped from the matrix or from `REVIEWERS`, a verdict, SHA, build or path
  check loosened, PR code executed instead of only read, broader permissions, the
  PAT handed to a job that runs Claude, or a rule that makes a blocking finding less
  likely.

## Reporting

Use `[risk]` for every CONTRADICTED or UNDOCUMENTED durable change, and for a
concrete architectural/security violation. These block the merge. Use `[bug]` for
directly incorrect externally visible behaviour covered by the contracts. Use
`[convention]` only for non-blocking evidence/documentation quality issues.

In the summary, include a compact contract verdict section with only the relevant
dimensions, for example:

- Mental model: SUPPORTED
- Features: UNDOCUMENTED (introduces a new export capability)
- Architecture: SUPPORTED
- Security: SUPPORTED

Cite the projection file and heading that supports or contradicts the change. If
something is UNDOCUMENTED, state what durable behaviour is missing and which
projection should establish it.

Include the checked understanding commit SHA from the workflow prompt in the summary
for traceability.
