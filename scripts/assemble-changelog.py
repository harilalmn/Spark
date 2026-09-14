#!/usr/bin/env python3
"""Fold the fragments in changelog.d/ into the *What changed* section of the release notes.

    python scripts/assemble-changelog.py           # print the section to stdout
    python scripts/assemble-changelog.py --check   # validate the fragments, print nothing
    python scripts/assemble-changelog.py --directory DIR   # for tests, and for a dry run

**Why this exists, and it is not the reason `E10-T12` gives.** The row says a single changelog
file becomes a merge-conflict magnet. That is a real argument and it is not currently this
project's -- 206 commits have gone straight to `main` and two pull requests exist in its whole
history. The argument that holds is simpler: *every release from `v0.1.0` to `v2026.9.0` shipped
with no what-changed at all*. `.github/release-notes.md` is install instructions, and
`--generate-notes` is deliberately off. A user upgrading was told how to install and never what
they were getting.

**The whole of the format is the file name**, `<kind>-<row>-<slug>.md`, which is what lets this
script be a hundred lines and lets `changelog.d/README.md` be the specification a contributor
actually reads. Parsing lives in `fragments()` and nowhere else; `ChangelogFragmentChecks` in
`Spark.Docs.Verify` drives this script as a subprocess rather than reimplementing it, so there is
exactly one definition of what a well-formed fragment is.

**An empty directory prints nothing at all** -- not an empty heading. A release with no
user-visible change is a real thing (a rebuild, a signing fix), and a *What changed* heading with
nothing under it is worse than no heading.
"""

from __future__ import annotations

import argparse
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# The headings, in the order they are printed. A fragment's kind is the first segment of its file
# name and has to be one of these -- the list is deliberately short, and `changelog.d/README.md`
# repeats it. ChangelogFragmentChecks reads this line out of this file and asserts the README
# agrees, so the two cannot drift.
KINDS = ["added", "changed", "fixed", "removed", "deprecated"]

HEADINGS = {
    "added": "Added",
    "changed": "Changed",
    "fixed": "Fixed",
    "removed": "Removed",
    "deprecated": "Deprecated",
}

# `<kind>-<row>-<slug>.md`. The row is required: a change nobody can trace to a reason is a
# sentence in a release that nobody can check.
NAME = re.compile(r"^(?P<kind>[a-z]+)-(?P<row>E(?P<epic>\d+)-T(?P<task>\d+))-(?P<slug>[a-z0-9-]+)\.md$")


class Fragment:
    """One file in `changelog.d/`, parsed."""

    def __init__(self, name: str, kind: str, epic: int, task: int, slug: str, body: str) -> None:
        self.name = name
        self.kind = kind
        self.epic = epic
        self.task = task
        self.slug = slug
        self.body = body

    @property
    def order(self) -> tuple[int, int, int, str]:
        return (KINDS.index(self.kind), self.epic, self.task, self.slug)


def fragments(directory: str) -> tuple[list[Fragment], list[str]]:
    """Every fragment in `directory`, and a complaint for every file that is not one.

    `README.md` is the specification and is skipped; anything else that does not parse is a
    problem rather than something to ignore, because a fragment silently dropped for a typo in its
    name is a change that does not appear in the release and nothing says so.
    """
    parsed: list[Fragment] = []
    problems: list[str] = []

    if not os.path.isdir(directory):
        return parsed, [f"{directory} does not exist."]

    for name in sorted(os.listdir(directory), key=str):
        if name == "README.md":
            continue

        path = os.path.join(directory, name)

        if not os.path.isfile(path):
            continue

        match = NAME.match(name)

        if match is None:
            problems.append(
                f"{name}: not a fragment name. Expected <kind>-<row>-<slug>.md, where kind is one "
                f"of {', '.join(KINDS)} and row is a register row such as E10-T12."
            )
            continue

        if match.group("kind") not in KINDS:
            problems.append(
                f"{name}: '{match.group('kind')}' is not a kind. Expected one of {', '.join(KINDS)}."
            )
            continue

        with open(path, encoding="utf-8") as handle:
            body = handle.read().replace("\r\n", "\n").strip()

        if not body:
            problems.append(f"{name}: empty. A fragment is a sentence, not a placeholder.")
            continue

        # The first paragraph is the sentence that goes in the release. It may wrap over several
        # lines -- these files are wrapped like every other document here -- so the rule is about
        # the paragraph, not the line.
        first = body.split("\n\n")[0].strip()

        if not first.endswith("."):
            problems.append(
                f"{name}: the first paragraph does not end in a full stop, so it is a title rather "
                f"than a sentence: {first.splitlines()[0]!r}"
            )
            continue

        parsed.append(Fragment(
            name=name,
            kind=match.group("kind"),
            epic=int(match.group("epic")),
            task=int(match.group("task")),
            slug=match.group("slug"),
            body=body,
        ))

    return parsed, problems


def render(parsed: list[Fragment]) -> str:
    """The *What changed* section, or the empty string when there is nothing to say."""
    if not parsed:
        return ""

    lines = ["## What changed", ""]

    for kind in KINDS:
        group = [f for f in parsed if f.kind == kind]

        if not group:
            continue

        lines.append(f"### {HEADINGS[kind]}")
        lines.append("")

        for fragment in sorted(group, key=lambda f: f.order):
            paragraphs = [p.strip() for p in fragment.body.split("\n\n") if p.strip()]

            # A bullet, then any further paragraphs indented under it so they stay part of it.
            lines.append("- " + " ".join(paragraphs[0].split()))

            for paragraph in paragraphs[1:]:
                lines.append("")
                lines.append("  " + " ".join(paragraph.split()))

        lines.append("")

    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    # STDOUT IS REDIRECTED TO A FILE BY THE RELEASE PATH, AND WINDOWS DEFAULTS IT TO THE CONSOLE
    # CODEPAGE. Every document here is written with em dashes and curly quotes in it; under cp1252
    # they come back as replacement characters in the published release notes, which is the kind
    # of fault that is invisible until a user reads it. Say utf-8 rather than hope.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8")

    parser = argparse.ArgumentParser(description="Assemble changelog.d/ into a release section.")
    parser.add_argument("--check", action="store_true",
                        help="validate the fragments and print nothing; exit 1 if any is malformed")
    parser.add_argument("--directory", default=os.path.join(ROOT, "changelog.d"),
                        help="the fragment directory (default: changelog.d/)")
    args = parser.parse_args()

    parsed, problems = fragments(args.directory)

    if problems:
        for problem in problems:
            print(problem, file=sys.stderr)
        print(f"{len(problems)} malformed changelog fragment(s). See changelog.d/README.md.",
              file=sys.stderr)
        return 1

    if args.check:
        print(f"{len(parsed)} changelog fragment(s), all well-formed.", file=sys.stderr)
        return 0

    sys.stdout.write(render(parsed))
    return 0


if __name__ == "__main__":
    sys.exit(main())
