# Writing and publishing a Spark package

**Owner:** `scripting` · **Last updated:** 2026-09-09 · `E7-T1`, `E7-T2`, `E7-T22`, `E7-T23`

This is the guide for anyone building a node library and putting it on nuget.org. It lives here
rather than under `docs/help/` for the same reason [HELP-AUTHORING.md](HELP-AUTHORING.md) does:
everything in that folder is end-user help and the help window lists all of it, and a guide for
package authors is not that.

**There is no tooling for any of this yet.** No `dotnet new` template, no `spark pack` command, no
sample project in this repository. Every step below is something you do by hand with the ordinary
.NET SDK, and that is worth knowing before you start rather than after.

---

## 1. What makes a package a Spark package

Two things, and both are required — `SparkPackageManifest` refuses a package missing either:

| | What it is | Why |
|---|---|---|
| The **`spark` tag** | An ordinary NuGet tag on the package | So a search on nuget.org finds it, and so a human reading the listing knows what it is |
| **`tools/spark.json`** | A small manifest inside the package | So the loader knows which assemblies to read nodes from |

A tag with no manifest is a package claiming to be something it has not said how to load. A
manifest with no tag is a package nobody will find.

**NuGet is the registry and this file is the only thing added to it.** Protocol, hosting, auth,
SemVer, dependency resolution, private feeds and nuget.org's reach all come free by being an
ordinary NuGet package. The one thing NuGet cannot express is *which assemblies in here are node
libraries*, and that is this file's entire job.

---

## 2. The manifest

`tools/spark.json`, inside the package:

```json
{
  "schema": 1,
  "assemblies": ["Acme.SparkNodes"],
  "displayName": "Acme Nodes",
  "description": "Panelisation and setting-out nodes."
}
```

| Key | Required | Meaning |
|---|---|---|
| `schema` | No — defaults to `1` | The manifest version you wrote against. A **newer** schema than the installed Spark understands is refused rather than guessed at |
| `assemblies` | **Yes** | The assemblies to read node definitions from, **by simple name**, in the order given. A manifest naming none is refused |
| `displayName` | No | What the library panel calls the package. Defaults to the package id |
| `description` | No | One sentence |

**`assemblies` is named rather than discovered, and that is the point.** Your `lib` folder also
holds everything you depend on. Reflecting over all of it would import nodes from libraries whose
authors never intended them — every public static method in a maths helper would become a canvas
node. You say which of your assemblies are node libraries; only those are read.

**Unknown keys are ignored rather than refused**, so a package built against a later Spark still
installs into an earlier one if its assemblies are compatible.

---

## 3. What becomes a node

**Reflection, with no attributes required.** Public static methods on public types become nodes;
the XML doc comments become the descriptions. You do not implement an interface, register anything,
or write a plugin entry point.

The attributes only *refine* what reflection already found:

| Attribute | Does |
|---|---|
| `[SparkNode]` | Overrides the display `Name`, the `Category`, the `DefaultLacing`, or the member `Kind` |
| `[NodeIgnore("reason")]` | Keeps a type or member **out**. The reason is required, and it is reported rather than swallowed |
| `[NodePort]` | Names or describes one parameter or the return value |

Some things are never imported, and the importer says why for each one it skipped: nested types,
generic type definitions, enums, interfaces, delegates and compiler-generated types.

Write the XML docs. They are what a user reads in the library panel and in help, and
`GenerateDocumentationFile` is what puts them in the package.

---

## 4. The project

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>

    <PackageId>Acme.SparkNodes</PackageId>
    <Version>1.0.0</Version>
    <Authors>Acme Ltd</Authors>
    <Description>Panelisation and setting-out nodes for Spark.</Description>
    <PackageTags>spark</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>

  <!-- Contract assemblies: referenced from an install, and NEVER shipped. See section 5. -->
  <ItemGroup>
    <Reference Include="Spark.Api">
      <HintPath>$(SparkInstall)\Spark.Api.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Spark.Geometry">
      <HintPath>$(SparkInstall)\Spark.Geometry.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <None Include="spark.json" Pack="true" PackagePath="tools/" />
  </ItemGroup>

