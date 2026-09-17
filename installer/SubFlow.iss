; SubFlow installer (Inno Setup 6).
; Build with installer\build-installer.ps1, which publishes the app into
; installer\out\app first and then compiles this script.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "SubFlow"
#define AppExe "NapisyPL.exe"
#define MenuText "Szukaj napisów z SubFlow"

[Setup]
AppId={{BBF4C1AB-BB31-4F1A-9C35-FF375106C4FB}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=SubFlow
AppVerName={#AppName} {#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user by default (no UAC prompt); the dialog still offers "install for all users".
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\NapisyPL\Assets\subflow.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
OutputDir=out
OutputBaseFilename=SubFlow-Setup-{#AppVersion}

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "explorermenu"; Description: "Dodaj „{#MenuText}” do menu Eksploratora (filmy i foldery)"; GroupDescription: "Integracja z Windows:"
Name: "autostart"; Description: "Uruchamiaj SubFlow w tle przy logowaniu (ikona w obszarze powiadomień)"; GroupDescription: "Integracja z Windows:"; Flags: unchecked
Name: "desktopicon"; Description: "Skrót na pulpicie"; GroupDescription: "Integracja z Windows:"; Flags: unchecked
Name: "offlinemt"; Description: "Pobierz lokalny tłumacz MADLAD dla kart AMD Radeon (ok. 4 GB, działa bez internetu)"; GroupDescription: "Tłumaczenie offline:"; Flags: unchecked

[Files]
Source: "out\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same verb the app registers from Opcje → Menu Eksploratora (HKA = HKCU for a
; per-user install, HKLM for all users).
Root: HKA; Subkey: "Software\Classes\Directory\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\SubFlow.Search"; ValueName: "MultiSelectModel"; ValueType: string; ValueData: "Player"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\SubFlow.Search"; ValueName: "MultiSelectModel"; ValueType: string; ValueData: "Player"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\SubFlow.Search"; ValueName: "MultiSelectModel"; ValueType: string; ValueData: "Player"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.avi\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.avi\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.avi\shell\SubFlow.Search"; ValueName: "MultiSelectModel"; ValueType: string; ValueData: "Player"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.avi\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.webm\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.webm\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.webm\shell\SubFlow.Search"; ValueName: "MultiSelectModel"; ValueType: string; ValueData: "Player"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.webm\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.m4v\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.m4v\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.m4v\shell\SubFlow.Search"; ValueName: "MultiSelectModel"; ValueType: string; ValueData: "Player"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.m4v\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.ts\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.ts\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.ts\shell\SubFlow.Search"; ValueName: "MultiSelectModel"; ValueType: string; ValueData: "Player"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.ts\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mts\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mts\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mts\shell\SubFlow.Search"; ValueName: "MultiSelectModel"; ValueType: string; ValueData: "Player"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.mts\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.m2ts\shell\SubFlow.Search"; ValueType: string; ValueData: "{#MenuText}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.m2ts\shell\SubFlow.Search"; ValueName: "Icon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.m2ts\shell\SubFlow.Search"; ValueName: "MultiSelectModel"; ValueType: string; ValueData: "Player"; Tasks: explorermenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.m2ts\shell\SubFlow.Search\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" --search ""%1"""; Tasks: explorermenu
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"" --background"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\offline-mt-setup\install-amd-runtime.ps1"" -Destination ""{app}\nllb-amd-runtime"""; StatusMsg: "Pobieram lokalny tłumacz (to może potrwać kilkanaście minut)…"; Flags: waituntilterminated; Tasks: offlinemt
Filename: "{app}\{#AppExe}"; Description: "Uruchom SubFlow"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Removes the per-user entry the app itself may have added from its Options menu.
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-shell"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterExplorerMenu"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\nllb-amd-runtime"
