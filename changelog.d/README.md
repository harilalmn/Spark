# `changelog.d/` — one file per change, assembled at release

**Status:** Current.
**Owner:** `docs-author`
**Last updated:** 2026-09-15 (`E10-T12`: the directory the rule had been pointing at)

Every user-visible change adds **a file here** rather than editing a shared changelog.
`scripts/assemble-changelog.py` folds them into the *What changed* section of the release notes,
and the files are deleted when the release goes out.

## The format

One file per change, named `<kind>-<row>-<slug>.md`:

```
fixed-E5-T11-third-party-import-crash.md
```

- **`<kind>`** is one of `added`, `changed`, `fixed`, `removed`, `deprecated` — these are the
  headings the assembler groups under, in that order.
- **`<row>`** is the register row the change belongs to, `E5-T11`. **Required**, because a change
  with no row is a change nobody can trace to a reason.
- **`<slug>`** is for humans reading `ls`. The assembler ignores it.

The file's content is **one sentence, in the present tense, addressed to a user**, ending with a
full stop. Not a commit message and not a row title:

```markdown
Importing a third-party NuGet package no longer crashes when the package contains an abstract
class with a public constructor.
```

A second paragraph is allowed when the first cannot carry it — a caveat, a migration step, a
number a reader needs. Keep it to that.

## What belongs here

**A change a user of Spark would notice.** A new node, a fixed crash, a changed default, a removed
option, a file format that reads something it did not.

**Not** internal work: a refactor, a test, a document, a CI change, a note. Those are in the
register and the journal, which is where somebody looking for them will be. A changelog listing
them is a changelog nobody finishes reading, and the point of this directory is that the section it
produces is short enough to read.

## Why one file per change

The stated reason in `E10-T12` is that a single changelog file becomes a merge-conflict magnet
when several branches edit its top. **That is a real argument and it is not currently this
project's** — 206 commits have gone straight to `main` and two pull requests exist in its whole
history.

**The argument that does hold today is simpler: there was no changelog at all.** Every release
from `v0.1.0` to `v2026.9.0` shipped with install instructions and nothing about what had changed
in it — `.github/release-notes.md` is a template, and `--generate-notes` is deliberately off. A
user upgrading was told how to install and never what they were getting. One file per change is
the cheapest mechanism that makes writing the sentence part of making the change, instead of an
archaeology exercise at release time.

## At release

`scripts/assemble-changelog.py` prints the section; `release.yml` splices it into the notes under
`{CHANGELOG}`. **Delete the fragments in the release commit**, so the directory holds exactly what
has not shipped yet — that, and not a version header, is what makes it obvious.

`ChangelogFragmentChecks` in `Spark.Docs.Verify` fails a fragment with an unknown kind, no row, an
empty body, or a **first paragraph** that does not end in a full stop — the paragraph rather than
the line, because these files are wrapped like every other document here.

**A malformed fragment stops the release rather than being skipped**, which is the point: a
fragment dropped for a typo in its name is a change that shipped with nothing saying so. Check
yours before you commit it:

```
python scripts/assemble-changelog.py --check   # validates, prints nothing
python scripts/assemble-changelog.py           # prints the section as it will appear
```
