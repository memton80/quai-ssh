; Installateur de Quai (Inno Setup 6) : un seul exécutable, installation par utilisateur (sans droits
; administrateur), désinstallation depuis « Applications installées » de Windows.
;
; Compilé par le CI (.github/workflows/build.yml) :
;   ISCC.exe /DAppVersion=0.1.0 /DAppNumericVersion=0.1.0 /DArch=x64 /DSourceDir=..\publish\x64 /DOutputDir=..\dist installer\Quai.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppNumericVersion
  #define AppNumericVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\" + Arch
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Quai"
#define AppExe "Quai.exe"
#define AppUrl "https://github.com/memton80/quai-ssh"

[Setup]
; Identifiant stable : ne jamais le changer, il relie les mises à jour et la désinstallation.
AppId={{8F6C1C0E-5B0E-4F0B-9E57-3C7F2A9D51A4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppNumericVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Installation de {#AppName}

; Par utilisateur : %LOCALAPPDATA%\Programs\Quai, aucune élévation.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=no
UsePreviousAppDir=yes
MinVersion=10.0.17763

#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

OutputDir={#OutputDir}
OutputBaseFilename=Quai-Setup-{#Arch}
SetupIconFile=..\src\Quai.App\Assets\Quai.ico
WizardStyle=modern
; Compression maximale (dictionnaire de 64 Mo, archive solide) : installateur plus petit, compilation un peu plus longue.
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Mise à jour par-dessus une version ouverte : l'installateur propose de fermer Quai.
CloseApplications=yes
RestartApplications=no

UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
french.PreparingApp=Préparation de Quai…
french.RemoveUserData=Supprimer aussi vos données Quai ?%n%nOui : les connexions enregistrées, les réglages, les hôtes connus et les secrets conservés dans le coffre Windows sont effacés.%n%nNon : ils sont conservés pour une réinstallation ultérieure.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Premier accès à tous les fichiers neufs (analyse antivirus) pendant l'installation plutôt qu'au premier démarrage,
; qui sinon met 10 s à s'afficher puis ne répond plus pendant une minute. Rien n'est exécuté ni affiché.
Filename: "{app}\{#AppExe}"; Parameters: "--warmup"; WorkingDir: "{app}"; StatusMsg: "{cm:PreparingApp}"; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Mise à jour lancée par Quai lui-même (/SILENT /RELAUNCH=1) : la nouvelle version redémarre une fois installée.
Filename: "{app}\{#AppExe}"; Parameters: "--after-update"; WorkingDir: "{app}"; Flags: nowait; Check: RelaunchRequested

[UninstallDelete]
; Journaux (et cache de l'ancien terminal WebView2 des premières versions de développement) :
; jamais conservés, ce ne sont pas des données de l'utilisateur.
Type: filesandordirs; Name: "{localappdata}\{#AppName}\WebView2"
Type: filesandordirs; Name: "{localappdata}\{#AppName}\logs"
Type: filesandordirs; Name: "{localappdata}\{#AppName}\updates"
Type: dirifempty; Name: "{localappdata}\{#AppName}"

[Code]
var
  RemoveUserData: Boolean;

// Installation demandée par le bouton « Mettre à jour » de Quai : relancer l'application à la fin.
function RelaunchRequested(): Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

// Désinstallation : on demande s'il faut aussi effacer les données personnelles (non par défaut).
// En mode silencieux, le paramètre /PURGE=1 répond « oui ».
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if UninstallSilent() then
    RemoveUserData := ExpandConstant('{param:PURGE|0}') = '1'
  else
    RemoveUserData := MsgBox(CustomMessage('RemoveUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  // Avant le retrait des fichiers : l'application efface elle-même ses secrets du coffre Windows.
  if (CurUninstallStep = usUninstall) and RemoveUserData then
    Exec(ExpandConstant('{app}\{#AppExe}'), '--purge-secrets', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
    DelTree(ExpandConstant('{localappdata}\{#AppName}'), True, True, True);
end;
