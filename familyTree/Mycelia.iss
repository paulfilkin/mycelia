; Mycelia installer (Inno Setup 6).
;
; Build first, from the repository root:
;   dotnet publish familyTree/Mycelia.csproj -c Release -r win-x64 --self-contained true
; then:
;   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" familyTree\Mycelia.iss
;
; The setup executable is written to Releases\ (untracked).  The version appears on four lines below and must
; match <Version> in Mycelia.csproj.  Never change AppId: Inno Setup uses it to upgrade an existing
; installation in place.  The release steps, signing included, are in the TDD (section 23).

[Setup]
; Basic information
AppName=Mycelia
AppVersion=0.2.0
AppVerName=Mycelia 0.2.0
VersionInfoVersion=0.2.0.0
VersionInfoDescription=Mycelia: family trees, connected to their sources.
AppComments=Family trees, connected to their sources.
AppPublisher=multifarious
AppPublisherURL=https://multifarious.filkin.com/
AppSupportURL=https://multifarious.filkin.com/
AppId={{868D1A5D-3F81-43B0-B277-2A338F927251}
DefaultDirName={autopf}\Mycelia
DefaultGroupName=Mycelia
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Mycelia.exe
Compression=lzma2
SolidCompression=yes
OutputDir=..\Releases
OutputBaseFilename=MyceliaSetup-0.2.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=Resources\Mycelia.ico
; Per-user install, no administrator rights needed (installs under %LocalAppData%\Programs), so an update started
; by the program shows no UAC prompt.
PrivilegesRequired=lowest

[Files]
; Everything from the self-contained publish folder
Source: "bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start menu shortcut
Name: "{group}\Mycelia"; Filename: "{app}\Mycelia.exe"; WorkingDir: "{app}"
; Desktop shortcut
Name: "{autodesktop}\Mycelia"; Filename: "{app}\Mycelia.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"

[Run]
; Option to launch after an interactive install
Filename: "{app}\Mycelia.exe"; Description: "Launch Mycelia"; Flags: nowait postinstall skipifsilent
; After a silent update started by the program itself (/VERYSILENT), start it again
Filename: "{app}\Mycelia.exe"; Flags: nowait; Check: IsSilentInstall

; Settings in %AppData%\Mycelia (including encrypted API keys) and the Assistant's conversations in
; %LocalAppData%\Mycelia are left in place on uninstall, so a reinstall keeps them.  Trees are wherever the
; person saved them and are never touched.

[Code]
// True when run with /SILENT or /VERYSILENT (an update the program started); the [Run] entry above then restarts it.
function IsSilentInstall: Boolean;
begin
  Result := WizardSilent;
end;
