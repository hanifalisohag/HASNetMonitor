; HAS NetMonitor - standalone installer (Inno Setup 6)
;
; Build it by hand with:
;   dotnet publish HASNetMonitor.csproj -c Release -f net48 -o publish
;   "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\HASNetMonitor.iss
; which writes installer\Output\HASNetMonitor-Setup-<version>.exe
; The second line is cmd.exe syntax; in PowerShell it is
;   & "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\HASNetMonitor.iss
; because %VAR% is not expanded there and a quoted path needs the & operator.
;
; The publish folder is the only input: the exe inside it is the whole app. Override
; the defaults with /DAppVersion=<x.y.z> /DPublishDir=<dir> /DOutputDir=<dir>; the
; GitHub workflow passes the version it read from the project file.
;
; Why .NET Framework and not .NET 11: see "Shipping a small exe" in README.md. The
; installer therefore downloads no runtime, only checks the in-box component.

#define AppName "HAS NetMonitor"
; The install folder and Start-menu group use the product's own spelling, so the
; path is %LOCALAPPDATA%\Programs\HASNetMonitor\HASNetMonitor.exe. AppName stays the
; display name shown in the wizard, Apps & features and the shortcuts.
#define AppFolderName "HASNetMonitor"
#define AppExeName "HASNetMonitor.exe"
; The per-user autostart value Core/StartupRegistration.cs writes and clears itself.
#define RunValueName "HASNetMonitor"
; %APPDATA%\<ConfigFolder> is the settings folder Core/MonitorConfig.cs uses.
#define ConfigFolder "HASNetMonitor"

#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "Output"
#endif

