; THE 3:13 MACHINE — Windows installer (Inno Setup 6)
; Produces Output\The313Machine_Setup.exe.
;
; Before compiling:
;   1. Unity: Hikari Forge → Build Windows (x64)      → ..\Builds\Windows\  (The313Machine.exe + _Data + UnityPlayer.dll ...)
;   2. Launcher: cd ..\Launcher && dotnet publish -c Release -o ..\Release\launcher
;   3. iscc The313Machine.iss   (or open in Inno Setup Compiler and press Build)
; Never compile with an empty or placeholder game folder for release: the [Files] check below aborts if the game exe is missing.

#define AppName "The 3:13 Machine"
; Windows forbids ":" in file and folder names, so everything on disk uses SafeName.
#define SafeName "The 313 Machine"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SetupName
  #define SetupName "The313Machine_setup"
#endif
#define Publisher "Hikari Forge Studios"
#define GameDir "..\Builds\Windows"
#define LauncherDir "..\Release\launcher"
#define GameExe "The313Machine.exe"
#define LauncherExe "The313MachineLauncher.exe"

#if !FileExists(GameDir + "\" + GameExe)
  #error The Unity build was not found in ..\Builds\Windows. Build the game first.
#endif
; The launcher is optional for beta builds: without it, shortcuts start the game directly.
#define IntroVideo "Intro\Hikari_Forge_Studios_Startup_Intro.mp4"
#if FileExists(LauncherDir + "\" + LauncherExe)
  #define HasLauncher
  #define StartExe LauncherExe
  #if !FileExists(LauncherDir + "\" + IntroVideo)
    #error The studio intro video is missing from ..\Release\launcher\Intro. Re-publish the launcher.
  #endif
#else
  #define StartExe "Game\" + GameExe
#endif

[Setup]
AppId={{6B3D3C13-0313-4F3A-9C13-313313313313}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppCopyright=© {#Publisher}
DefaultDirName={autopf}\{#Publisher}\{#SafeName}
DefaultGroupName={#SafeName}
DisableProgramGroupPage=yes
; Per-user install by default: no administrator rights needed. Users may choose all-users in the dialog.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename={#SetupName}
SetupIconFile=setup.ico
UninstallDisplayIcon={app}\The313Machine.ico
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard_large.bmp
WizardSmallImageFile=wizard_small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
#ifdef HasLauncher
Source: "{#LauncherDir}\{#LauncherExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#LauncherDir}\launcher.json"; DestDir: "{app}"; Flags: ignoreversion onlyifdoesntexist
; Studio cinematic, played by the launcher from {app}\Intro (WPF/.NET runtime is bundled inside the launcher exe).
; The game carries its own copy in Game\The313Machine_Data\StreamingAssets for direct launches.
Source: "{#LauncherDir}\{#IntroVideo}"; DestDir: "{app}\Intro"; Flags: ignoreversion
#endif
; Game logo icon (Tools/ArtGen/make_app_icon.ps1). Shortcuts point at this standalone .ico so Windows
; cannot keep showing a cached icon from an older exe.
Source: "setup.ico"; DestDir: "{app}"; DestName: "The313Machine.ico"; Flags: ignoreversion
Source: "README_BETA.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme skipifsourcedoesntexist
Source: "{#GameDir}\*"; DestDir: "{app}\Game"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*_BurstDebugInformation_DoNotShip\*,*_BackUpThisFolder_ButDontShipItWithYourGame\*"

[Icons]
Name: "{group}\{#SafeName}"; Filename: "{app}\{#StartExe}"; IconFilename: "{app}\The313Machine.ico"
Name: "{group}\{cm:UninstallProgram,{#SafeName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#SafeName}"; Filename: "{app}\{#StartExe}"; IconFilename: "{app}\The313Machine.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#StartExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Saved games live in %USERPROFILE%\AppData\LocalLow\Hikari Forge Studios\The 313 Machine and are
; deliberately NOT removed on uninstall. Launcher preferences in %APPDATA% are removed.
[UninstallDelete]
Type: files; Name: "{userappdata}\Hikari Forge Studios\The 313 Machine\launcher_settings.json"
Type: files; Name: "{userappdata}\Hikari Forge Studios\The 313 Machine\launcher.log"
