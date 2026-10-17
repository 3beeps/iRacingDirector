; Inno Setup script for iRacing Director. Built by tools/build-installer.ps1.
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8C1B6E0A-5E1B-4C33-9C1A-2B7D5D7A1F11}
AppName=iRacing Director
AppVersion={#AppVersion}
AppVerName=iRacing Director {#AppVersion}
AppPublisher=iRacing Director
; Per-user install: no admin rights needed to install.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\iRacing Director
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
OutputDir=..\dist
OutputBaseFilename=iRacingDirector-Setup-{#AppVersion}
SetupIconFile=..\src\iRacingDirector\Assets\director.ico
UninstallDisplayIcon={app}\iRacingDirector.exe
UninstallDisplayName=iRacing Director
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Director closes itself via --exit (see PrepareToInstall), which also works when it runs as administrator.
CloseApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\iRacingDirector.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\iRacing Director"; Filename: "{app}\iRacingDirector.exe"
Name: "{userdesktop}\iRacing Director"; Filename: "{app}\iRacingDirector.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\iRacingDirector.exe"; Description: "Start iRacing Director"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Closes Director and removes its sign-in startup entry.
Filename: "{app}\iRacingDirector.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "DirectorCleanup"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// Compares dotted versions ("1.10.0" > "1.9.2"). Returns <0, 0 or >0.
function CompareVersion(A, B: String): Integer;
var
  PA, PB, NA, NB: Integer;
begin
  Result := 0;
  while (Result = 0) and ((A <> '') or (B <> '')) do
  begin
    PA := Pos('.', A); if PA = 0 then PA := Length(A) + 1;
    PB := Pos('.', B); if PB = 0 then PB := Length(B) + 1;
    NA := StrToIntDef(Copy(A, 1, PA - 1), 0);
    NB := StrToIntDef(Copy(B, 1, PB - 1), 0);
    Result := NA - NB;
    Delete(A, 1, PA);
    Delete(B, 1, PB);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exe, Version: String;
begin
  Exe := ExpandConstant('{app}\iRacingDirector.exe');
  if FileExists(Exe) then
  begin
    if GetVersionNumbersString(Exe, Version) and (CompareVersion(Version, '1.1.0') >= 0) then
      Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    else
      // 1.0 had no --exit (running it would just start the app), so close it the blunt way.
      Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM iRacingDirector.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if SuppressibleMsgBox('Also delete your iRacing Director settings?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\iRacing Director'), True, True, True);
end;
