---
id: reference.occt-packaging
title: How other projects package, ship and attribute OpenCascade
related: [concepts.geometry-basics]
since: "0.1"
---

**Status:** Reference. A survey of observed practice, gathered to shrink the questions going to
counsel rather than to answer them.
**Owner:** `platform`
**Last updated:** 2026-08-28

> **This document is not legal advice.** It records what other projects were observed to do,
> with a path or a URL against every claim. Practice is evidence of what is customary. It is
> not evidence of what is correct, and several of the projects surveyed here appear to be
> doing less than their licence obligations require.

---

## 1. What this is, and what it is not

[ADR-0020](../adr/0020-occt-via-c-abi-shim.md) adopts OpenCascade Technology as Spark's
solid-modelling kernel and sends **six questions to counsel**. The client's instruction was
sensible: before deriving a packaging posture from first principles, look at what shipping
projects already do. This document is that look.

**It is a survey of practice, and nothing more.**

- **It is evidence, not cover.** Knowing that four projects put their OCCT DLLs loose in the
  install directory tells us that doing so is unremarkable. It does not tell us that doing so
  discharges the obligation, and it certainly does not transfer anybody else's legal risk onto
  our own decision.
- **Following prior art does not make an approach correct.** Two of the projects examined here
  ship OCCT binaries with **no LGPL text and no exception notice anywhere in the artefact**.
  That is the practice; it is also, on the face of the licence, a shortfall. Copying it would
  be copying a defect.
- **None of this is legal advice.** It is a reading of files in public repositories by an
  engineer, and it should be handed to counsel as *input* rather than presented as a
  conclusion.

### The distinction that shapes the whole survey

**Most well-known OCCT consumers are themselves copyleft, which makes their position easier
than Spark's and their practice weaker evidence for us.**

