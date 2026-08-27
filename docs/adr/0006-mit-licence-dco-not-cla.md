# ADR-0006 — MIT licence, DCO rather than a CLA

**Status:** Accepted
**Date:** 2026-08-27
**Deciders:** Nicety

## Context

Spark is an open-source alternative to a product that ships as part of a commercial
ecosystem, and its adoption story depends on two things that are both licence-sensitive.
Third-party node packages are the contribution path that needs no kernel expertise, which is
the mitigation for the scope-versus-capacity risk; those packages are ordinary NuGet packages
loaded in-process into collectible load contexts. And M8 proves `Spark.Host` inside a real
Revit or AutoCAD add-in, which means Spark assemblies loaded into a proprietary application
alongside proprietary code.

The project is also directed by a single maintainer. Whatever the contribution mechanism is,
it has to work without an administrative apparatus behind it.

## Decision

Spark is MIT-licensed. Contributions are accepted under the Developer Certificate of Origin,
signed off per commit, with the process documented in `CONTRIBUTING.md` at M0. There is no
Contributor Licence Agreement.

## Alternatives considered

### GPL or AGPL

Copyleft is the strongest available protection against a CAD vendor taking the kernel,
improving it privately and shipping it as a closed product — which, given what Spark is
positioned against, is not a hypothetical concern. It lost because it is incompatible with
both adoption paths. A node package is compiled against `Spark.Api` and `Spark.Geometry` and
loaded into the same process, and embedding puts Spark inside a closed-source add-in
deliberately. Under a copyleft core, both of those become legal questions rather than
technical ones, and a licence that requires a lawyer before someone can try the extensibility
story will simply not be tried.

### LGPL

Designed for exactly the linking case, and it keeps the kernel improvements flowing back.
It lost on residual uncertainty rather than on the letter of the licence. The relinking
obligation sits awkwardly with self-contained single-file R2R publishing, and the boundary
between "uses the library" and "derived from the library" is genuinely unclear for a node
package that subclasses our types and is generated into our library by reflection. Uncertainty
alone is enough to deter an embedder, and deterring embedders defeats D5.

### A Contributor Licence Agreement

A CLA gives the project the ability to relicense later — to move to a foundation, to dual-license,
or to correct a licensing mistake — and gives stronger written assurance about provenance
than a sign-off line does. It lost because it puts a signature step in front of a one-line
typo fix, which measurably reduces drive-by contributions, and because it requires a legal
entity for contributors to assign to, which a single-maintainer project does not have. The
DCO gives the provenance assertion that actually matters here with `git commit -s` and no
paperwork.

## Consequences

### Positive

Anyone can use, embed, fork or sell work built on Spark without asking, which is the
condition for the node-package ecosystem and for CAD embedding to happen at all. Contributing
requires a sign-off line and nothing else. MIT is the licence most .NET and NuGet consumers
expect, so it raises no questions during procurement.

### Negative

Nothing prevents a commercial fork, including one that takes the kernel and gives nothing
back. Without a CLA the project cannot relicense without contacting every contributor, so the
licence choice is effectively permanent once contributions arrive. DCO sign-off is also
enforced socially or by a CI check rather than by a signed document, which is a weaker
provenance record if it is ever tested.

### Neutral

MIT constrains what may be vendored: Clipper2's C# distribution is Boost-licensed and
compatible, and any future dependency has to clear the same bar. That is a routine check, not
a burden.

## Notes

This is the ADR in the set whose reasoning is most reconstructed rather than recorded — the
plan states the choice and the DCO-not-CLA preference but not the argument. The reconstruction
above follows from the extensibility and embedding goals stated elsewhere in the plan. Revisit
only if the project acquires a legal entity behind it or if a commercial fork causes concrete
harm, and note that by then relicensing needs every contributor's agreement.
