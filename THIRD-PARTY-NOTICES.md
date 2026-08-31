# Third-party notices

Spark links software written by other people. This file names all of it, says what licence each
piece is under, and says what that obliges us to do. **It is generated against a specific build**:
the exact versions a given installation carries are in `spark_occt.buildkey.json`, written beside
the native libraries by `scripts/build-native.ps1`.

**Nothing in this file is legal advice.** Six questions are with counsel — see **Q13** in
[docs/PRD.md](docs/PRD.md#14-open-questions) — and the most important of them is whether
`spark_occt` is a *work that uses the Library* under the Open CASCADE exception or a derivative
work under LGPL §5. What is written here is what the obligations are understood to be and what the
build actually does about them.

**Last updated:** 2026-08-31

---

## OpenCascade Technology

| | |
|---|---|
| **What it is** | The solid-modelling kernel: exact BRep booleans, filleting, shelling, sewing, healing, tessellation, STEP and IGES |
| **Version** | 8.0.1, built from the `opencascade` vcpkg port |
| **Licence** | **LGPL-2.1-only, with the Open CASCADE exception.** Texts in [`licences/`](licences/) |
| **Where it comes from** | https://github.com/Open-Cascade-SAS/OCCT |
| **How Spark uses it** | Dynamically linked from `spark_occt.dll`, a shim we wrote. The OpenCascade libraries ship **unmodified** and **replaceable**, beside the application |

### The notice the exception requires

> **This software makes use of facilities provided by the Open CASCADE Technology software.**

That sentence is not decoration. The Open CASCADE exception permits distributing object code that
incorporates material from OpenCascade's header files under terms of our choosing **provided that
you give prominent notice in supporting documentation to this code that it makes use of or is based
on facilities provided by the Open CASCADE Technology software**. This file, the README, the
`concepts.solids` help topic and `spark --version` all carry it.

### What that obliges, and where the build meets it

**These are obligations of the *pipeline*, not of anybody's memory.** `E13-T16` exists because a
licence condition that depends on somebody remembering it at release time is a condition that will
eventually be missed.

| Obligation | How it is met |
|---|---|
| **Link dynamically, never statically** | `native/spark_occt/CMakeLists.txt` links the OpenCascade toolkits as shared libraries; `spark_occt.dll` imports fifteen of them and the closure is thirty-three |
| **Ship the libraries unmodified and replaceable** | `scripts/build-native.ps1` copies the port's DLLs beside `spark_occt.dll` and modifies none of them. A user may replace any of them with their own build of the same version |
| **No `PublishSingleFile` sealing the natives, and no NativeAOT over OpenCascade** | Constrains `E12-T8`. A single-file bundle that extracts to a temp directory does not obviously preserve the right to relink, and NativeAOT does not preserve it at all |
| **Prominent notice in supporting documentation** | This file, [README.md](README.md)'s build section, `docs/help/concepts/solids.md`, and the application's About box (`E12-T18`) |
| **A standing source offer, honourable against a specific artefact** | `spark_occt.buildkey.json`, written beside the DLLs at build time, records `(occt-version, vcpkg-baseline, shim-source-hash, rid)`. The offer is satisfiable *against that key* rather than approximately — see **R22** |
| **Any modification kept as a numbered patch** | There are none. The vcpkg port applies its own five patches, which are part of the port and are recorded in the baseline the key names |

### The source offer

The corresponding source for the OpenCascade libraries Spark ships is the upstream tag the vcpkg
port pins, which the build key names exactly. The vcpkg port's own patches are in the vcpkg
repository at the baseline commit the key records. `spark_occt` itself is MIT and is in this
repository, at the commit the key's `shim-source-hash` identifies.

---

## The rest

| Component | Licence | Notes |
|---|---|---|
| **Avalonia** | MIT | The UI framework, and the Skia and HarfBuzz natives it brings |
| **AvaloniaEdit** | MIT | The code-block editor |
| **Dock.Avalonia** | MIT | Docking |
| **Roslyn** (`Microsoft.CodeAnalysis.*`) | MIT | Code-block compilation and completion |
| **xUnit v3** | Apache-2.0 | Tests only; not shipped |
| **CsCheck** | MIT | Tests only; not shipped |
| **BenchmarkDotNet** | MIT | Benchmarks only; not shipped |
| **MinVer** | Apache-2.0 | Build only; not shipped |
| **FreeType, libpng, zlib, bzip2, brotli** | Their own permissive licences | Reached through OpenCascade rather than directly. FreeType arrives whether or not Spark asks for it — see [NOTES.md N53](docs/NOTES.md) |

**`Spark.Geometry` itself carries none of this.** It is pure managed, has no native component, and
is checked by `scripts/check-no-native-binaries.sh` on every CI build — **NFR-5**, which ADR-0020
left standing precisely so that the geometry kernel stays independently distributable no matter
what the application links.