FreeCAD is LGPL-2.1 and KiCad is GPL-3.0. For both, the awkward questions simply do not arise:
their own licence is at least as strong as OCCT's, so the combined work is governed by their
licence anyway and there is no tension to resolve. KiCad says this out loud in
[`LICENSE.README`](https://github.com/KiCad/kicad-source-mirror/blob/master/LICENSE.README):

> These licenses are compatible, but a combined works as is will be governed under the terms
> of the GPLv3 (or later). This includes any binary distribution of the KiCad EDA suite by the
> KiCad project or any third party, e.g. Linux distributor.

Spark cannot say that sentence, because Spark is MIT. So FreeCAD and KiCad are surveyed here
for their **practical mechanics** — how the files sit, how the installer is built, where the
notice appears — and not for their licence posture.

The genuinely comparable projects are **permissively licensed applications and libraries that
ship OCCT binaries**:

| Project | Licence | Why it matters here |
|---|---|---|
| **Macad3D** | MIT | C#, .NET, WPF, Windows installer, a hand-maintained OCCT binding, actively developed. **This is Spark's exact situation.** |
| **CadQuery / OCP** | Apache-2.0 | Ships OCCT two different ways — conda and PyPI wheels — and the two ways reach opposite answers on the same obligations. Instructive precisely because it is inconsistent. |
| **replicad** | MIT at the root | Solves the permissive-product/copyleft-payload problem by **splitting the package boundary**, which is the most directly transferable idea in this document. |
| **TiGL** (DLR) | Apache-2.0 | An institutional research library that publishes its OCCT patches as patch files. |

Weight those four. Read FreeCAD and KiCad for mechanics only.

---

## 2. Per-project findings

### 2.1 Macad3D — MIT, C#/.NET, Windows installer

Repository: <https://github.com/Macad3D/Macad3D>. MIT, `License.txt`, copyright
"2015-2025 Tobias Schachte". User guide in a second repository,
<https://github.com/Macad3D/UserGuide>, also MIT.

**This is the highest-value target in the survey and it repays close reading.**

#### File layout and publish mode

The decisive file is
[`Build/MSBuild/Macad.Publish.props`](https://github.com/Macad3D/Macad3D/blob/main/Build/MSBuild/Macad.Publish.props),
quoted in full because it settles Spark's installer question on its own:

```xml
<PublishDir>$(MMRootDir)Bin\Publish</PublishDir>
<PublishProtocol>FileSystem</PublishProtocol>
<PublishSelfContained>False</PublishSelfContained>
<PublishSingleFile>False</PublishSingleFile>
<PublishReadyToRun>True</PublishReadyToRun>
<AppHostDotNetSearch>AppRelative</AppHostDotNetSearch>
<AppHostRelativeDotNet>DotNetRuntime</AppHostRelativeDotNet>
```

`PublishSingleFile` is **explicitly `False`**, not merely absent. The .NET runtime is copied
into a `DotNetRuntime` subdirectory and found there by an app-relative host search — so
Macad3D gets the benefits usually reached for with self-contained single-file publishing, and
gets them without sealing anything into a bundle.

The OCCT binaries reach the output directory as ordinary loose files. From
[`Build/Nuget/Macad.ThirdParty.Occt.props`](https://github.com/Macad3D/Macad3D/blob/main/Build/Nuget/Macad.ThirdParty.Occt.props):

```xml
<ItemGroup>
  <CopyToOutput Include="$(FreeTypeBinPath)\freetype.dll" />
  <CopyToOutput Include="$(TbbBinPath)\tbb12.dll" />
  <CopyToOutput Include="$(TbbBinPath)\tbbmalloc.dll" />
  <CopyToOutput Include="$(OcctBinPath)\*" />
</ItemGroup>
```

`Build/MSBuild/Macad.Publish.targets` then promotes every `CopyToOutput` item into the publish
directory with `TargetPath` set to the bare filename, so the OCCT DLLs land flat beside
`Macad.exe`. **They are not renamed, not repacked and not compressed at rest.**

#### Static versus dynamic linking

Dynamic, and the evidence is structural rather than stated. `Macad.ThirdParty.Occt.props` adds
`native\opencascade\win-x64\lib` to `AdditionalLibraryDirectories` — that directory contains
`TK*.lib` import libraries — while the matching `bin` directory of `TK*.dll` files is copied to
the output. Import library plus shipped DLL is dynamic linking. I found **no statement anywhere
in the repository or user guide that says "we link dynamically"**, and no evidence of a static
build.

#### Attribution and notices — the part worth copying

Macad3D's About box is
[`Source/Macad/Window/Auxiliary/AboutDialog.xaml`](https://github.com/Macad3D/Macad3D/blob/main/Source/Macad/Window/Auxiliary/AboutDialog.xaml).
The two lines that carry the OCCT notice are:

```xml
<TextBlock Margin="0,20,0,0"
           Text="{Binding OcctVersion, StringFormat={}Uses Open CASCADE Technology {0}}" />
<TextBlock>and other open source products.</TextBlock>

<TextBlock Margin="0,10,0,0">
    <Hyperlink Click="_ShowLicense_Click">
        License terms and 3rd party licenses
    </Hyperlink>
</TextBlock>
```

So the rendered About box reads, on a line of its own:

> **Uses Open CASCADE Technology 7.9.2**
> and other open source products.
> *License terms and 3rd party licenses*

`OcctVersion` is read from the library itself at run time —
`Occt.Helper.Version.Major/Minor/Maintenance/Development` in
`AboutDialog.xaml.cs` — so the notice cannot drift away from the binary actually loaded. That
is a small, good idea and Spark should take it.

The wording is close to the exception's own language. The exception, quoted verbatim from
[`OCCT_LGPL_EXCEPTION.txt`](https://github.com/Open-Cascade-SAS/OCCT/blob/master/OCCT_LGPL_EXCEPTION.txt),
requires that you

> give prominent notice in supporting documentation to this code that it makes use of or is
> based on facilities provided by the Open CASCADE Technology software.

"Uses Open CASCADE Technology 7.9.2" is a plain-English rendering of "makes use of … facilities
provided by the Open CASCADE Technology software". **Seeing how a shipping MIT product satisfies
that phrase is the single most useful output of this research.**

#### Where the licence texts actually live — and a gap

The `License terms and 3rd party licenses` hyperlink does **not** open a local file. From
[`Source/Macad/Commands/AppCommands.cs`](https://github.com/Macad3D/Macad3D/blob/main/Source/Macad/Commands/AppCommands.cs):

```csharp
public static ActionCommand<string> ShowHelpTopic { get; } = new(
    (topicId) =>
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var url = $"https://macad3d.net/userguide/go/?version={version.Major}.{version.Minor}&guid={topicId}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    })
```

The topic GUID passed from the About box is `6d66b830-344b-4400-a50c-ba31459287d9`, which is the
`uid` of
[`docs/License/License.md`](https://github.com/Macad3D/UserGuide/blob/main/docs/License/License.md)
in the UserGuide repository. Fetching
`https://macad3d.net/userguide/go/?version=4.0&guid=6d66b830-344b-4400-a50c-ba31459287d9`
resolves to a live "Licenses" page with the structure that file describes. Its OCCT section is,
verbatim:

```markdown
### Open CASCADE Technology
Copyright (c) OPEN CASCADE SAS
https://dev.opencascade.org
[!code-text[](Occt.txt)]
[!code-text[](OcctException.txt)]
```

`docs/License/Occt.txt` is 24,802 bytes — the LGPL-2.1 text — and `docs/License/OcctException.txt`
is 676 bytes and contains the Open CASCADE exception in full. So the complete licence and the
complete exception are published, inlined into one page, alongside FreeType and Intel TBB
attributions that both explicitly say "OpenCASCADE Technology uses …".

**The gap:** those texts appear to be online only. The installer's file list is generated by
[`Build/Deploy.csx`](https://github.com/Macad3D/Macad3D/blob/main/Build/Deploy.csx), whose `app`
file set admits only

```csharp
Files = new List<string> { "*.exe", "*.dll", "*.xml", "*.json" }
```

from `Bin\Publish`. No `.txt`. Meanwhile the `dotnetruntime` set copies `**\*.*`, and
`Macad.Publish.targets` deliberately copies Microsoft's `LICENSE.TXT` and
`THIRD-PARTY-NOTICES.TXT` into that folder — so **Microsoft's notices are installed on disk and
OCCT's are not**. The OCCT licence reaches the user only if they click through to a web page.

I did not extract an installed tree to confirm this, so treat it as a reading of the build
scripts rather than a verified observation of the shipped product. **Spark should not copy this
part.** Shipping the two licence text files costs 26 KB.

#### Installer construction

[`Build/Setup/MacadSetup.iss`](https://github.com/Macad3D/Macad3D/blob/main/Build/Setup/MacadSetup.iss),
Inno Setup 6.7.1 (pinned in `Build/WebPackages.config`). A plain file-copy install:

```
DefaultDirName={autopf64}\{#MyAppName}
Compression=lzma
InternalCompressLevel=max
SolidCompression=True
```

`Compression=lzma` and `SolidCompression=True` compress the *installer payload*; the user still
ends up with ordinary, individually replaceable `.dll` files in
`C:\Program Files\Macad3D`. The `[Files]` section is generated by `Deploy.csx` into
`_GeneratedDefinitions.iss` as one `Source:`/`DestDir:` line per file.

**There is no `LicenseFile=` directive in `[Setup]`, so the installer shows no licence page at
all.** Notable, given that FreeCAD does show one.

#### Which OCCT modules they ship

Determinable exactly, and this is the best module-trimming evidence in the survey.
[`Build/_ThirdParty_Occt.csx`](https://github.com/Macad3D/Macad3D/blob/main/Build/_ThirdParty_Occt.csx)
lists them:

```csharp
static readonly string[] _OcctToolkits = new string[]
{
    "TKernel", "TKMath",
    "TKG2d", "TKG3d", "TKGeomBase", "TKBRep", "TKMesh",
    "TKGeomAlgo", "TKTopAlgo", "TKPrim", "TKBO", "TKFeat", "TKShHealing", "TKFillet", "TKOffset", "TKBool",
    "TKService", "TKV3d", "TKOpenGl",
};

static readonly string[] _OcctPartialToolkits = new string[]
{
    "TKXSBase", "TKDE", "TKLCAF", "TKXCAF", "TKVCAF", "TKCAF", "TKCDF", "TKDEIGES", "TKDESTEP",
    "TKHLR",
};
```

**Twenty-nine toolkits**, plus exactly three third-party DLLs — `freetype.dll`, `tbb12.dll`,
`tbbmalloc.dll`. The build script *refuses* to package if FreeImage, FFmpeg or OpenVR were
enabled:

> "Packaging OCCT is only supported with dependencies Freetype and TBB. Please disable all
> other and rebuild OCCT."

OCCT arrives as a prebuilt NuGet package `Macad.ThirdParty.Occt` **7.9.2**
(`Directory.Packages.props`) served from the project's own feed `https://nuget.macad3d.net`
(`nuget.config`). Its nuspec template sets `<license type="file">native\opencascade\LICENSE_LGPL_21.txt</license>`
and the generator copies both `LICENSE_LGPL_21.txt` and `OCCT_LGPL_EXCEPTION.txt` into the
package — so the licence and exception *are* correctly attached at the package boundary, even
though they fall off at the installer boundary.

#### Source offer, and modifications

The README's "About OpenCASCADE Technology" section is the whole of it:

> The restore script downloads a pre-built version of OpenCASCADE Technology (OCCT) so that the
> project can be built immediately. This package contains only the parts that are used in this
> project. The complete distribution can be cloned from the
> [OCCT github repo](https://github.com/Open-Cascade-SAS/OCCT). This allows to use additional
> parts, build the library with other build options or to make code changes. The currently used
> version can be found in the about dialog.
>
> To use an own build of OCCT, you need to configure the path to OCCT build directory using the
> following script console command:
>
>     > occt config <pathToOcct>

**That last paragraph is the relink story, written as a build instruction.** Macad3D documents
how to substitute your own OCCT build, which is a practical demonstration of the freedom LGPL
§6 is trying to preserve. It is a URL plus a version-in-the-About-box rather than a formal
written offer or a hosted archive.

I found **no evidence that Macad3D patches OCCT**. There is no patch directory, and
`_ThirdParty_Occt.csx` consumes a CMake build tree without modifying sources.

#### How it reconciles MIT with LGPL binaries

Directly, and by separation rather than by argument. `License.txt` at the root is a clean MIT
grant covering Macad3D's own code and says nothing about third parties. The OCCT binaries are
identified as third-party in the About box, and the licence detail is delegated to a
"3rd Party Licenses" section that keeps each library's terms under that library's own heading.
The MIT text is never claimed to cover OCCT, and OCCT's terms are never claimed to cover
Macad3D. **The two are simply kept in separate boxes**, which is the whole technique.

---

### 2.2 CadQuery and OCP — Apache-2.0, and two answers to the same question

Repositories: <https://github.com/CadQuery/cadquery> (Apache-2.0),
<https://github.com/CadQuery/OCP> (Apache-2.0, `LICENSE` is the Apache-2.0 text).

CadQuery is interesting because it ships OCCT through **two channels that behave completely
differently**, and only one of them looks defensible.

#### Channel one: conda. Clean separation.

[`OCP/conda/meta.yaml`](https://github.com/CadQuery/OCP/blob/master/conda/meta.yaml) declares
OCCT as an ordinary runtime dependency:

```yaml
requirements:
  build:
    - python {{ environ.get('PYTHON_VERSION') }}
    - occt={{ OCCT_VER }}=all*
  run:
    - python {{ environ.get('PYTHON_VERSION') }}
    - occt={{ OCCT_VER }}=all*
```

`cadquery/conda/meta.yaml` in turn depends on `ocp=7.9.3.1`. So on the conda route OCCT is a
**separate package with its own licence metadata**, installed as separate replaceable shared
libraries into the environment. The `conda-forge/occt` package metadata records
`license: LGPL-2.1-only` and `license_file: LICENSE_LGPL_21.txt`
([`recipe/meta.yaml`](https://github.com/conda-forge/occt-feedstock/blob/main/recipe/meta.yaml)),
so the LGPL text is installed with it. Nothing is bundled, nothing is renamed, and a user can
replace the `occt` package independently. **This is the good version.**

Two details from that same recipe are worth carrying forward. It builds from a pinned upstream
tag —

```yaml
url: https://github.com/Open-Cascade-SAS/OCCT/archive/refs/tags/V{{ version.replace(".", "_") }}.tar.gz
sha256: 0d6913eae4bcc09a3653ceced6dda1aec11c35a1513d4c06762c9b002092c68a
patches:
  - patches/switch-vtk-freetype-cmake-order.patch
```

— which is a pinned tag *plus a SHA-256* as the source offer, and it **does patch OCCT**, with
the patch published as a file in the feedstock. And its SPDX field says `LGPL-2.1-only`,
**omitting the exception**, which is exactly the metadata problem ADR-0020 flags for vcpkg.

#### Channel two: the PyPI wheel. Bundled, renamed, and unattributed.

I downloaded `cadquery_ocp-7.9.3.1.1-cp312-cp312-win_amd64.whl` (46,557,938 bytes) from PyPI and
read its contents directly. What is inside:

- **159,353,490 bytes uncompressed** across 731 entries.
- `OCP/OCP.cp312-win_amd64.pyd` — **94,209,024 bytes**. The binding is larger than the kernel.
- A `cadquery_ocp.libs/` directory holding **71 DLLs totalling 65,080,048 bytes**, of which
  **47 are OCCT `TK*` toolkits totalling 54,680,576 bytes**.
- `cadquery_ocp/LICENSE` — 11,346 bytes, and it is **the Apache-2.0 text**.

**The OCCT DLLs are renamed.** They appear as, for example:

```
cadquery_ocp.libs/TKDESTEP-3b6cbd266d70018d74de66353afc4421.dll
cadquery_ocp.libs/TKGeomAlgo-550728fcf92cbddabda6e6451ed83842.dll
cadquery_ocp.libs/TKernel-7f537ebf992290773c03ef7f618dd6bb.dll
```

`cadquery_ocp-7.9.3.1.1.dist-info/DELVEWHEEL` records the tool that did it — `delvewheel repair`
version 1.12.1 — and `OCP/__init__.py` carries the injected loader shim:

```python
"""""" # start delvewheel patch
def _delvewheel_patch_1_12_1():
    import os
    if os.path.isdir(libs_dir := os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir, 'cadquery_ocp.libs'))):
        os.add_dll_directory(libs_dir)
```

delvewheel does not merely copy the DLLs; it mangles their filenames with a content hash and
patches the importing binaries' import tables to match. Dropping a replacement `TKernel.dll`
into that directory would not be loaded.

**The wheel's `METADATA` declares `License: Apache-2.0`.** I searched the whole archive: there
is no LGPL text, no `OCCT_LGPL_EXCEPTION.txt`, and no notice naming Open CASCADE beyond the
summary line "Python wrapper for Open CASCADE Technology 3D geometry library". The
[CadQuery README](https://github.com/CadQuery/cadquery/blob/master/README.md) `## License`
section says only:

> CadQuery is licensed under the terms of the
> [Apache Public License, version 2.0](http://www.apache.org/licenses/LICENSE-2.0).

**This is a practice to record and not to imitate.** An Apache-2.0-labelled artefact containing
47 renamed LGPL shared libraries, shipped with no LGPL text and no exception notice, is a long
way from "prominent notice in supporting documentation" and a long way from preserving the
relink freedom. It is reported here because it is real, widely installed, and it shows how
easily the obligation is lost at the packaging step rather than at the design step.

#### Measured sizes — the most useful numbers in this document

Because the wheel is a plain ZIP, every uncompressed size is exact. For a **full** OCCT 7.9.3
Windows x64 release build:

| Set | Count | Uncompressed bytes | MiB |
|---|---|---|---|
| OCCT `TK*` toolkits | 47 | 54,680,576 | 52.1 |
| Third-party DLLs (FreeImage, OpenEXR, freetype, libraw, tiff, jpeg, zstd, lcms2, webp, msvcp140 …) | 24 | 10,399,472 | 9.9 |
| Binding (`OCP.pyd`, pybind11 over the whole API) | 1 | 94,209,024 | 89.8 |

The full toolkit list shipped: `TKBO, TKBRep, TKBin, TKBinL, TKBinTObj, TKBinXCAF, TKBool,
TKCAF, TKCDF, TKDE, TKDEGLTF, TKDEIGES, TKDESTEP, TKDESTL, TKDEVRML, TKExpress, TKFeat,
TKFillet, TKG2d, TKG3d, TKGeomAlgo, TKGeomBase, TKHLR, TKIVtk, TKLCAF, TKMath, TKMesh,
TKMeshVS, TKOffset, TKOpenGl, TKPrim, TKRWMesh, TKService, TKShHealing, TKStd, TKStdL, TKTObj,
TKTopAlgo, TKV3d, TKVCAF, TKXCAF, TKXSBase, TKXml, TKXmlL, TKXmlTObj, TKXmlXCAF, TKernel`.

**Now the finding that changes Spark's plan.** Taking those per-toolkit sizes and summing only
the twenty-nine toolkits Macad3D ships gives **50,079,232 bytes, 47.8 MiB — against 52.1 MiB for
all forty-seven.** Trimming OCCT from 47 toolkits to 29 saves roughly **8%**.

That is a derived figure, not a measurement of Macad3D's own binaries, and the two builds differ
in version and configuration — so treat it as an order of magnitude. But the shape of it is
robust: **the weight is concentrated in the toolkits nobody can drop.** `TKDESTEP` alone is
5,988,352 bytes; `TKGeomAlgo`, `TKGeomBase` and `TKBool` are 4.3, 4.1 and 3.7 MB. The real
savings are in the *optional third-party* dependencies — FreeImage, OpenEXR, libraw, tiff, jpeg
and friends account for most of the 9.9 MiB and are all switchable off at configure time.

**Implication for Spark:** the 40–160 MB bracket in R15 is too wide at the top. A dynamically
linked OCCT payload on `win-x64` is realistically **55–70 MB uncompressed** before the .NET
runtime, the shim and the application, and module trimming is not the lever that moves it.

Other measured points for calibration:

- `cadquery-ocp` 7.9.3.1.1 wheels: 46.3 MB (win_amd64), 67.8 MB (manylinux x86_64),
  67.5 MB (macOS x86_64), 62.6 MB (macOS arm64) — all compressed.
- `conda-forge` `occt` 8.0.1 `win-64`: 25,373,321 bytes for the `all` variant and 25,185,358
  bytes for `novtk`, compressed `.conda`.
- **Macad|3D 4.4's Windows installer is 111,118,276 bytes** — a measured `Content-Length` on
  `https://macad3d.net/download/Macad3D_4.4_Setup.exe`. That is LZMA solid-compressed and
  includes the .NET 10 runtime, WPF, twenty-nine OCCT toolkits and sample models. It is the
  closest thing in this survey to "what a Spark installer will weigh". I did not extract it, so
  the uncompressed split between OCCT and everything else is **not determined**.

---

### 2.3 replicad — MIT product, LGPL package boundary

Repository: <https://github.com/sgenoud/replicad>. Root `LICENSE` is MIT, "Copyright 2023
QuaroTech Sàrl".

replicad is a browser CAD library, so its file-layout mechanics do not transfer. **Its package
structure does, and it is the cleanest solution to the permissive-product problem in this
survey.**

The monorepo has a package `packages/replicad-opencascadejs` whose
[`package.json`](https://github.com/sgenoud/replicad/blob/main/packages/replicad-opencascadejs/package.json)
declares:

```json
{
  "name": "replicad-opencascadejs",
  "description": "OpencascadeJS custom build for replicad",
  "license": "LGPL-2.1-only",
  ...
}
```

and ships its own `LICENSE` — 26,526 bytes, the LGPL-2.1 text — listed explicitly in the
package's `files` array so it cannot be dropped by the publisher.

**So the MIT product and the LGPL payload are different packages with different declared
licences and different licence files.** The consumer-facing library `replicad` stays MIT; the
artefact that actually contains OCCT is honestly labelled LGPL-2.1-only and carries the LGPL
text with it. Nobody has to argue that MIT somehow covers OCCT, because nothing ever claimed it
did.

They also use a **custom trimmed OCCT build** — the build scripts invoke
`ghcr.io/taucad/opencascade.js` with `custom_build_single.yml` / `custom_build_multi.yml` — so
module selection is deliberate. I did not measure the resulting WASM sizes.

The README says only "As an abstraction over opencascade", with no licence notice, so the
**notice** side of replicad's practice is weak. It is the **boundary** that is worth taking.

---

### 2.4 TiGL — Apache-2.0, DLR, and published OCCT patches

Repository: <https://github.com/DLR-SC/tigl>. `LICENSE.txt` is the Apache-2.0 text with no
appended third-party section.

The whole of TiGL's OCCT attribution is one sentence in
[`README.md`](https://github.com/DLR-SC/tigl/blob/main/README.md):

> The TiGL library uses the OpenCASCADE CAD kernel to represent the airplane geometry by NURBS
> surfaces.

That is it. There is no NOTICE file, no LGPL text in the repository, and I could not find an
About dialog or credits screen in the TiGLCreator sources — so I record TiGL's notice practice
as **README sentence only**, and its handling of the LGPL text as **not determined** for the
binary distributions on the `dlr-sc` conda channel, which I did not download.

What TiGL does contribute is the **modification** answer. It carries a top-level `patches/`
directory of unified diffs against the kernel:

```
patches/oce-0.15/feature-coons_c2.patch          32,126 bytes
patches/oce-0.15/fix-bspline_step_import.patch    8,341
patches/oce-0.15/fix_bop_bug.patch                2,515
patches/oce-0.17/feature-coons_c2.patch          15,937
patches/oce-0.17/fix-fonts.patch                 14,685
patches/oce-0.17/fix-geomapi_extremacurvecurve.patch 8,315
...
```

These target OCE, the older OpenCASCADE Community Edition, and may well be stale — but the
**shape** is exactly what ADR-0020 proposes for Spark: named patch files in the consuming
repository, organised by upstream version, never an edited vendor tree.

---

### 2.5 FreeCAD — LGPL-2.1. Mechanics only.

Repository: <https://github.com/FreeCAD/FreeCAD>. Root `LICENSE` is the LGPL-2.1 text
(27,030 bytes).

**Installer.** NSIS. `package/WindowsInstaller/FreeCAD-installer.nsi` with
`package/WindowsInstaller/Settings.nsh` setting `SetCompressor /SOLID lzma`. It is a plain
recursive file copy — from `setup/install.nsh`:

```nsis
SetOutPath "$INSTDIR\bin"
# recursively copy all files under bin
File /r "${FILES_FREECAD}\bin\*.*"
```

OCCT's `TK*.dll` files therefore land loose in `$INSTDIR\bin` alongside Qt, Python and
everything else. `package/WindowsInstaller/Delete.bat` — which prunes debug artefacts before
packaging — deletes things like `freetyped.dll` and `Coin4d.dll` by name, which confirms these
are ordinary side-by-side DLLs.

**Licence page.** Unlike Macad3D, FreeCAD *does* show one. `include/gui.nsh`:

```nsis
# Show the license.
!define MUI_LICENSEPAGE_BUTTON $(^NextBtn)
!insertmacro MUI_PAGE_LICENSE "${FILES_LICENSE}"
```

and `package/WindowsInstaller/LICENSE` (26,526 bytes) plus `License.rtf` (26,144 bytes) are both
the LGPL-2.1 text. Note what that means: the licence the installer displays is FreeCAD's own
LGPL, which happens also to be OCCT's licence. The coincidence is why FreeCAD never has to
resolve the question Spark has to resolve.

**Attribution.** The About dialog (`src/Gui/Dialogs/DlgAbout.cpp`) builds a *License* tab from
`LICENSE.html` in the help directory and a *Libraries* tab from `ThirdPartyLibraries.html`:

```cpp
QString baseurl = QStringLiteral("file:///%1/ThirdPartyLibraries.html")
                      .arg(QString::fromStdString(App::Application::getHelpDir()));
textField->setSource(QUrl(baseurl));
```

The source of that page is
[`src/Doc/ThirdPartyLibraries.html.cmake`](https://github.com/FreeCAD/FreeCAD/blob/main/src/Doc/ThirdPartyLibraries.html.cmake),
and the OCCT row is, in full:

```html
<th align = 'left' > <a href = 'https://opencascade.com/open-cascade-technology' > Open CASCADE </a> </th>
<td> <code> ${OCC_VERSION_STRING} </code> </td>
```

**A name, a link and a version substituted at build time. No licence name, no LGPL statement,
no exception text on that page.** The LGPL text lives in the adjacent License tab, undifferentiated
from FreeCAD's own. This is materially *less* than Macad3D does, and it is defensible only
because FreeCAD is itself LGPL.

**Modules and linking.** Not determined from the repository. FreeCAD builds against whatever
OCCT the platform LibPack or conda environment provides, and I found no toolkit selection list.

---

### 2.6 KiCad — GPL-3.0. Mechanics only, and a warning about metadata.

Repository mirror: <https://github.com/KiCad/kicad-source-mirror>.

**How OCCT arrives.** Through vcpkg. `vcpkg.json` at the root:

```json
{
  "name": "opencascade",
  "features": [ "rapidjson" ]
}
```

and `vcpkg-configuration.json` pins it to KiCad's own overlay registry
(`https://gitlab.com/kicad/packaging/kicad-vcpkg-registry.git`) at a named baseline. That is a
reproducible source pin, and it is close to what ADR-0020 plans for Spark.

**Attribution.** This is the striking part. KiCad's OCCT attribution is one line in
`common/build_version.cpp`:

```cpp
aMsg << indent4 << "OCC: " << OCC_VERSION_COMPLETE << eol;
```

— a version string in the copyable version-info block. I searched
`common/dialog_about/AboutDialog_main.cpp` (1,248 lines) for `OpenCASCADE`, `OCCT`, `OCC_VERSION`
and `OCE`: **no matches.** OpenCASCADE is not credited in KiCad's About dialog. And
`LICENSE.README`, quoted at the top of this document, enumerates only the licences of code
vendored under `thirdparty/` — **OpenCASCADE does not appear in it at all**, because it is an
external linked library rather than vendored source.

So KiCad's answer to "prominent notice" is a version number in a diagnostics blob. Under
GPL-3.0 that costs them nothing. **Under MIT it would be the weakest posture in this survey.**

---

### 2.7 The vcpkg port itself — because Spark will consume it

Spark builds OCCT from a pinned tag via vcpkg, so the port's own behaviour is part of Spark's
packaging story.
[`ports/opencascade/vcpkg.json`](https://github.com/microsoft/vcpkg/blob/master/ports/opencascade/vcpkg.json)
at version 8.0.1 declares:

```json
"license": "LGPL-2.1-only",
```

**The exception is omitted from the SPDX metadata**, exactly as ADR-0020 suspected. But the
picture is better than the metadata suggests, and this materially narrows that counsel question.
The last statement in
[`portfile.cmake`](https://github.com/microsoft/vcpkg/blob/master/ports/opencascade/portfile.cmake)
is:

```cmake
vcpkg_install_copyright(
    FILE_LIST
        "${SOURCE_PATH}/LICENSE_LGPL_21.txt"
        "${SOURCE_PATH}/OCCT_LGPL_EXCEPTION.txt"
)
```

**vcpkg installs both texts** — the LGPL *and* the exception — into the package's copyright
file. The omission is confined to the machine-readable SPDX field, which cannot express
"LGPL-2.1 plus a bespoke exception" in the first place. Spark inherits both files at
`installed/<triplet>/share/opencascade/copyright` and can copy them straight into the payload.

Three further facts from that portfile that Spark's build must account for:

1. **The port supports static linkage and will silently do it.**
   `if (VCPKG_LIBRARY_LINKAGE STREQUAL "dynamic") set(BUILD_TYPE "Shared") else() set(BUILD_TYPE "Static")`.
   Since ADR-0020 forbids static linking, **the triplet is a licence control, not just a build
   setting**, and it should be asserted in CI rather than assumed.
2. **The port already patches OCCT**, with five numbered patches applied in order:
   `0001-cmake-keep-build-use-vcpkg-explicit.patch`,
   `0002-cmake-load-exported-package-dependencies.patch`,
   `0003-image-remove-freeimage-msvc-autolink.patch`,
   `0004-cmake-add-additional-path-extraction-for-OpenCASCADE.patch`,
   `0005-drop-bin-letter.patch`.
   So Spark's honest answer to "do you modify OCCT?" is **"yes, by way of vcpkg's published
   patch series"**, and the source offer must name the vcpkg baseline as well as the OCCT tag —
   which is what R22's cache key
   `(occt-tag, vcpkg-baseline, shim-source-hash, rid)` already records.
3. **It already trims a module**: `-DBUILD_MODULE_Draw=OFF`.

The port pins upstream by tag and SHA-512:

```cmake
vcpkg_from_github(
    REPO Open-Cascade-SAS/OCCT
    REF "${VERSION_STR}"
    SHA512 bbe7099071cbf5397940ebc6e66ec05f8023d5e5aae6142870e14b93aa6f8f94c30980ef421e717f0fbfbc23b3520c3ccfe8a939c4caba3ccbf325060e26eb52
```

which is a stronger source pin than a bare tag reference, since a retagged upstream would fail
the hash.

---

### 2.8 One negative result worth recording

`occt-import-js` (<https://github.com/kovacsv/occt-import-js>, 284 stars) compiles OCCT to
WebAssembly and is the import path behind the MIT-licensed Online 3D Viewer. It looked like a
permissive project statically linking OCCT. **It is not permissive**: the repository's own
licence is LGPL-2.1, and `LICENSE.md` is 26,526 bytes — the LGPL text.

A project that links OCCT into a single indivisible artefact chose to license itself LGPL. That
is one data point, not a rule, and it did not come with a stated rationale. It is recorded here
because it points the same way as replicad's package split: **where the OCCT code cannot be
separated from yours, projects tend to let the copyleft licence follow it.**

---

## 3. Comparison across the eight questions

| | **Macad3D** (MIT) | **CadQuery conda** (Apache-2.0) | **CadQuery PyPI wheel** (Apache-2.0) | **replicad** (MIT) | **TiGL** (Apache-2.0) | **FreeCAD** (LGPL-2.1) | **KiCad** (GPL-3.0) |
|---|---|---|---|---|---|---|---|
| **1. File layout** | Loose `TK*.dll` flat beside `Macad.exe` in `C:\Program Files\Macad3D`; unmodified, unrenamed | Separate `occt` conda package; own libs, own licence file | Bundled in `cadquery_ocp.libs/`; **renamed with content hashes** by delvewheel; import tables patched | OCCT is a **separate npm package** with its own LGPL licence | Not determined for binary distributions | Loose `TK*.dll` in `$INSTDIR\bin` via `File /r` | Loose DLLs from a vcpkg build |
| **2. Static vs dynamic** | Dynamic — import libs + shipped DLLs. **Never stated in words** | Dynamic (separate package) | Dynamic but effectively unreplaceable once renamed | Statically linked into WASM by construction | Not determined | Dynamic | Dynamic; vcpkg triplet decides |
| **3. Attribution** | **About box: "Uses Open CASCADE Technology 7.9.2" + link to a licence page carrying the LGPL and the exception in full** | conda `license_file: LICENSE_LGPL_21.txt` | **None.** METADATA says `License: Apache-2.0`; no LGPL, no exception, README silent | LGPL text shipped in the OCCT sub-package; root README mentions "opencascade" only | One README sentence: "uses the OpenCASCADE CAD kernel" | About → Libraries tab: name, link, version. LGPL text in a separate License tab | **One line, `OCC: <version>`, in the version-info blob.** Absent from the About dialog and from `LICENSE.README` |
| **4. Source offer** | README link to the OCCT GitHub repo + version in About + documented `occt config <path>` to substitute your own build | Pinned tag + SHA-256 in the feedstock recipe | Not present in the artefact | Docker image tag for the OCCT build | Not determined | Not determined | vcpkg registry + baseline commit |
| **5. Modifications** | None found | **Yes** — `patches/switch-vtk-freetype-cmake-order.patch` in the feedstock | Inherits the conda build | Custom trimmed build config (`custom_build_*.yml`) | **Yes** — `patches/oce-0.15/`, `patches/oce-0.17/` unified diffs in-repo | Not determined | Inherits vcpkg's five numbered patches |
| **6. Installer** | Inno Setup, plain file copy, `Compression=lzma` + `SolidCompression`. **`PublishSingleFile=False` set explicitly.** **No licence page** | n/a | n/a | n/a | n/a | NSIS, `File /r`, `SetCompressor /SOLID lzma`. **Shows an LGPL licence page** | n/a in-repo |
| **7. Modules shipped** | **29 toolkits**, listed in `_ThirdParty_Occt.csx`; only FreeType + TBB as third-party deps | Full build; `all` and `novtk` variants | **47 toolkits**, 52.1 MiB; 24 third-party DLLs, 9.9 MiB | Custom trimmed WASM build; sizes not measured | Not determined | Not determined | vcpkg default + `rapidjson`; `Draw` off |
| **8. MIT/permissive reconciliation** | Root MIT covers own code only; OCCT identified as third party in About; licences kept in per-library sections | conda dependency boundary does the work | **Not reconciled.** LGPL binaries inside an Apache-2.0-labelled wheel | **Package boundary does the work** — LGPL payload is a separately licensed package | Apache-2.0 file with no third-party section | n/a — LGPL itself | n/a — GPL-3.0 itself |

**Nobody in this survey uses single-file packaging for a desktop OCCT application.** Macad3D
disables it explicitly; FreeCAD copies a directory tree. The only artefacts that seal OCCT into
something indivisible are the WASM builds — where it is unavoidable — and the PyPI wheel, which
does not seal it so much as make it unreplaceable by renaming.

---

## 4. The proposed Spark packaging checklist

Written so an engineer can implement it and a lawyer can review it. **It is a proposal shaped by
observed practice, not a compliance opinion, and it must not ship without counsel's review of
the items in §5.**

### 4.1 Where files sit

1. **OCCT ships as unmodified, individually replaceable shared libraries**, in their upstream
   filenames — `TKernel.dll`, `TKMath.dll`, `TKBRep.dll` and so on. **No renaming, no hashing
   into the filename, no repacking, no embedding as a resource.** delvewheel-style mangling is
   forbidden by name.
2. On Windows they sit **flat in the application directory** beside `spark.exe`, as Macad3D and
   FreeCAD both do. On Linux and macOS they sit in the same directory as the shim, reached by
   `$ORIGIN` / `@loader_path` rather than by an absolute path baked at build time.
3. `spark_occt` — Spark's own MIT-licensed shim — sits beside them as a separate binary. It is
   never statically merged with OCCT.
4. The install is a **plain file copy**. Installer-level compression of the payload is fine
   (Macad3D uses LZMA solid; FreeCAD uses `/SOLID lzma`); what matters is that the user's disk
   ends up holding real, individually replaceable files.

### 4.2 What is forbidden

5. **No `PublishSingleFile`.** Set `<PublishSingleFile>False</PublishSingleFile>` **explicitly**
   in the publish props, as Macad3D does, so that it reads as a decision rather than a default,
   and add a comment naming ADR-0020. Applies to E12-T8.
6. **No NativeAOT of anything that links OCCT**, and no `IncludeNativeLibrariesForSelfExtract`.
7. **No static linking.** vcpkg will build OCCT statically if the triplet says so, so
   `x64-windows` / `x64-linux-dynamic` and equivalents are a **licence control**. Add a CI
   assertion that the built OCCT artefacts are shared libraries and that no `TK*.lib` static
   archive is linked into `spark_occt`.
8. **No edited OCCT tree.** Any change is a numbered patch file under `native/patches/occt/`,
   applied at build time, following vcpkg's `0001-…`/`0002-…` convention.
9. **No claim, anywhere, that Spark's MIT licence covers OCCT.** MIT covers Spark's code
   including `spark_occt`; OCCT is third-party and carries its own terms.

### 4.3 What notices appear, and where

The exception's operative words are "prominent notice in supporting documentation". Macad3D
satisfies it in the About box; the aim below is to be **unambiguously more thorough than the
most thorough project in this survey**, because the cost of doing so is a few kilobytes and one
dialog line.

10. **About box.** A permanently visible line, not behind a tab:
    `Uses Open CASCADE Technology <version>` — with `<version>` read **from the loaded library at
    run time**, as Macad3D does, so it can never drift from the binary actually present. Beside
    it, a link to the local `THIRD-PARTY-NOTICES.md`.
11. **`THIRD-PARTY-NOTICES.md` in the repository root and in the install directory**, listing
    OCCT with its version, its copyright holder, its licence, the exception, and the source URL.
    Draft text in §6.
12. **Licence texts on disk in the install tree**, not merely online. Ship both
    `LICENSE_LGPL_21.txt` and `OCCT_LGPL_EXCEPTION.txt` unmodified, under a `licences/`
    subdirectory. vcpkg already places both at
    `installed/<triplet>/share/opencascade/copyright`, so the build can copy them rather than
    vendoring a second copy. **This is where Macad3D falls short and Spark should not.**
13. **README.** A short paragraph in the same place that explains the positioning point ADR-0020
    insists on — that OCCT is open source, freely redistributable, installed with Spark, and
    requires no account and no other vendor's product. Draft text in §6.
14. **Installer.** A licence page (FreeCAD shows one; Macad3D does not). It should display
    Spark's MIT licence *and* name OCCT with its licence, with the full texts installed to
    `licences/`.
15. **Release notes.** Every release names the OCCT version it ships.
16. **The reproducibility record.** Write `(occt-tag, vcpkg-baseline, shim-source-hash, rid)`
    from R22 into the About box and into the build output, so the source offer in §4.4 can be
    honoured against a *specific* artefact rather than approximately.

### 4.4 What the source offer says

17. State the pinned upstream tag and the vcpkg baseline commit, and link to both. A tag plus a
    hash is stronger than a tag alone — the conda-forge recipe pins a SHA-256 and the vcpkg port
    pins a SHA-512, and Spark inherits the latter for free.
18. List the patches applied, including vcpkg's five, and link to them.
19. **Document how to substitute your own OCCT build.** This is Macad3D's best idea after the
    About line: their README explains `occt config <pathToOcct>`. Spark's equivalent is a
    documented statement that replacing the `TK*` shared libraries in the install directory with
    an ABI-compatible build of the same OCCT version is supported and expected — which is the
    relink freedom, demonstrated rather than asserted.
20. Whether a tag reference suffices or a hosted archive is required is **a question for
    counsel**, not for this document. See §5.

### 4.5 Build configuration

21. **Trim third-party dependencies before trimming toolkits.** The measurement in §2.2 says
    module trimming buys about 8% while the optional image and video libraries account for
    ~10 MiB. Configure with FreeImage, FFmpeg, OpenVR and VTK **off** — Macad3D's build script
    refuses to package if any of them is on, which is a good hard gate to copy.
22. Ship the toolkit set the shim actually needs. Macad3D's twenty-nine are a reasonable
    starting list for a modelling application with STEP and IGES. Confirm by linking, not by
    guessing — this is an M1.6 measurement task.
23. **Weigh the artefacts at M1.6** and replace R15's 40–160 MB bracket with a measured number.
    §2.2 suggests the answer will land near the bottom of that bracket, not the middle.

---

## 5. What this research settles, and what it does not

**Practice can tell us what is customary. It cannot tell us what is correct.** Two projects in
this survey — CadQuery's PyPI wheel and, less severely, KiCad — appear to do materially less
than the licence text asks, and both are widely used without visible consequence. That is worth
knowing and it is not worth relying on.

### It narrows these

**The publish-mode question** (is single-file, trimmed or AOT publishing compatible with the
relink obligation?) — **narrowed, and close to settled on the engineering side.** No surveyed
desktop application uses single-file packaging with OCCT. Macad3D sets
`<PublishSingleFile>False</PublishSingleFile>` explicitly while still getting a private runtime
via `AppHostRelativeDotNet`, which proves the ergonomics Spark wants are reachable without it.
The only artefacts that make OCCT unreplaceable are the WASM builds and the delvewheel-renamed
wheel, and the wheel demonstrates the failure mode precisely: **renaming a library defeats
relinking just as thoroughly as bundling it.** That converts a legal question into a design rule
Spark can adopt now. It does not tell us whether single-file publishing would have been
*permissible*.

**The vcpkg-metadata question** (does the port declaring `LGPL-2.1-only` and omitting the
exception create exposure?) — **substantially narrowed.** The metadata omission is real and
confirmed, but `portfile.cmake` calls `vcpkg_install_copyright` with **both**
`LICENSE_LGPL_21.txt` and `OCCT_LGPL_EXCEPTION.txt`, so the exception text is installed and
available to be shipped. conda-forge does the same thing in the same way — SPDX field
`LGPL-2.1-only`, `license_file: LICENSE_LGPL_21.txt` — which suggests the omission is an
artefact of SPDX's inability to express a bespoke exception rather than a considered position by
either packager. The question narrows from "are we exposed by our build tool?" to the much
smaller "does a third party's incomplete SPDX string bind us, when we ship the complete text
ourselves?"

**The "prominent notice" question** — **narrowed, though not answered.** We now have a spectrum
of real behaviour, from KiCad's version string in a diagnostics blob at one end to Macad3D's
first-class About line plus a full published licence page at the other. Macad3D is the only MIT
project observed doing it properly, and its wording tracks the exception's own phrasing. **What
counsel now has to rule on is a concrete proposal (§4.3, §6) rather than an open-ended question.**

**The modifications question** — **effectively answered by practice.** Numbered patch files in
the consuming repository is what vcpkg does, what conda-forge does, and what TiGL does. ADR-0020
already proposes it. There is no competing practice.

### It leaves these untouched

**The shim question — whether `spark_occt`, whose entire purpose is to expose OCCT, is a "work
that uses the Library" under the exception or a derivative work under §5.** This is the central
question and **nothing in this survey touches it.** Macad3D's binding is C++/CLI compiled against
OCCT headers and shipped MIT, and CadQuery's OCP is pybind11 compiled against OCCT headers and
shipped Apache-2.0 — so both projects behave *as though* the answer is favourable. Neither
states a rationale, neither cites advice, and **neither has been tested.** Two projects assuming
something is not evidence that it is true. ADR-0020 was right: this cannot be settled by reading
more.

**The embedder question — what obligations attach to a user who embeds `Spark.Host` in a
commercial closed-source add-in (D5).** No project in this survey has an analogous
redistribution story. Macad3D is an application; CadQuery and TiGL are libraries whose consumers
are themselves open source. **Nothing found.**

**The source-offer question — whether a tag reference suffices or a hosted archive is required.**
Every project surveyed uses a URL or a tag, and none hosts an archive. But every project
surveyed is also distributing through channels (GitHub, conda-forge, PyPI) where the upstream
source is trivially reachable, which may be doing the work. **Practice is uniform here but its
uniformity may be an accident of context, so it should not be read as reassurance.**

### One more thing this settles, which was not a legal question at all

**R15's 40–160 MB bracket is too wide at the top.** A full OCCT Windows x64 release build is
52.1 MiB of toolkits and 9.9 MiB of optional third-party libraries, measured. Macad3D's
twenty-nine-toolkit subset is ~48 MiB by the same per-toolkit figures. Module trimming saves
about 8%; turning off FreeImage and friends saves more. Spark's OCCT payload should be planned
at **55–70 MB uncompressed on `win-x64`**, and Macad|3D's complete 111 MB installer — .NET
runtime, WPF, OCCT and samples — is the best available whole-product comparator.

---

## 6. Draft notice text for Spark

**Proposed wording, for counsel to review and amend. Not approved, and not legal advice.**

### 6.1 About box

The permanently visible lines, modelled on Macad3D's:

```
Spark <version>
Copyright (c) <year> Nicety — MIT licence

Uses Open CASCADE Technology <occt-version>
and other open source software.

Third-party licences · Build <occt-tag>/<vcpkg-baseline>/<shim-hash>
```

`<occt-version>` is queried from the loaded library at run time, never hard-coded.
"Third-party licences" opens the local `THIRD-PARTY-NOTICES.md`; the build triple is
selectable text so a user can quote it in a bug report and so the source offer can be honoured
against a specific artefact.

### 6.2 README

Placed with the installation section, and deliberately making ADR-0020's positioning point in
the same paragraph:

> **Spark's solid-modelling kernel is Open CASCADE Technology (OCCT), and it is installed with
> Spark.** Spark makes use of facilities provided by the Open CASCADE Technology software.
> OCCT is free and open-source software, distributed by Open Cascade SAS under the GNU Lesser
> General Public License version 2.1 with the Open CASCADE exception. It costs nothing, needs no
> account, no licence purchase and no other vendor's product, and it ships in Spark's default
> install so that nothing is greyed out on first run. Spark's own code, including the
> `spark_occt` binding, is MIT-licensed; OCCT is not Spark's code and is not covered by Spark's
> licence.
>
> OCCT is linked dynamically and shipped as unmodified shared libraries beside the application,
> so you may replace them with your own build of the same version. The exact version, upstream
> tag and build baseline are shown in Help → About, and the full licence texts are installed in
> the `licences` folder. See `THIRD-PARTY-NOTICES.md`.
>
> `Spark.Geometry` itself remains pure managed and contains no native binaries; the OCCT-backed
> kernel lives in a separate assembly.

### 6.3 `THIRD-PARTY-NOTICES.md`

```markdown
# Third-party notices

Spark is distributed under the MIT licence. Spark's own source code, including the
`spark_occt` binding, is MIT-licensed. This file lists third-party software distributed
with Spark, together with the terms that apply to it. Spark's MIT licence does not apply
to any of the software listed below.

---

## Open CASCADE Technology (OCCT)

**Spark makes use of facilities provided by the Open CASCADE Technology software.**

- **Version shipped:** <occt-version>
- **Copyright:** © Open Cascade SAS
- **Licence:** GNU Lesser General Public License version 2.1, with the Open CASCADE
  exception (version 1.0) to GNU LGPL version 2.1.
- **Licence texts:** [`licences/LICENSE_LGPL_21.txt`](licences/LICENSE_LGPL_21.txt) and
  [`licences/OCCT_LGPL_EXCEPTION.txt`](licences/OCCT_LGPL_EXCEPTION.txt), both reproduced
  unmodified as published by Open Cascade SAS.
- **Home page:** <https://dev.opencascade.org>
- **Source:** <https://github.com/Open-Cascade-SAS/OCCT>, tag `<occt-tag>`.

### How OCCT is built and shipped

OCCT is **linked dynamically**. Its shared libraries are shipped **unmodified**, under their
upstream filenames, as separate files in Spark's installation directory. They are not
statically linked, not renamed, not bundled into a single-file executable and not embedded in
any other binary. You are free to replace them with your own build of the same OCCT version,
and Spark is built and packaged so that doing so works.

### Obtaining the source

The exact OCCT source Spark was built from is the upstream repository above at tag
`<occt-tag>`. It is built through the vcpkg port `opencascade` at baseline
`<vcpkg-baseline>`, which applies the patches published in that port; the port and its patches
are at <https://github.com/microsoft/vcpkg/tree/<vcpkg-baseline>/ports/opencascade>.
Spark applies no further patches of its own. [Amend this sentence if that ever ceases to be
true, and list Spark's own patches from `native/patches/occt/` here.]

The tag, the baseline and the shim commit for the build you are running are shown in
Help → About.

---

## FreeType

Open CASCADE Technology uses FreeType. Copyright © The FreeType Project
(<https://www.freetype.org>). All rights reserved. Licence text:
[`licences/FTL.TXT`](licences/FTL.TXT).

## Intel oneTBB

[Include only if TBB is enabled in Spark's OCCT build.] Open CASCADE Technology uses Intel
oneAPI Threading Building Blocks, © Intel Corporation, Apache License 2.0. Licence text:
[`licences/LICENSE_TBB.txt`](licences/LICENSE_TBB.txt).

## Clipper2

[Existing dependency of `Spark.Geometry`; entry to be completed.]

## .NET runtime

Microsoft .NET, © Microsoft Corporation, MIT licence. Microsoft's own `LICENSE.TXT` and
`THIRD-PARTY-NOTICES.TXT` are installed alongside the runtime.
```

Two notes on the drafting.

**The phrase "makes use of facilities provided by the Open CASCADE Technology software" is
deliberately lifted verbatim from the exception.** The exception asks for prominent notice "that
it makes use of or is based on facilities provided by the Open CASCADE Technology software", and
using its own words removes any argument about whether a paraphrase was sufficient. Macad3D's
"Uses Open CASCADE Technology" is the short form of the same sentence; the About box uses the
short form, this file uses the long one.

**The "How OCCT is built and shipped" section has no counterpart in any project surveyed.** It is
proposed because every constraint ADR-0020 accepts — dynamic linking, unmodified libraries, no
single-file, replaceability — is invisible to a user unless it is written down, and writing it
down is what turns an engineering decision into the "supporting documentation" the exception asks
for. **Whether it is necessary or sufficient is a question for counsel.**

---

> **A final restatement, because it matters more than anything above.** Every finding here is a
> description of what somebody else did. Not one of them is a ruling on what Spark may do.
> **This document is not legal advice.**
