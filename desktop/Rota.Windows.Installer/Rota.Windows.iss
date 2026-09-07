#ifndef AppVersion
  #define AppVersion "0.4.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\installer"
#endif

[Setup]
AppId={{70B477A9-8340-4D91-9549-5E53ED5528DF}
AppName=Rota
AppVersion={#AppVersion}
AppVerName=Rota {#AppVersion}
AppPublisher=Rota
AppPublisherURL=https://github.com/LuviKING/Rota
AppSupportURL=https://github.com/LuviKING/Rota/issues
AppUpdatesURL=https://github.com/LuviKING/Rota/releases
DefaultDirName={localappdata}\Programs\Rota
DefaultGroupName=Rota
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=Rota-Windows-v{#AppVersion}-Setup-x64
SetupIconFile=..\Rota.Windows\Assets\Rota.ico
UninstallDisplayIcon={app}\Rota.exe
UninstallDisplayName=Rota {#AppVersion}
Uninstallable=yes
CloseApplications=yes
CloseApplicationsFilter=Rota.exe
RestartApplications=no
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=Rota
VersionInfoDescription=Instalador do Rota para Windows
VersionInfoProductName=Rota
VersionInfoProductVersion={#AppVersion}
#ifdef EnableSigning
SignTool=rotasign
SignedUninstaller=yes
SignToolRetryCount=3
SignToolRetryDelay=2500
#endif

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na área de trabalho"; GroupDescription: "Atalhos adicionais:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\Rota.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Rota.Windows\README-WINDOWS.md"; DestDir: "{app}"; DestName: "LEIA-ME.txt"; Flags: ignoreversion
Source: "..\Rota.Windows\RELEASE-NOTES-0.4.0.md"; DestDir: "{app}"; DestName: "NOVIDADES-0.4.0.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Rota"; Filename: "{app}\Rota.exe"; WorkingDir: "{app}"; Comment: "Abrir o Rota"
Name: "{autodesktop}\Rota"; Filename: "{app}\Rota.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Comment: "Abrir o Rota"

[Run]
Filename: "{app}\Rota.exe"; Description: "Abrir o Rota"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""Rota - Lembrete de estudos"" /F"; Flags: runhidden; RunOnceId: "RemoveRotaReminder"

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not UninstallSilent then
    MsgBox('O aplicativo e seus atalhos serão removidos. Seus planos e histórico locais serão preservados.', mbInformation, MB_OK);
end;
