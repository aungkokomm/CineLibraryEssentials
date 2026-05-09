; ============================================================================
; Inno Setup Script — CineLibrary Essentials (Portable Distribution)
; ----------------------------------------------------------------------------
; Builds a single setup .exe that installs CineLibrary Essentials with no
; admin rights, no system registration, and config stored alongside the .exe.
;
; Usage:
;   1. Install Inno Setup 6+        https://jrsoftware.org/isinfo.php
;   2. Run a Release self-contained publish FIRST, e.g.:
;        dotnet publish -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 ^
;                       -p:WindowsAppSDKSelfContained=true --self-contained true
;   3. Open this .iss in Inno Setup and click "Compile" (or run ISCC.exe).
;   4. Output is "release\CineLibraryEssentials_Setup_<version>.exe".
; ============================================================================

#define MyAppName "CineLibrary Essentials"
#define MyAppVersion "1.1.2"
#define MyAppPublisher "Aung Ko Ko Myint"
#define MyAppURL "https://github.com/aungkokomm"
#define MyAppExeName "CineLibraryEssentials.exe"
#define MyPublishDir "bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\publish"

[Setup]
; Unique app identifier (do NOT change once published — ensures clean upgrades)
AppId={{8D2C3C6F-4A41-4B98-9D1E-2F4A0E7C9A17}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases

; ===== Portable-friendly install =====
; Per-user install (no admin required)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes

; Setup branding
SetupIconFile=Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern

; Compression
Compression=lzma2/ultra
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Output
OutputDir=release
OutputBaseFilename=CineLibraryEssentials_Setup_{#MyAppVersion}

; Architecture (x64 only — matches our publish target)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Minimum Windows version (matches WinUI 3 SDK requirement: Windows 10 1809 / build 17763)
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startmenuicon"; Description: "Create a &Start Menu shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
; Recursively pull in everything from the publish folder.
; The "*" with recursesubdirs handles all the WindowsAppSDK / .NET runtime dlls.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up app's local config on uninstall (kept alongside the exe)
Type: files; Name: "{app}\appsettings.json"
