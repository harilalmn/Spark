; Spark's installer. E13-T17.
;
; WHAT THIS IS FOR. Spark shipped as a portable zip: unzip it anywhere, run the exe, delete the
; folder to uninstall. That is the right release for people who cannot run an installer and it is
; the wrong default, because it puts the whole job of "where does this live and how do I get rid of
; the old one" onto the user. This installs, appears in Add/Remove Programs, and removes the
; previous version before it lays down the new one.
;
; THE APPID IS THE ONE VALUE THAT MUST NEVER CHANGE. Windows identifies an installed product by it.
; Change it and every future release stops recognising the versions before it: two Sparks in
; Add/Remove Programs, two Start menu entries, and an "upgrade" that upgrades nothing. It is a
; random GUID and it means nothing on its own, which is exactly why nobody should ever be tempted
; to tidy it.
;
; PER-USER, INTO %LOCALAPPDATA%, AND NOT ELEVATED. Two reasons, and the second is the sharper one.
; Spark needs nothing outside its own folder, so machine-wide buys the user nothing; and the build
; is not signed yet (E13-T17's remaining half), so an unsigned installer that also demands
; administrator is the single worst shape to hand SmartScreen. When there is an identity to sign
; with, machine-wide becomes a decision worth taking on its merits rather than one taken by default.
;
; THE RUNTIME IS CHAINED, NOT BUNDLED. Spark is framework-dependent on purpose - see the note in
; scripts/publish.ps1, which declines --self-contained for licence reasons that trace back to
; OpenCascade's LGPL relink obligation. So the prerequisite is real and this installer has to deal
; with it: it looks for the runtime, and only downloads Microsoft's installer when it is missing.
;
; It needs Microsoft.NETCore.App and NOT Microsoft.WindowsDesktop.App. Avalonia is its own rendering
; stack rather than a WPF wrapper, so the desktop runtime would be a bigger download for nothing.
; Checked against Spark.Desktop.runtimeconfig.json, which names the framework outright, rather than
; assumed from the fact that this is a desktop application.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif

#ifndef Staged
  #define Staged "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "Spark"
#define Publisher "Spark"
#define AppUrl "https://github.com/harilalmn/Spark"
#define ExeName "Spark.Desktop.exe"

; The .NET runtime Spark is built against. Both halves are here rather than spelled out at each use
; site, because the day this moves to .NET 11 it must move in exactly one place.
#define DotNetMajor "10"
#define DotNetUrl "https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe"

[Setup]
; Never change this. See the note at the top of this file.
AppId={{64C33818-D99E-4FA6-81EB-26615288D8CB}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#FileVersion}
AppPublisher={#Publisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; Per-user. No elevation, no UAC prompt, nothing written outside the user's own profile.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

; x64 only, matching the one RID publish.ps1 stages. Stated rather than left to the default, so a
; 32-bit machine is refused with a sentence instead of installing something that cannot start.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir={#OutputDir}
OutputBaseFilename=spark-{#AppVersion}-setup
SetupIconFile=..\src\Spark.UI\Assets\spark-logo.ico
UninstallDisplayIcon={app}\{#ExeName}
UninstallDisplayName={#AppName} {#AppVersion}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; The whole staged folder, which publish.ps1 has already checked is complete - the application, the
; CLI beside it, the native provider, the licences and the notices. Verifying the payload is that
; script's job and is not repeated here; two checks of the same thing drift apart.
Source: "{#Staged}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

{ ---------------------------------------------------------------------------------------------- }
{ Is the runtime already here?                                                                     }
{                                                                                                  }
{ By looking for the shared framework directory rather than by running `dotnet --list-runtimes`.    }
{ A machine with the runtime and no SDK has no `dotnet` on PATH at all, so the command would report }
{ "not found" on exactly the machines where the answer matters most.                               }
{ ---------------------------------------------------------------------------------------------- }
function DotNetRuntimeInstalled: Boolean;
var
  Base: String;
  Search: TFindRec;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}') + '\dotnet\shared\Microsoft.NETCore.App';

  if not DirExists(Base) then
    Exit;

  if FindFirst(Base + '\*', Search) then
  begin
    try
      repeat
        if (Search.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          { A directory per installed patch: 10.0.11, 10.0.10, and so on. Any 10.x will do - the }
          { runtime rolls forward across patches, which is what makes a version floor honest.    }
          if Pos('{#DotNetMajor}.', Search.Name) = 1 then
          begin
            Result := True;
            Break;
          end;
        end;
      until not FindNext(Search);
    finally
      FindClose(Search);
    end;
  end;
end;

{ ---------------------------------------------------------------------------------------------- }
{ Remove the version already installed, before installing this one.                                }
{                                                                                                  }
{ Inno would install over the top without this, and that is nearly right: the uninstall entry is    }
{ reused and the files are replaced. What it does not do is remove a file this version no longer    }
{ ships - a renamed assembly, a native DLL dropped from the payload - which then sits in the folder }
{ forever and, in .NET's case, can still be loaded. So the previous version is uninstalled          }
{ properly, which is also what the request asked for in as many words.                              }
{                                                                                                  }
{ A failure here is reported and does not stop the install. The new version over the top of the old }
{ one is a worse outcome than a clean install and a much better one than no Spark at all.           }
{ ---------------------------------------------------------------------------------------------- }
function PreviousUninstaller: String;
var
  Key: String;
  Value: String;
begin
  Result := '';
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1';

  if RegQueryStringValue(HKCU, Key, 'QuietUninstallString', Value) then
    Result := Value
  else if RegQueryStringValue(HKLM, Key, 'QuietUninstallString', Value) then
    Result := Value;
end;

function RemovePreviousVersion: Boolean;
var
  Command: String;
  Code: Integer;
begin
  Result := True;
  Command := PreviousUninstaller;

  if Command = '' then
    Exit;

  { QuietUninstallString is the path in quotes followed by its silent switches, so it is split at }
  { the closing quote rather than at the first space - the path contains spaces on any machine    }
  { whose user name does.                                                                         }
  if not ShellExec('', 'cmd.exe', '/C "' + Command + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := False;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Installer: String;
begin
  Result := '';

  if not RemovePreviousVersion then
  begin
    { Not fatal. Say so in the log and carry on; see the note above. }
    Log('The previous version could not be uninstalled. Installing over it instead.');
  end;

  if DotNetRuntimeInstalled then
    Exit;

  { The runtime is missing, so it is fetched. This is the only thing the installer downloads, and }
  { it comes from Microsoft's own aka.ms redirect rather than from a copy we host - a copy would  }
  { be a second thing to keep patched and a second thing to be trusted.                           }
  DownloadPage.Clear;
  DownloadPage.Add('{#DotNetUrl}', 'dotnet-runtime-win-x64.exe', '');
  DownloadPage.Show;

  try
    try
      DownloadPage.Download;
    except
      Result := 'Spark needs the .NET {#DotNetMajor} runtime, and it could not be downloaded: '
        + GetExceptionMessage + #13#10#13#10
        + 'Install it from https://dotnet.microsoft.com/download/dotnet/{#DotNetMajor}.0 '
        + '(the "Run desktop apps" x64 runtime), then run this installer again.';
      Exit;
    end;
  finally
    DownloadPage.Hide;
  end;

  Installer := ExpandConstant('{tmp}\dotnet-runtime-win-x64.exe');

  { Microsoft's installer asks for elevation itself, which is correct: the runtime is machine-wide }
  { even though Spark is not. /passive shows progress and asks nothing.                            }
  if not ShellExec('', Installer, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, Code) then
  begin
    Result := 'The .NET {#DotNetMajor} runtime installer could not be started.';
    Exit;
  end;

  { 3010 is "succeeded, restart required", which is a success. }
  if (Code <> 0) and (Code <> 3010) then
  begin
    Result := 'The .NET {#DotNetMajor} runtime installer failed with code ' + IntToStr(Code) + '.';
    Exit;
  end;

  if Code = 3010 then
    NeedsRestart := True;
end;