[Setup]
; A stable AppId keeps upgrades replacing the previous install instead of stacking
; copies beside it. Never change it after the first public release.
AppId={{7D4E2C1A-58B3-4F6E-9C2D-1E7A6B0F3D94}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Md Hanif Ali Sohag
AppPublisherURL=https://github.com/hanifalisohag/HASNetMonitor
AppSupportURL=https://github.com/hanifalisohag/HASNetMonitor/issues
DefaultDirName={autopf}\{#AppFolderName}
DefaultGroupName={#AppFolderName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
; The app is a per-user taskbar widget: it keeps its settings in %APPDATA% and its
; logon entry in HKCU. A per-user install needs no UAC prompt and lands where the
; user can write, which is what README.md promises ("no admin rights needed").
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
; Windows 10 1903+ and Windows 11: the net48 build needs .NET Framework 4.8, which
; is the in-box version from 1903 onwards.
MinVersion=10.0.18362
OutputDir={#OutputDir}
OutputBaseFilename=HASNetMonitor-Setup-{#AppVersion}
SetupIconFile=..\Assets\HASNetMonitor.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
; One exe, no background updater: keep the wizard short, but show the licence because
; it carries the attribution requirement.
AllowNoIcons=yes
; The licence page shows LICENSE at the repository root: the same text the About box
; reads from the embedded resource, so the user sees the credit before installing.
LicenseFile=..\LICENSE
; Program.cs takes Local\HASNetMonitor.Instance for the life of the process, so this
; is the app's own "is it running" answer. Setup and Uninstall refuse to continue
; while it is held instead of half-copying over a running widget.
AppMutex=Local\HASNetMonitor.Instance
; Restart Manager must not try to close the widget: it would send WM_CLOSE, which the
; layered window does not treat as Exit, leaving a running process with no window.
; The user quits from the tray menu (Exit) and AppMutex explains that.
CloseApplications=no
RestartApplications=no
; Signing belongs to the workflow, which reads its material from secrets; the .iss
; stays free of keys and certificates.
SignedUninstaller=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; The publish output is the app: HASNetMonitor.exe and nothing else it needs at run
; time. The .pdb is debug symbols, so it is deliberately not installed.
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; The licence travels with the installed copy: the About box reads it from the exe, and
; this file is the copy a user can read without launching anything.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
; Any side-by-side assemblies a future publish adds, if present.
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; The starting settings are a template, not the user's config: SeedConfig copies it to
; %APPDATA% only when no config.json exists yet. Keeping it here means the uninstaller
; never owns the user's file, so uninstalling cannot delete their settings.
Source: "config.default.json"; DestDir: "{app}"; DestName: "config.default.json"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Launch once the wizard finishes. "skipifsilent" keeps unattended installs quiet;
; a second copy would exit on the mutex anyway.
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Registry]
; "Start with Windows" is the app's own setting (the settings dialog toggles it and
; MonitorConfig defaults it on), so Setup does not offer a second, competing switch.
; This entry only registers the value for deletion: uninstalling must not leave a Run
; entry pointing at an exe that is no longer there. dontcreatekey means nothing is
; written during install.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "{#RunValueName}"; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
; Leftovers Inno does not track. TNM_TRACE writes wherever it is pointed, and running
; the exe from {app} with TNM_TRACE=trace.log puts the log beside it.
Type: files; Name: "{app}\trace.log"

[Code]
function InitializeSetup: Boolean;
begin
  // The single in-box dependency: .NET Framework 4.8, which the net48 build targets.
  // Windows 10 1903+ ships it; this catches a machine where it was removed.
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    MsgBox('{#AppName} needs .NET Framework 4.8, which this Windows does not have.'
      + #13#10#13#10
      + 'Install it from https://dotnet.microsoft.com/download/dotnet-framework and run Setup again.',
      mbError, MB_OK);
end;

// Gives a brand-new user the same starting settings the app writes on first run, so the
// file exists from the start and can be edited straight away. An existing config.json is
// never touched: reinstalling or upgrading must keep whatever the user has chosen.
// Copying the template by hand (rather than in [Files]) also keeps the file out of the
// uninstaller's list, so uninstalling never removes the user's settings on its own.
procedure SeedConfig;
var
  ConfigPath: String;
  TemplatePath: String;
begin
  ConfigPath := ExpandConstant('{userappdata}\{#ConfigFolder}');
  TemplatePath := ExpandConstant('{app}\config.default.json');

  if FileExists(ConfigPath + '\config.json') then
    Exit;

  if not FileExists(TemplatePath) then
    Exit;

  if not ForceDirectories(ConfigPath) then
    Exit;

  CopyFile(TemplatePath, ConfigPath + '\config.json', False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // After the files are in place: the template is in {app} by then.
  if CurStep = ssPostInstall then
    SeedConfig;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ConfigPath: String;
  PromptForm: TSetupForm;
  PathLabel: TNewStaticText;
  DeleteCheckBox: TNewCheckBox;
  OKButton, CancelButton: TNewButton;
  ButtonWidth: Integer;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  // Settings live in %APPDATA%\HASNetMonitor and are the user's, so they are kept by
  // default: a reinstall then starts from the saved settings. Deleting them is a
  // separate, opt-in decision made on the checkbox below.
  ConfigPath := ExpandConstant('{userappdata}\{#ConfigFolder}');
  if not DirExists(ConfigPath) then
    Exit;

  // An unattended uninstall has nobody to tick the box, so it keeps the settings. That
  // also makes "remove the program, keep my settings" expressible from a script:
  //   unins000.exe /VERYSILENT
  if UninstallSilent then
    Exit;

  // The uninstaller has no wizard pages, so the question is a small dialog of its own.
  PromptForm := CreateCustomForm(372, 132, False, True);
  try
    PromptForm.Caption := 'Uninstall {#AppName}';

    PathLabel := TNewStaticText.Create(PromptForm);
    PathLabel.Parent := PromptForm;
    PathLabel.Left := ScaleX(12);
    PathLabel.Top := ScaleY(12);
    PathLabel.Width := PromptForm.ClientWidth - ScaleX(24);
    PathLabel.Height := ScaleY(34);
    PathLabel.AutoSize := False;
    PathLabel.WordWrap := True;
    PathLabel.Caption := 'Your saved settings are kept. They live in:'
      + #13#10 + ConfigPath;

    // Unticked: leaving the box alone keeps the settings, so the safe answer is the
    // one that needs no click.
    DeleteCheckBox := TNewCheckBox.Create(PromptForm);
    DeleteCheckBox.Parent := PromptForm;
    DeleteCheckBox.Left := ScaleX(12);
    DeleteCheckBox.Top := PathLabel.Top + PathLabel.Height + ScaleY(10);
    DeleteCheckBox.Width := PromptForm.ClientWidth - ScaleX(24);
    DeleteCheckBox.Height := ScaleY(17);
    DeleteCheckBox.Caption := 'Also delete my saved settings (config.json)';
    DeleteCheckBox.Checked := False;

    OKButton := TNewButton.Create(PromptForm);
    OKButton.Parent := PromptForm;
    OKButton.Caption := 'OK';
    OKButton.ModalResult := mrOk;
    OKButton.Default := True;
    OKButton.Height := ScaleY(23);

    CancelButton := TNewButton.Create(PromptForm);
    CancelButton.Parent := PromptForm;
    CancelButton.Caption := 'Cancel';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;
    CancelButton.Height := ScaleY(23);

    ButtonWidth := PromptForm.CalculateButtonWidth([OKButton.Caption, CancelButton.Caption]);
    OKButton.Width := ButtonWidth;
    CancelButton.Width := ButtonWidth;
    OKButton.Top := PromptForm.ClientHeight - ScaleY(23 + 10);
    CancelButton.Top := OKButton.Top;
    CancelButton.Left := PromptForm.ClientWidth - ScaleX(10) - ButtonWidth;
    OKButton.Left := CancelButton.Left - ScaleX(6) - ButtonWidth;

    // Cancel means "no, keep my settings"; the program itself is already gone either
    // way, so it must not pretend the uninstall was undone.
    if PromptForm.ShowModal <> mrOk then
      Exit;

    if DeleteCheckBox.Checked then
      DelTree(ConfigPath, True, True, True);
  finally
    PromptForm.Free;
  end;
end;
