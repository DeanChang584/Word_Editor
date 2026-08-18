; Word Formatter — Inno Setup Installer
; Requires Inno Setup 6+

#define MyAppName "Word Formatter"
#define MyAppShortName "WordFormatter"
#define MyAppVersion "2.1"
#define MyAppPublisher "Dean Chang (TechWordsInSight)"
#define MyAppURL "https://github.com/DeanChang584/WordFormatter"
#define MyAppExeName "WordFormatter.exe"

[Setup]
AppId={{B8F4A3D2-1E5C-4A7B-9D6F-8C2E3A1B5D7F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}.0
DefaultDirName={autopf}\{#MyAppShortName}
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=Word Formatter v{#MyAppVersion}
SetupIconFile=..\frontend\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableDirPage=no
PrivilegesRequired=admin
DisableProgramGroupPage=yes
AllowNoIcons=yes
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce

[Files]
; Backend
Source: "..\dist\WordFormatter\backend.exe"; DestDir: "{app}"; Flags: ignoreversion

; Launcher (the main UI entry point)
Source: "..\dist\WordFormatter\WordFormatter.exe"; DestDir: "{app}"; Flags: ignoreversion

; Frontend (WinUI app + .NET runtime)
Source: "..\dist\WordFormatter\frontend\*"; DestDir: "{app}\frontend"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Word document batch formatting tool"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Word Formatter now"; Flags: postinstall nowait skipifsilent shellexec; WorkingDir: "{app}"

[UninstallRun]
; Kill running processes
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM backend.exe >nul 2>&1"; Flags: runhidden
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM WordFormatterUI.exe >nul 2>&1"; Flags: runhidden
; Remove logs and preview cache
Filename: "{cmd}"; Parameters: "/C if exist ""{localappdata}\WordFormatter"" rmdir /S /Q ""{localappdata}\WordFormatter"""; Flags: runhidden

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  NeedsRestart := False;
  // Kill running processes so files can be overwritten
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM backend.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM WordFormatterUI.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM WordFormatter.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function IsWebView2Installed: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');
  if not Result then
    Result := RegKeyExists(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsWebView2Installed then
  begin
    if MsgBox('WebView2 Runtime is required for preview functionality.' + #13#10 +
              'Go to Microsoft download page now?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download',
                '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;
