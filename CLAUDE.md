# Spark — read this before doing anything

This project is in **marathon development**: a long, deliberately resumable run towards 1.0,
worked in small verified steps that survive an interrupted session.

## If you are starting a session, do these four things first, in order

1. **Read [docs/JOURNAL.md](docs/JOURNAL.md), the *Current state* section.** It is the single
   source of truth for where the work stopped. It is written **before** each step begins, not
   only after one ends, so it is accurate even when the session died mid-step.
2. **Reconcile it against the repository**, because the journal describes intent and git
   describes fact:
   ```
   git status --short --branch
   git log --oneline -5
   ```
   A dirty tree means the last step was interrupted part-way. `git diff` shows exactly how far
   it got. **Trust the tree over the journal** when they disagree, and say so in the log entry.
3. **Run the gates** before adding anything, so you find out whether you inherited a clean tree
   or a broken one. The commands and their known quirks are in
   [AGENTS.md](AGENTS.md#before-you-commit).
4. **Do what *Next action* says.** It is one concrete sentence, written by the previous session
   specifically for you.

## The loop, once you are going

Journal first, then work, then verify, then document, then commit. The protocol is written out
in [docs/JOURNAL.md](docs/JOURNAL.md#the-protocol) and it is not optional — it is the only thing
that makes an abrupt stop cheap.

**Never leave the journal describing a step you have already finished, and never start a step
the journal does not mention.**

## Everything else

[AGENTS.md](AGENTS.md) is the working agreement: the documentation standing instruction, the
gates, and the traps. [docs/TODO.md](docs/TODO.md) is priority order,
[docs/TASKS.md](docs/TASKS.md) is the full register, [docs/EPICS.md](docs/EPICS.md) is the
context and [docs/PRD.md](docs/PRD.md) is the reasoning. The journal points at the specific rows
it is working; it does not replace them.
