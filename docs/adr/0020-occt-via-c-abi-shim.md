# ADR-0020 — OpenCascade as the solid-modelling kernel, reached through a C-ABI shim we own

**Status:** Accepted
**Date:** 2026-08-28
**Deciders:** Nicety
**Supersedes:** [ADR-0002](0002-own-managed-geometry-kernel.md)

## Context

The client instructed capability parity with Dynamo's geometry, which is now **FR-81** with a
register behind it. Reading `ProtoGeometry.dll` member by member produced a finding that
changes scope rather than describing it:
[DYNAMO-COVERAGE §6.1](../DYNAMO-COVERAGE.md#61-parity-on-solid-and-surface-commits-us-to-exact-solid-modelling)
established that **70 members cannot exist without exact BRep booleans, trimming, filleting
and sewing** — 32 of them directly, a further 38 behind the same seam.

That is precisely the work [ADR-0002](0002-own-managed-geometry-kernel.md) staged last, that
**R1** calls research-grade, and that [PRD §9](../PRD.md#9-out-of-scope) states publicly as
post-1.0. Parity contradicts all three, and §6.1 set out three honest paths: accept the
commitment and build it ourselves, scope parity to the end of 1.x, or take an existing engine
deliberately rather than as a contingency.

**The client chose the third.** This record exists because that choice reverses ADR-0002's
central premise. ADR-0002 rejected OCCT *as the default* on the grounds that native binaries
per RID recreate the deployment shape Spark exists to escape, and it kept an OCCT-backed
optional package as the documented fallback for exactly this risk. The fallback is now the
plan, so the record that called it a fallback has to be superseded rather than annotated.

What ADR-0002 got right survives and is restated below: the objection to Parasolid, ACIS and
C3D was about **licensing and cost**, not capability, and nothing here disturbs it.

## Decision

**Spark's solid-modelling kernel is OpenCascade Technology, reached through a hand-written
C-ABI shim that we own.**

Concretely:

- **The engine is OCCT.** LGPL-2.1 with the Open CASCADE exception; 8.0.1, July 2026;
  upstream CI on Windows, macOS and Ubuntu; a vcpkg port at the current version; shipping
  today under FreeCAD, KiCad, Salome, CadQuery and Macad3D.
- **The binding is `spark_occt`** — a C++ translation unit set in `native/spark_occt/`,
  exporting a flat C ABI, MIT-licensed, written and maintained by us. **We adopt no
  third-party binding.** Estimated **350–500 exported entry points**, touching roughly
  **2–3% of OCCT's class surface** — on the order of 150–220 classes out of some 6,900
  headers. That calibration is not invented: `opencascade-rs`, the closest comparable
  hand-written binding, declares 538 functions.
- **The managed side is `Spark.Geometry.Occt`**, a new assembly that P/Invokes `spark_occt`
  through `LibraryImport` and implements `IBrepKernel`. It is the only assembly permitted to
  observe a native handle.
- **OCCT is built from a pinned source tag via a vcpkg manifest**, not consumed as a NuGet
  package.
- **OCCT ships in the default install.** A Dynamo user who finds booleans greyed out on first
  run is exactly what FR-81 forbids.
- **`Spark.Geometry` itself stays pure managed and independently distributable**, and
  **NFR-5 stands unchanged**: its published output still contains no native binaries, still
  asserted by CI. The native component lives in a different assembly and a different
  directory.

The seam that carries this is not ADR-0003 as written; the residency rule it needs is
[ADR-0021](0021-brep-kernel-residency.md), which amends it.

## Alternatives considered

### The engine

**OpenNURBS / `Rhino3dm`.** MIT, mature, excellent, and the emotionally attractive answer:
it is the library Rhino's own file format is built on and it would carry no licence
obligations at all. It lost on a fact rather than a judgement — it is a **representation and
file-format library**. There are no booleans, no fillet and no trimming in it; Rhino's
modelling lives in commercial RhinoCommon. It solves none of the 70 members. It is worth
keeping as a **post-1.0 `.3dm` interop item**, which is a pure addition and costs this
decision nothing.

**Manifold, and the mesh-boolean libraries generally.** Genuinely excellent at what they do,
and fast. They lost because they produce **no BRep**. Converting a solid to a mesh to
intersect it and back again is a one-way lossy trip: analytic faces are gone, so fillet is
impossible and STEP export is meaningless. That is the position the client has just rejected
— it is mesh booleans with extra steps.

**CGAL.** Comprehensive and correct, with the best-documented exact-predicate work in the
field. It lost twice over: its boolean packages are **GPL-3.0**, which is incompatible with
MIT distribution and with **D5**'s goal of embedding inside commercial CAD add-ins; and it is
not a BRep kernel either, so it would not deliver the members even if the licence allowed it.

**Parasolid, ACIS, C3D.** These solve robustness properly, and robust surface-surface
intersection is exactly what makes them cost what they cost. They lose for the reason
ADR-0002 already gave and that reason is undisturbed: **per-seat royalty licensing is
incompatible with an MIT tool users install freely** — the original problem in a new currency.
ADR-0002's argument here was about openness and cost, never about capability, and this record
does not touch it.

### The binding

**`OcctNet.Wrapper`.** Architecturally right — it is a stable C ABI, which is the shape we
concluded we want. It is disqualified on provenance: its nuspec carries a commit hash **with
no repository URL**, so what is actually on offer is a **174 MB binary blob, MIT by
assertion, from an unnamed author group, with no auditable source**. We could not read it,
could not patch it, and could not contribute a fix upstream because there is no upstream to
contribute to. That is disqualifying independently of how mature the code turns out to be.

**`Occt.NET`.** 338,000 downloads, which is the only argument for it. It declares **no
licence at all**, and no licence grants no rights. A download count measures reach, not
rights.

**SWIG or another generator.** The appeal is obvious: 6,951 headers is a lot to bind by hand.
It loses on the thing that matters most here — **generation cannot reduce the ABI surface, it
can only enlarge it faster.** Macad3D's generated C++/CLI binding is 170 files and 13.35 MB
*for a subset*. Our entire strategy for surviving OCCT upgrades depends on the surface being
small and deliberately chosen, and a generator's output is neither.

**C++/CLI.** Proven at application scale by Macad3D under MIT, and it has the best debugging
story of any option by a distance — one debugger, one call stack, managed and native frames
interleaved. It lost because it is **Windows-only, permanently**. Choosing it would reverse
[ADR-0001](0001-avalonia-not-wpf.md)'s central argument, kill the Linux CI rot-guard for
everything downstream of `Spark.Geometry`, and violate the `-windows`-free rule that
`Spark.Architecture.Tests` currently enforces. The C-ABI shim costs perhaps **15–25% more
effort** and buys back the entire cross-platform option; that trade is not close.

### Consuming OCCT from nuget.org

Considered and rejected on evidence. OCCT is at **8.0.1**, and **every** OCCT package on
nuget.org is stranded at **7.8 or 7.9**. That ecosystem is not merely immature, it is
abandoned in place, and depending on it would mean depending on somebody who has already
stopped. Building from a pinned source tag through a vcpkg manifest is the only option that
lets us choose our own version and our own patch level.

## Consequences

### Positive

**All 70 members become reachable, and R1 retires outright.** That is the largest single risk
reduction available to this project. Exact booleans, trimming, filleting, sewing and shelling
stop being a research programme and become integration work.

**R12 retires with it.** OCCT gives AP203, AP214 and **AP242** with assemblies, names, colours
and units, plus IGES, for free. The managed STEP subset is discarded rather than descoped.

**M6's demoable improves** from *"solids that can be combined"* to *"solids that can be
combined, filleted, shelled, trimmed and exported to STEP"*.

**Cross-platform is preserved.** A C ABI plus P/Invoke runs on every RID. ADR-0001's rationale
stands and **D14** — Windows-only releases — is untouched.

**The index-based BRep decision pays off exactly as designed.** A flat array-of-indices model
is what marshals well across a C ABI, and ADR-0003 said it was chosen partly to keep an OCCT
adapter mechanical. It was, and it is.

### Negative

**Cost, and both halves of it must be stated together.**

**Against the plan as written: +7 to +11 weeks.** A new 2-week OCCT de-risk spike at **M1.6**;
M5 loses a week; **M6 goes from 14 weeks to 20–24**; M8 loses a week. A new epic **E13** of
roughly 24 weeks, most of it landing inside M6.

**Against what the client actually asked for: it saves years, and it retires R1.**

Both are true. The intuitive expectation is *buy rather than build, therefore cheaper*, and it
is wrong here for exactly one reason: **the plan as written never contained the expensive
thing.** M6's 14 weeks bought mesh booleans. Exact booleans, fillet, chamfer and trim were in
the out-of-scope list, post-1.0, possibly never. Parity was never funded, and this is what
funding it looks like — in the cheapest form available.

**The Linux CI job stops being nearly free.** ADR-0001 justified it as a rot-guard *precisely
because it was cheap*, and it must now build native code. The mitigation is to build OCCT once
per RID as a cached artefact keyed on `(occt-tag, vcpkg-baseline, shim-source-hash, rid)`, so
steady-state CI downloads it and builds only the shim, with the from-clean build running
nightly. **Without that, the rot-guard will not survive a busy PR queue.** Note also that
OCCT's own CI has **no ARM64 leg**, and its macOS coverage is x64 only.

**Eight new risks, R15 … R22**, recorded in [PRD §12](../PRD.md#12-risks):

- **R15** — native binary distribution: a per-RID matrix, an installer whose size is
  bracketed at **40–160 MB uncompressed and not yet measured**, code signing, and antivirus
  false positives.
- **R16** — debugging across the boundary. **A boolean that returns a wrong-but-valid shape is
  diagnosable only inside code we do not own.** Mitigated by piping `Message_Report` into
  `SparkDiagnostic`, running `BRepCheck_Analyzer` in Debug builds, and attaching a
  Draw-Harness-compatible `.brep` dump to every failure so a bug reproduces upstream.
- **R17** — version upgrades. The stranded 7.8/7.9 packages on nuget.org are the warning, and
  the small deliberate ABI surface is the mitigation.
- **R18** — OCCT's own numerical failure modes: booleans on tangency, fillet on complex vertex
  blends. `Result<T>` exists for exactly this, and **R3 does not retire — it changes owner.**
- **R19** — process crash. C++ exceptions unwinding into managed frames are undefined
  behaviour. `catch(...)` in every entry point, `OSD::SetSignal(false)`, and the out-of-process
  worker — previously deferred past v1 for R11 — **now serves R11 and R19 together**.
- **R20** — threading, which is unresolved and is a top-three risk.
- **R21** — licence obligations constraining the publish pipeline.
- **R22** — build reproducibility.

**Two build-policy conflicts, both real and both small.** `AllowUnsafeBlocks=false` is
repository policy (**NFR-15**), and the `LibraryImport` source generator emits unsafe code and
requires it true. The resolution is to keep the repository default `false` and opt in for
`Spark.Geometry.Occt` **only**, with a comment naming this ADR, plus an architecture test
asserting it is the only project doing so. Separately,
`SparkGeometryTakesNoThirdPartyDependencyBeyondClipper` stays **exactly as it is** — it needs
a *companion* rule asserting `Spark.Geometry.Occt` is referenced only by composition roots.
Relaxing either test would be the wrong repair.

**Licensing obligations attach to the publish pipeline, permanently.** See the section below.

### Neutral

**For 1.0 there will be exactly one provider.** The `IBrepKernel` seam is retained for
`Result<T>`, `Capabilities` and insurance, not because a second provider is planned. One is
not. **Do not build a second provider to justify the abstraction.**

**Some managed work is discarded as redundant rather than descoped**: the managed STEP subset,
the throwaway SSI spike, the entire managed exact-boolean programme, managed
fillet/chamfer/shell/thicken/draft, managed sew/heal/validate, managed
extrude/revolve/loft/sweep, and managed BRep tessellation.

**Some is reduced but not eliminated, and this must not be over-claimed.** The mesh boolean
work loses its urgency but not its purpose, because **OCCT is poor at mesh booleans** and
Dynamo has them; it defers to 1.x with `Capabilities` greying it. And the **OBJ, STL, PLY and
glTF writers stay ours** — they must work in a build with no native component at all, since
M1's demoable is `spark` writing an OBJ polyline.

## Licensing

**This is not legal advice.** What follows is a description of the licence texts and of the
obligations we intend to meet. Six questions go to counsel and are listed below; two of them
must be answered before M6.

OCCT is **LGPL-2.1 plus the Open CASCADE exception**. The exception exists to address LGPL §5's
C++ header problem: object code that incorporates material from OCCT headers may be
distributed under terms of your choice, **provided prominent notice is given in supporting
documentation**. That is what allows `spark_occt` to be MIT despite compiling against OCCT
headers.

The obligations we plan against:

- **Link dynamically, never statically.**
- **Ship OCCT as unmodified, replaceable shared libraries**, preserving the user's right to
  relink against their own build.
- **No `PublishSingleFile` sealing the natives, and no full NativeAOT of OCCT.** This
  constrains E12-T8.
- **Ship the LGPL text and the exception text.**
- **Give prominent notice** in the About box, the README, the installer and the release notes.
- **Offer source**, via a pinned tag and a recorded vcpkg baseline.
- **Keep any OCCT modification as a numbered patch file**, never as an edited tree.

Spark's own code, `spark_occt` included, stays MIT.

**Six questions for counsel.** The central one is whether a thin shim whose entire purpose is
to expose OCCT is a *work that uses the Library* under the exception, or a derivative work
under §5. Also: whether single-file, trimmed or AOT publishing is compatible with the relink
obligation; whether vcpkg's port declaring `LGPL-2.1-only` — **omitting the exception** —
creates exposure; what "prominent notice in supporting documentation" requires concretely;
what the obligations are for a user who embeds `Spark.Host` in a commercial add-in (**D5**);
and whether the source offer is satisfied by a tag reference or requires a hosted archive.
**Items 1 and 3 must be answered before M6.**

## The positioning problem

This is the consequence with no technical fix, and it is recorded here because it is the one
most likely to be handled badly by default.

Spark exists because Dynamo Sandbox depends on ProtoGeometry and so **forces users to have an
Autodesk product installed**, and because solving that by acquiring a different heavyweight
dependency would *move* the problem rather than remove it. That sentence is in ADR-0002, in
PRD §2 and in the README.

**Spark now acquires a heavyweight native dependency.**

The distinction is real and it is defensible: OCCT is open source, freely redistributable,
installed *with* Spark, and requires no account, no licence purchase and no other vendor's
product. But it only holds **if we say it first, clearly, in our own words.** The failure mode
is leaving it as an M8 documentation task and letting somebody else write the framing for us.

Hence: OCCT ships in the default install, `Spark.Geometry` stays independently distributable
and pure managed, NFR-5 stands unchanged, and **the README says all three in the same
paragraph, in this change and not later.**

## Notes

### M1.6 was taken on 2026-08-31, and this record stands

**`M1.6-C2` was the only criterion that could have reopened this decision, and it passed.** Two
managed `Brep`s go out through `LibraryImport` into `spark_occt_import`, are fused by
`BRepAlgoAPI_Fuse`, and come back as a resident `Brep` whose tessellation measures **42.0** against
arithmetic's 42. The same trip runs in C, in `native/spark_occt/test/smoke.c`, so a failure in one
and not the other says which half is wrong.

**Three of the seven open items below are answered, and the answers are recorded against them
rather than in a paragraph that would be read instead of them.** What is answered: the binary size
(item 1), most of the FreeType question (item 2), and E13-T3's cost (item 7). What is not: STEP
without XCAF, the threading envelope, the counsel question, and `OcctNet.Wrapper`'s repository.

**One estimate in the Decision above is wrong by an order of magnitude and is left standing.** It
says *350–500 exported entry points*; the shim exports about **thirty** and does everything M6
needs. The estimate was right about the work and wrong about the shape — a binding that exposes
OpenCascade *types* needs a call per type per operation, and this one exposes one flat tagged
encoding instead. See [N49](../NOTES.md). It is left standing because a decision record records
what was decided and what was believed at the time, and the belief is part of the record.

**Two things were learnt that the record did not anticipate**, both in [NOTES.md](../NOTES.md):
a shape that meshes correctly has **not** been shown to be correctly oriented, because meshing and
modelling read different things ([N50](../NOTES.md)); and Spark's trims carry no pcurve, so the
importer must compute them, and skipping the loops produces a cylinder with **square caps** that
looks entirely convincing as a mesh ([N51](../NOTES.md)).

### The original notes

**M1.6 is a new 2-week de-risk spike and it gates this record the way M1.5 gates ADR-0001.**
Its job is to build OCCT from a pinned tag via vcpkg on Windows and Linux, drive one boolean
through a minimal shim and `LibraryImport`, and **measure the things nobody has measured**.

**Seven things could not be determined and are recorded as open, not resolved.** Each is
listed with how to find out, because an uncertainty written as an answer is worse than one
written as a gap:

1. ~~**Real binary sizes.**~~ **Answered 2026-08-31: 52.0 MB uncompressed for `win-x64`**, which
   is every OpenCascade DLL staged beside the shim; **28.4 MB** for the fifteen toolkits the shim
   actually links, whose transitive load-time dependencies are not yet verified. Either number
   replaces the 40–160 MB bracket and is well under the 100 MB that would reopen shipping OCCT by
   default.
2. **Whether excluding the Visualization module drops the FreeType dependency.** *Partly
   answered 2026-08-31, by observation rather than by experiment:* the vcpkg port installs
   `opencascade[core,freetype]`, so **FreeType is a default feature of the port** rather than
   something Visualization alone drags in — which makes this a question about the port's features
   as much as about the link. `TKV3d`, `TKOpenGl` and `TKService` are built by the port regardless
   and are simply not linked. *Still to find out:* configure a build with Visualization off and
   inspect the resulting link.
3. **Whether STEP can be used without pulling in XCAF.** *Find out:* attempt a
   `STEPControl`-only read and write at M1.6 and see what the linker demands.
4. **OCCT's real thread-safety envelope.** Documented guidance is thin and R20 depends on it.
   *Find out:* stress the shim from the parallel evaluator's thread count and read the
   upstream source for the specific packages we call.
5. **The counsel question** — the shim's status under the exception. *Find out:* ask counsel;
   this cannot be settled by reading more.
6. **Whether `OcctNet.Wrapper` has a source repository at all.** We could not find one from
   the nuspec. *Find out:* ask the publisher. It does not change the decision — an unauditable
   dependency was rejected on that ground — but it should not be recorded as a certainty.
7. **E13-T3's real cost** — shape lifetime, the handle table and the native memory budget. It
   is bracketed at 2–4 weeks and nobody has built it. *Find out:* M1.6 produces the first
   honest estimate.

**What would reopen this record.** A counsel answer that makes the shim a derivative work
under §5 would force a choice between relicensing `spark_occt` and abandoning the approach,
and that is a different decision, not an amendment to this one. Nothing else here should
reopen it — in particular, discovering that OCCT is hard to debug or slow to upgrade is a
cost this record has already accepted and named.
