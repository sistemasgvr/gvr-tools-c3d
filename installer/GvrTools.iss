; GVR Tools — instalador comercial (Inno Setup 6+)
; Empaqueta build/2021…2027 en %ProgramData%\GVR\GvrTools\<año>\ y escribe .addin.
; Desarrollo interno: seguir usando scripts/install-addin.ps1

#define MyAppName "GVR Tools"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "GVR"
#define MyAppURL "https://github.com/sistemasgvr"
#ifndef PayloadRoot
#define PayloadRoot "..\build"
#endif

[Setup]
AppId={{A7C3E2F1-9B4D-4E8A-9C1F-6D2B0A5E8F31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={commonappdata}\GVR\GvrTools
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=GvrTools-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=no
; PDF24 ya viene comprimido: no recomprimir (ahorra minutos en cada build de prueba).
; Las DLLs sí usan lzma2.
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
LicenseFile=assets\TOS.txt
ShowLanguageDialog=yes
; Escudo GVR (src/GvrTools.UI/Icons/Escudo_GVR.png) exportado a los formatos que pide Inno --
; ver installer/assets/README.md para cómo se generaron y cómo regenerarlos si el logo cambia.
SetupIconFile=assets\SetupIcon.ico
WizardImageFile=assets\WizardImage.bmp
WizardSmallImageFile=assets\WizardSmallImage.bmp
UninstallDisplayIcon={app}\SetupIcon.ico
CloseApplications=force
CloseApplicationsFilter=Revit.exe

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "revit2021"; Description: "Revit 2021"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2022"; Description: "Revit 2022"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2023"; Description: "Revit 2023"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2024"; Description: "Revit 2024"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2025"; Description: "Revit 2025"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2026"; Description: "Revit 2026"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked
Name: "revit2027"; Description: "Revit 2027"; GroupDescription: "Instalar este add-in para:"; Flags: unchecked

[InstallDelete]
; Borra el payload anterior del año antes de copiar, para que no queden DLLs viejas mezcladas.
Type: filesandordirs; Name: "{app}\2021"; Tasks: revit2021
Type: filesandordirs; Name: "{app}\2022"; Tasks: revit2022
Type: filesandordirs; Name: "{app}\2023"; Tasks: revit2023
Type: filesandordirs; Name: "{app}\2024"; Tasks: revit2024
Type: filesandordirs; Name: "{app}\2025"; Tasks: revit2025
Type: filesandordirs; Name: "{app}\2026"; Tasks: revit2026
Type: filesandordirs; Name: "{app}\2027"; Tasks: revit2027

[Files]
; Cada carpeta build/<año> se copia solo si el task correspondiente está marcado.
Source: "{#PayloadRoot}\2021\*"; DestDir: "{app}\2021"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace; Excludes: "*.pdb"; Tasks: revit2021
Source: "{#PayloadRoot}\2022\*"; DestDir: "{app}\2022"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace; Excludes: "*.pdb"; Tasks: revit2022
Source: "{#PayloadRoot}\2023\*"; DestDir: "{app}\2023"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace; Excludes: "*.pdb"; Tasks: revit2023
Source: "{#PayloadRoot}\2024\*"; DestDir: "{app}\2024"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace; Excludes: "*.pdb"; Tasks: revit2024
Source: "{#PayloadRoot}\2025\*"; DestDir: "{app}\2025"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace; Excludes: "*.pdb"; Tasks: revit2025
Source: "{#PayloadRoot}\2026\*"; DestDir: "{app}\2026"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace; Excludes: "*.pdb"; Tasks: revit2026
Source: "{#PayloadRoot}\2027\*"; DestDir: "{app}\2027"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace; Excludes: "*.pdb"; Tasks: revit2027
; PDF24 se incluye para que la instalación sea reproducible y no dependa de una descarga.
Source: "prereqs\pdf24-creator-installer.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall nocompression
; Copia del ícono para que UninstallDisplayIcon (abajo) apunte a un archivo real tras instalar --
; sin esto, Agregar o quitar programas usa un ícono genérico de Windows.
Source: "assets\SetupIcon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Run]
; Flags oficiales PDF24 (Inno): silent + sin auto-update + solo impresora PDF (suficiente para Revit 2021).
Filename: "{tmp}\pdf24-creator-installer.exe"; Parameters: "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOUPDATE /COMPONENTS=pdfPrinter"; StatusMsg: "Instalando PDF24 Creator..."; Flags: waituntilterminated; Check: ShouldInstallPdf24

[Code]
var
  PrereqPage: TWizardPage;
  PrereqList: TNewMemo;
  InstallPdf24Check: TNewCheckBox;
  Pdf24InstalledAtStart: Boolean;
  UpgradeInstall: Boolean;

function InitializeSetup(): Boolean;
begin
  UpgradeInstall := RegKeyExists(
    HKLM,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7C3E2F1-9B4D-4E8A-9C1F-6D2B0A5E8F31}_is1');
  Result := True;
end;

function RevitExeExists(Year: Integer): Boolean;
begin
  Result := FileExists(ExpandConstant('{pf}\Autodesk\Revit ' + IntToStr(Year) + '\Revit.exe'));
end;

function IsPdf24BinaryPresent: Boolean;
begin
  { Ejecutables reales del producto. Una carpeta PDF24 vacía o restos de desinstalación no cuentan. }
  Result :=
    FileExists(ExpandConstant('{pf}\PDF24\pdf24.exe')) or
    FileExists(ExpandConstant('{pf}\PDF24\pdf24-DocTool.exe')) or
    FileExists(ExpandConstant('{pf}\PDF24\pdf24-Creator.exe')) or
    FileExists(ExpandConstant('{pf32}\PDF24\pdf24.exe')) or
    FileExists(ExpandConstant('{pf32}\PDF24\pdf24-DocTool.exe')) or
    FileExists(ExpandConstant('{pf32}\PDF24\pdf24-Creator.exe'));
end;

function IsPdf24PrinterPresent: Boolean;
begin
  { Impresora del producto PDF24 Creator.
    No basta con el driver "PDF24": otras apps (p. ej. ProSheets) lo reutilizan. }
  Result :=
    RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Control\Print\Printers\PDF24') or
    RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Control\Print\Printers\PDF24 Creator') or
    RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Control\Print\Printers\PDF24 Toolbox');
end;

function IsPdf24Installed: Boolean;
begin
  { Antes se miraba solo HKLM\SOFTWARE\PDF24 o DirExists — eso queda como basura tras
    desinstalar y marcaba "Ya instalado" sin tener el programa usable para Revit. }
  Result := IsPdf24BinaryPresent and IsPdf24PrinterPresent;
end;

function IsSpanish: Boolean;
begin
  Result := ActiveLanguage = 'spanish';
end;

procedure UpdatePrerequisitePage;
var
  RequiredText, FoundText, ActionText: string;
  Pdf24Required: Boolean;
begin
  Pdf24Required := WizardIsTaskSelected('revit2021');
  Pdf24InstalledAtStart := IsPdf24Installed;

  if Pdf24Required then
    RequiredText := 'Revit 2021'
  else if IsSpanish then
    RequiredText := 'Opcional'
  else
    RequiredText := 'Optional';

  if Pdf24InstalledAtStart then
  begin
    if IsSpanish then
    begin
      FoundText := 'Instalado';
      ActionText := 'Ya instalado';
    end
    else
    begin
      FoundText := 'Installed';
      ActionText := 'Already installed';
    end;
  end
  else
  begin
    FoundText := '';
    if Pdf24Required then
    begin
      if IsSpanish then
        ActionText := 'Debe instalarse'
      else
        ActionText := 'Must install';
    end
    else
    begin
      if IsSpanish then
        ActionText := 'Disponible'
      else
        ActionText := 'Available';
    end;
  end;

  PrereqList.Lines.Clear;
  PrereqList.Lines.Add('Name                    Required          Found             Action');
  PrereqList.Lines.Add('PDF24 Creator      ' + RequiredText + '      ' + FoundText + '      ' + ActionText);

  InstallPdf24Check.Visible := not Pdf24InstalledAtStart;
  if not Pdf24InstalledAtStart then
  begin
    InstallPdf24Check.Checked := Pdf24Required;
    InstallPdf24Check.Enabled := not Pdf24Required;
    if IsSpanish then
    begin
      if Pdf24Required then
        InstallPdf24Check.Caption := 'PDF24 es obligatorio para Revit 2021 y se instalará automáticamente.'
      else
        InstallPdf24Check.Caption := 'Instalar PDF24 Creator (opcional para Revit 2022 o posterior).';
    end
    else
    begin
      if Pdf24Required then
        InstallPdf24Check.Caption := 'PDF24 is required for Revit 2021 and will be installed automatically.'
      else
        InstallPdf24Check.Caption := 'Install PDF24 Creator (optional for Revit 2022 or later).';
    end;
  end;
end;

procedure InitializeWizard;
var
  Y: Integer;
  SelectedTasks: string;
begin
  for Y := 2021 to 2027 do
  begin
    if RevitExeExists(Y) then
    begin
      if SelectedTasks <> '' then
        SelectedTasks := SelectedTasks + ',';
      case Y of
        2021: SelectedTasks := SelectedTasks + 'revit2021';
        2022: SelectedTasks := SelectedTasks + 'revit2022';
        2023: SelectedTasks := SelectedTasks + 'revit2023';
        2024: SelectedTasks := SelectedTasks + 'revit2024';
        2025: SelectedTasks := SelectedTasks + 'revit2025';
        2026: SelectedTasks := SelectedTasks + 'revit2026';
        2027: SelectedTasks := SelectedTasks + 'revit2027';
      end;
    end;
  end;
  if SelectedTasks <> '' then
    WizardSelectTasks(SelectedTasks);

  if IsSpanish then
    PrereqPage := CreateCustomPage(
      wpSelectTasks, 'Prerequisitos', 'Comprueba los componentes necesarios antes de instalar.')
  else
    PrereqPage := CreateCustomPage(
      wpSelectTasks, 'Prerequisites', 'Review the components needed before installation.');

  PrereqList := TNewMemo.Create(PrereqPage);
  PrereqList.Parent := PrereqPage.Surface;
  PrereqList.Left := 0;
  PrereqList.Top := 0;
  PrereqList.Width := PrereqPage.SurfaceWidth;
  PrereqList.Height := ScaleY(112);
  PrereqList.ReadOnly := True;

  InstallPdf24Check := TNewCheckBox.Create(PrereqPage);
  InstallPdf24Check.Parent := PrereqPage.Surface;
  InstallPdf24Check.Left := 0;
  InstallPdf24Check.Top := PrereqList.Top + PrereqList.Height + ScaleY(16);
  InstallPdf24Check.Width := PrereqPage.SurfaceWidth;
  InstallPdf24Check.Height := ScaleY(42);

  Pdf24InstalledAtStart := IsPdf24Installed;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (PrereqPage <> nil) and (CurPageID = PrereqPage.ID) then
    UpdatePrerequisitePage;
end;

function ShouldInstallPdf24: Boolean;
begin
  { Reevaluar al instalar (no solo al abrir el wizard): evita saltarse el setup si el
    estado cambió, o si basura de registro engañó una detección antigua. }
  Result :=
    (not IsPdf24Installed) and
    (WizardIsTaskSelected('revit2021') or InstallPdf24Check.Checked);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectTasks then
  begin
    if (not WizardIsTaskSelected('revit2021')) and
       (not WizardIsTaskSelected('revit2022')) and
       (not WizardIsTaskSelected('revit2023')) and
       (not WizardIsTaskSelected('revit2024')) and
       (not WizardIsTaskSelected('revit2025')) and
       (not WizardIsTaskSelected('revit2026')) and
       (not WizardIsTaskSelected('revit2027')) then
    begin
      MsgBox(
        'Selecciona al menos una versión de Revit.' + #13#10 +
        'Select at least one Revit version.',
        mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function AddinDirForYear(Year: Integer): string;
begin
  if Year >= 2027 then
    Result := ExpandConstant('{pf}\Autodesk\Revit\Addins\' + IntToStr(Year))
  else
    Result := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + IntToStr(Year));
end;

procedure RemoveAddinForYear(Year: Integer);
var
  AddinDir, Path: string;
begin
  AddinDir := AddinDirForYear(Year);
  Path := AddinDir + '\GvrTools.addin';
  if FileExists(Path) then
    DeleteFile(Path);
  Path := AddinDir + '\GvrTools.MassPdfExport.addin';
  if FileExists(Path) then
    DeleteFile(Path);
end;

function WriteAddin(Year: Integer): Boolean;
var
  AddinDir, AddinPath, AssemblyPath, Content: string;
begin
  AddinDir := AddinDirForYear(Year);
  ForceDirectories(AddinDir);
  AssemblyPath := ExpandConstant('{app}\' + IntToStr(Year) + '\GvrTools.App.dll');
  AddinPath := AddinDir + '\GvrTools.addin';
  Content :=
    '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
    '<RevitAddIns>' + #13#10 +
    '  <AddIn Type="Application">' + #13#10 +
    '    <Name>GVR Tools</Name>' + #13#10 +
    '    <Assembly>' + AssemblyPath + '</Assembly>' + #13#10 +
    '    <AddInId>87c18a7a-dc0c-47af-a20d-86d2bcd59a91</AddInId>' + #13#10 +
    '    <FullClassName>GvrTools.App.GvrApplication</FullClassName>' + #13#10 +
    '    <VendorId>GVR</VendorId>' + #13#10 +
    '    <VendorDescription>GVR</VendorDescription>' + #13#10 +
    '  </AddIn>' + #13#10 +
    '</RevitAddIns>';
  Result := SaveStringToFile(AddinPath, Content, False);
end;

procedure SyncAddinForYear(Year: Integer);
begin
  if WizardIsTaskSelected('revit' + IntToStr(Year)) then
    WriteAddin(Year)
  else
    RemoveAddinForYear(Year);
end;

procedure WriteLicenseConfig;
var
  Dir, Path: string;
begin
  Dir := ExpandConstant('{userappdata}\GVR\GvrTools');
  if not ForceDirectories(Dir) then
    Exit;
  Path := Dir + '\license-config.json';
  SaveStringToFile(Path, '{"BaseUrl":"https://tools.proyectosgvr.com"}', False);
end;

procedure ClearUserLicenseCache;
var
  Dir: string;
begin
  Dir := ExpandConstant('{userappdata}\GVR\GvrTools');
  if FileExists(Dir + '\license.dat') then
    DeleteFile(Dir + '\license.dat');
  if FileExists(Dir + '\usage-queue.json') then
    DeleteFile(Dir + '\usage-queue.json');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not UpgradeInstall then
      ClearUserLicenseCache;
    WriteLicenseConfig;
    SyncAddinForYear(2021);
    SyncAddinForYear(2022);
    SyncAddinForYear(2023);
    SyncAddinForYear(2024);
    SyncAddinForYear(2025);
    SyncAddinForYear(2026);
    SyncAddinForYear(2027);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Y: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    ClearUserLicenseCache;
    for Y := 2021 to 2027 do
      RemoveAddinForYear(Y);
  end;
end;
