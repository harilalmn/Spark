# ADR-0025 — The source is closed; the builds stay public

**Status:** Accepted, 2026-09-09
**Supersedes:** [ADR-0006](0006-mit-licence-dco-not-cla.md) (MIT licence, DCO rather than a CLA)
**Tasks:** `E12-T22`, `E12-T23`

## Context

`ADR-0006` chose MIT with a DCO, on a reasoning worth restating because it was sound: Spark's
adoption story leaned on third-party node packages as a contribution path needing no kernel
expertise, and a permissive licence with a one-line sign-off is the lowest-friction way to accept
those.

On 2026-09-09 the client made the repository private and said: *let us not keep it opensource.
Instead, let us keep the release public.*

That is a business decision and this ADR records it rather than argues with it. What it changes is
narrower than "Spark is now closed", and the boundaries are the point.

## Decision

**Spark's source is not public and is not open source.** `LICENSE` stops being MIT.

**The builds stay public**, in `harilalmn/Spark-Releases` — a repository holding no source. A
private repository's releases are private with it; GitHub gives them the repository's visibility
and offers no setting that separates the two, which is `E12-T22` and is why a second repository
exists at all.

## What this does not do, and cannot

**It does not revoke the MIT licence already granted.** Every version published while the
repository was public was published under MIT, and that grant is irrevocable for anybody who took a
copy. This changes the terms **going forward**; it does not reach backwards, and no document in
this repository may imply that it does.

Whether anybody took such a copy was checked rather than assumed: at the moment of the change the
repository had **0 forks and 0 stars**.

**It does not free Spark from other people's licences.** Spark links **OpenCascade** under
LGPL-2.1 with the Open CASCADE exception, and that obliges dynamic linking, unmodified replaceable
libraries, no single-file seal, no NativeAOT over the kernel, and **a standing offer of source for
OpenCascade**. None of that cares what licence Spark's own code carries. While the repository was
public the offer was satisfied by the repository; it now lives in `Spark-Releases`' README, which
is the public face of the product and the right place for it. `Q13` asks counsel whether a tag
reference suffices or a hosted archive is needed, and that question is sharper now than when it was
written.

**It does not settle the contributor question, because there is none to settle.** `ADR-0006` chose
DCO precisely so that contributions arrived under a licence the project could rely on. Every commit
in this repository is by one author, so there is no third-party copyright to relicense and nobody
to ask. Had there been, this decision would have needed their agreement rather than a note.

## The licence text is a placeholder and is marked as one

`LICENSE` now says all rights are reserved. That is accurate and it is not a product licence: it
says nothing about what somebody downloading the installer may do with it, which is the question an
end-user licence answers. Writing that is counsel's work, not a session's, and pretending otherwise
by drafting something lawyerly would be worse than leaving an honest placeholder.

## Consequences

**`CONTRIBUTING.md` describes something that no longer exists.** It invites pull requests under MIT
with a DCO sign-off, to a repository nobody outside can see. It is rewritten to say what is true.

**The README's positioning line goes.** *"Spark is an open-source, independent alternative to
Autodesk Dynamo Sandbox"* was two claims, and the first is now false. The second is retained
because it is accurate and because Autodesk's own trademark guidelines permit exactly this kind of
referential comparison — with an attribution the project did not previously carry, added in the
same change.

**The public face is now two repositories and they must not drift.** `Spark-Releases`' README is
what a user reads; this repository's README is what a developer reads. The third-party notices and
the OpenCascade offer exist in both, and a change to either has to be made twice. That is a cost
this decision accepts and is written down so the next person knows to pay it.

**Nothing about the build changes.** MinVer still derives the version from the tag, the gates are
the gates, and `THIRD-PARTY-NOTICES.md` ships beside the executable exactly as before — because
every obligation in it came from somebody else's licence, not from ours.