</Project>
```

Set `SparkInstall` to wherever Spark is installed — the folder holding `Spark.Desktop.exe`. The
contract assemblies ship with their `.xml` documentation beside them, so you get IntelliSense on
them too.

Target `net10.0`, which is what Spark runs on. Older targets down to `net5.0` and
`netstandard2.0`/`2.1` are accepted; `net472` and other .NET Framework monikers are **not** — a
`net472` assembly may load on .NET 10 and may not, and guessing wrong is a type-load failure at run
time rather than a message.

**Platform-specific monikers work**: `net10.0-windows` and `net8.0-windows` are accepted on Windows,
because the process resolving them is already running there. `-android` and `-ios` are not.

**`ref/` counts as well as `lib/`.** NuGet's `lib` is compile-and-run; `ref` is compile-only, which
is what a package whose implementation comes from a host at run time ships — every CAD API package
is this shape. Spark reads `lib` when it is there and falls back to `ref`, so a reference-only
package is usable for writing code against.

---

## 5. Three traps

### `<Private>false</Private>` — the one that will bite you

`Spark.Api`, `Spark.Geometry`, `Spark.Geometry.Io` and `Spark.Engine` are **contract assemblies**.
`PackageLoadContext` always resolves them from the host process, never from your package, because
instances of their types cross the boundary between Spark and your nodes. If you ship your own
copies, a `Point3d` your node returns is not the `Point3d` the graph understands.

`<Private>false</Private>` on every contract reference is what keeps them out of your `lib` folder.
Nothing checks this for you.

Deliberately **not** contract assemblies: `Spark.Nodes.Core`, `Spark.Viewport`, `Spark.UI`,
`Spark.Scripting`. Nothing a package legitimately does needs their types, and adding one to the list
would convert an internal detail into a permanent public promise.

### You reference Spark from an install, not from nuget.org

Spark publishes nothing. `IsPackable` is `false` for every project in this repository, by decision
**D11** — Spark is an application, not a library ecosystem. Node authors reference `Spark.Api` and
`Spark.Geometry` **from an install**, which is how Revit and AutoCAD add-ins are built anyway, since
those already resolve their assemblies out of a directory rather than restoring them.

This is friction and it is not accidental. Publishing the contract assemblies would buy a
convenience in exchange for permanent package-id ownership, a signing story, a release cadence tied
to nuget.org and a compatibility obligation to strangers.

### Signatures are read, not verified

The install disclosure a user sees reports **present but unverified** for a signed package. Spark
does not check the certificate chain, revocation, or who signed it. Do not rely on a signature to
tell your users anything, and do not let the disclosure imply to you that one was checked.

---

## 6. Pack and publish

```bash
dotnet pack -c Release
dotnet nuget push bin/Release/Acme.SparkNodes.1.0.0.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key <your-key>
```

`dotnet pack` puts your assembly at `lib/net10.0/Acme.SparkNodes.dll`, which is what Spark expects.

**That sentence was once false, and it cost a whole layer of test coverage** —
[N77](NOTES.md#n77--every-package-test-passed-and-no-real-package-could-be-installed). The loader
looked for the assembly at the package root, so *every package on nuget.org would have failed to
install*, and fifty-eight green tests hid it because each one built its package by hand with the
DLL at the root. The fix uses NuGet's own `FrameworkReducer` rather than hand-written string
comparison.

---

## 7. Check it before you push

There is no automated check, so do this by hand. Open the `.nupkg` — it is a zip:

1. `tools/spark.json` is present and names your assembly by **simple name**, no `.dll`.
2. `lib/net10.0/` holds your assembly and its `.xml` documentation.
3. `lib/net10.0/` does **not** hold `Spark.Api.dll`, `Spark.Geometry.dll`, `Spark.Geometry.Io.dll`
   or `Spark.Engine.dll`. If it does, fix `<Private>false</Private>` and pack again.
4. The `spark` tag is in the `.nuspec`.

Then install it from a local folder feed before you push it anywhere:

```bash
mkdir -p ./local-feed
cp bin/Release/Acme.SparkNodes.1.0.0.nupkg ./local-feed/
```

Point Spark at that folder as its package source, open **File ▸ Packages…**, search, and press
**Install…**. You should see your publisher, licence and node count in the disclosure, and your
nodes in the library panel after you agree.

---

## 8. What is proven, and what is not

**Proven.** A package genuinely produced by `dotnet pack`, from a project written for the purpose,
was installed and loaded once by hand when [N77](NOTES.md) was fixed. Package install, the
disclosure, the trust store, transitive dependency restore, per-version load contexts and unloading
are all covered by `Spark.Packages.Tests`.

**Not proven.** No fixture in the suite packs a real project and installs the result — every test
builds its package with `ZipArchive`. So the round trip works and **is not guarded**, which is
precisely the shape of defect N77 was.

**Not built at all.** A `dotnet new` template, a `spark pack` verb that writes the manifest and
checks the four points in section 7, and a test that packs a real project end to end. None of these
is on the register yet.

---

## 9. If you only want *types*, not nodes

You do not need any of this. **File ▸ Packages…**, untick **Spark packages only**, and press **Add
as a library…** — any ordinary .NET package works, with no manifest and no tag, and its types become
available to code blocks. See
[Code blocks](help/concepts/code-blocks.md#using-a-library-from-nugetorg).

The two are genuinely different operations: **Install** adds nodes to the canvas and needs the
manifest, **Add as a library** adds types to a code block and needs nothing. Publish a Spark package
only if you want the first.
