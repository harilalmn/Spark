# ADR-0026 — A `.spark` file's text is written as typed

**Status:** Accepted, 2026-09-11
**Tasks:** `E3-T24`
**Related:** [ADR-0017](0017-spark-file-is-plain-json.md) (the file is plain JSON, so that graphs review like code)

## Context

`SparkFile` writes through `Utf8JsonWriter`, and until this decision it used the writer's default
encoder, `JavaScriptEncoder.Default`. That encoder escapes every character that is dangerous inside
an HTML page — `<`, `>`, `&`, `'`, `+` and the quotation mark — and every character outside ASCII.
The client's re-saved demo files showed what that looks like in practice:

```
"script": "return 1 \u002B 1;"
```

and `\u0022Spark\u0022` for a quoted string. A node titled *Façade → north* was written as
`Fa\u00E7ade \u2192 north`.

ADR-0017 chose plain JSON over a container so that a graph reviews like code — so that a pull
request shows what changed. Code-block text is the text most likely to be read in a diff, and it is
where `+` and quotation marks are commonest. An encoder built for JSON that is pasted into a web
page was working against the reason the format exists.

## Decision

**Write with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`.** `+`, `<`, `>`, `&`, `'` and non-ASCII
characters are written as typed. What JSON itself requires is still escaped: a quotation mark as
`\"`, a backslash as `\\`, and control characters.

**And accept the one-time diff.** A file written before this change reads back to exactly the same
document — every JSON reader accepts both spellings — and takes the new spelling the first time it is
saved. Every file written since re-saves byte for byte, so `E7-T7`'s promise stands: it was always a
promise about a file this build wrote.

**Opening an older file does not mark it modified.** The application's saved snapshot is a fresh
write of the document it opened, not the text on disk, so the diff appears only when somebody saves.

## Alternatives

**Write relaxed only for text that changed.** Rejected. The reader decodes every string, so the
writer cannot know how an unchanged string was spelled unless the original spelling is carried
through `GraphDocument` beside the value — new state on a contract type, for a cosmetic property.
And its result is worse than the diff it avoids: a file would hold two spellings of the same character
indefinitely, and the day somebody edits one string, that line changes spelling as well as content.

**`JavaScriptEncoder.Create(UnicodeRanges.All)`.** Rejected, because it does not do the job: the
built-in encoders escape the HTML-sensitive characters whatever ranges they are allowed, so `+` and
the quotation mark would still be escaped. Only the relaxed encoder leaves them alone.

**Writing every string by hand.** Rejected. It is the only way to write characters beyond the Basic
Multilingual Plane literally — no encoder setting allows them (N150) — and an emoji in a code block
is not worth owning a JSON string writer.

**Keep the default.** Rejected. It is ADR-0017's premise, lost for a reason that does not apply.

## On the word "unsafe"

The relaxed encoder is called unsafe because its output is not safe to paste into an HTML page or a
`<script>` element **without further encoding**. A `.spark` file is never served that way. Anything
that ever does embed one in a web page must encode it for that context itself, which is correct
practice for any text and was never something a file format could guarantee on its behalf.

## Consequences

- A code block reads in a diff as it reads in the editor.
- The first save of an older file containing any of these characters shows a diff of spelling only.
  **No committed file changes**: nothing in `docs/examples/` contained an escaped character.
- A character beyond the Basic Multilingual Plane is still written as an escaped surrogate pair, and
  still round-trips exactly (N150).
