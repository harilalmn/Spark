<!--
  The top of every release's notes. Read and substituted by .github/workflows/release.yml; the
  commit list GitHub generates from --generate-notes is appended under it.

  It lives in a file rather than inside the workflow because a PowerShell here-string inside a YAML
  block scalar is two quoting rules fighting each other, and because these are the sentences a user
  reads first - they deserve to be editable without touching the pipeline that ships them.

  Placeholders: {INSTALLER} is the installer's file name, {DOTNET} the .NET major version.
-->

## Install

Download **{INSTALLER}** and run it.

It installs for the current user only, into `%LOCALAPPDATA%\Programs\Spark` — no administrator
prompt — and **removes any previous version first**, so upgrading is just running the new
installer. Spark appears in Add/Remove Programs like anything else, and uninstalling leaves
nothing behind.

Spark needs the **.NET {DOTNET} runtime**. The installer checks for it and fetches it from
Microsoft only if it is missing.

> ⚠️ **This installer is not code-signed**, so Windows SmartScreen warns the first time you run it:
> choose **More info**, then **Run anyway**. Signing needs a certificate issued to a verified
> identity, which Spark does not have yet. Verify the download against its checksum below if that
> matters to you — and it reasonably might.

## Or don't install it

`spark-portable-win-x64.zip` is the same build as a plain folder. Unzip it anywhere, run
`Spark.Desktop.exe`, delete the folder to uninstall. This is the one to use on a machine where you
cannot install software.

## Verifying what you downloaded

Every file has a `.sha256` beside it.

```powershell
$file = '{INSTALLER}'
(Get-FileHash $file).Hash -eq (Get-Content "$file.sha256").Split(' ')[0]
```

## What is in the box

The desktop application, the `spark` command line beside it, and the OpenCascade solid-modelling
provider. Licences and third-party notices ship in the install folder.
