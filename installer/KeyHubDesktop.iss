#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{BA07164E-F238-4F51-88DD-68B2FBD893ED}
AppName=KeyHub Desktop
AppVersion={#AppVersion}
AppPublisher=ddbbiii
DefaultDirName={localappdata}\Programs\KeyHubDesktop
DefaultGroupName=KeyHub Desktop
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=KeyHubDesktop-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\KeyHub.Desktop.exe
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\KeyHub Desktop"; Filename: "{app}\KeyHub.Desktop.exe"
Name: "{userdesktop}\KeyHub Desktop"; Filename: "{app}\KeyHub.Desktop.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}\cli"; Check: NeedsAddPath(ExpandConstant('{app}\cli')); Flags: preservestringtype

[Run]
Filename: "{app}\KeyHub.Desktop.exe"; Description: "启动 KeyHub Desktop"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsAddPath(Path: string): Boolean;
var
  Existing: string;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', Existing) then
    Existing := '';
  Result := Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Existing) + ';') = 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Existing: string;
  AppPath: string;
begin
  if CurUninstallStep <> usUninstall then Exit;
  AppPath := ExpandConstant('{app}\cli');
  if RegQueryStringValue(HKCU, 'Environment', 'Path', Existing) then
  begin
    StringChangeEx(Existing, ';' + AppPath, '', True);
    StringChangeEx(Existing, AppPath + ';', '', True);
    if CompareText(Existing, AppPath) = 0 then Existing := '';
    RegWriteExpandStringValue(HKCU, 'Environment', 'Path', Existing);
  end;
end;
