# Writing a Spark help topic

**Owner:** `docs-author` · **Last updated:** 2026-09-01 · `E10-T4`, `E10-T6`

This is the guide for anyone adding or editing a topic under `docs/help/concepts/`. It lives here
rather than inside `docs/help/` on purpose: everything under that folder is end-user help, the
help window lists all of it, and both harnesses check it as topics. A guide for contributors is
none of those things. It exists
because eleven topics already agreed on a shape nobody had written down, and **a convention every
file happens to follow is one the twelfth file breaks without anybody noticing**.

Everything here is enforced. `HelpTopicSchemaTests` checks the shape, `DocumentationSampleTests`
compiles the samples, `ExampleGraphTests` runs the example graphs, and `NodeTopicCoverageTests`
checks node coverage in both directions. If you get it wrong the build tells you which file and
what about it.

---

## 1. The front matter

Five keys, all required, in this order:

```yaml
---
id: concepts.undo
title: Undo and redo
nodes: []
related: [concepts.files, concepts.lacing]
since: "0.1"
---
```

| Key | What it is |
|---|---|
| `id` | `concepts.` plus the file name without its extension. The help library keys on this, and a reader navigates by file, so the two must agree. |
| `title` | What the topic is called in the help window's list. Sentence case. |
| `nodes` | The node keys this topic documents, as `Package/Name`. Empty is fine and common — most concept topics explain an idea rather than a node. **Every name here must exist**, which is what catches a rename. |
| `related` | Other topic ids, for the reader who has arrived at nearly the right page. **Every entry must name a topic that exists**, and a topic may not list itself. |
| `since` | The version the topic first described something real. A string, quoted. |

Then three provenance lines, immediately after the front matter:

```markdown
**Status:** Current. Describes the undo stack in the running application.
**Owner:** `ui-shell`
**Last updated:** 2026-08-30
```

**`Status:` is one of two words and the second has to be earned.**

- **`Current`** — the code exists and this page describes it.
- **`Specification`** — this page was written *before* the code, and the code is written to match
  it. It is a promise that somebody will come back.

That promise is easy to forget. Two topics carried `Specification` for months after their code
shipped, saying *written before the engine exists* about an engine that had existed since M2. **When
the code lands, the status changes, and changing it means re-reading the page against the code** —
not editing one line.

---

## 2. What a topic owes its reader

**A worked example, and the harness requires one.** Not a sentence saying an example would be
possible. One of: a fenced code block, a pipe table of inputs and results, a section whose heading
contains *example*, or a link to a `.spark` file in `docs/examples/`.

**Samples are compiled, so they have to be real.** Every ` ```csharp ` fence is compiled against
the same references a code-block node gets. Two samples were caught wrong by hand before the
harness existed and both read as perfectly plausible; one of them called a method that has never
existed. Write the sample, then let the build tell you.

Two kinds of sample are compiled two different ways, and it matters which you are writing:

- A sample in `code-blocks.md` **is** a code block — bare identifiers become input ports, and it is
  compiled through `ScriptNodeFactory` exactly as the application would.
- A sample anywhere else is ordinary C#. It carries its own `using` lines, which are hoisted.

**Say what is not covered.** Most topics end with a short *What this does not cover* section
pointing at the topic that does. A reader who has landed on nearly the right page is the reader
most worth helping.

---

## 3. Style

**Write for somebody who is stuck**, not for somebody browsing. They have a graph in front of them
that is not doing what they expected.

- **Lead with the answer.** The explanation goes underneath it.
- **Say what the software does, not what it is for.** *Frozen nodes are skipped when the graph
  runs* beats *freezing lets you manage performance*.
- **Give the reason when the behaviour is surprising.** A reader who understands why a rule exists
  stops fighting it. When Spark refuses to do something, the topic should say what it would have
  cost to allow.
- **Use the product's own words.** If the button says *Collapse to node*, the topic says
  *Collapse to node*.
- **Numbers, not adjectives.** *1.2 ms at 2 000 nodes*, not *fast*.

---

## 4. Before you commit

```
dotnet build Spark.slnx -warnaserror
./tests/Spark.UI.Tests/bin/Debug/net10.0/Spark.UI.Tests.exe
./tests/Spark.Docs.Verify/bin/Debug/net10.0/Spark.Docs.Verify.exe
```

The first compiles the samples. The second checks the schema, the node coverage both ways, and the
example graphs. The third checks these documents against the repository.

---

## 5. What is generated, and must not be written by hand

**Node reference pages and diagnostic pages do not live here.** They are generated at runtime from
the live node library and the live diagnostic codes — `NodeReference` and `DiagnosticReference` —
so a node that arrives in a package has a page the moment it loads, and a node that does not exist
has no page.

**`DocGenerator.cs` is explicitly not ported.** It was 1,478 hand-maintained entries that drifted
until 101 of 108 public constructors rendered blank. If you find yourself about to write a page per
node by hand, that is the thing you are about to rebuild.

What a node page says comes from **XML doc comments on the node's own method** — `<summary>`,
`<param>` and `<returns>`. Improving a node's documentation means editing the C#, not the Markdown.
