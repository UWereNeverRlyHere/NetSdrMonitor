; NetSdrMonitor — інсталятор Inno Setup
;
; Збірка:  .\build-installer.ps1                (з теки install\)
;      або  .\build-installer.ps1 -Version 1.2.3
;
; Поруч із цим .iss має лежати інсталятор .NET Desktop Runtime:
;   windowsdesktop-runtime-10.0.8-win-x64.exe

#define MyAppName        "NetSdrMonitor"
#define MyAppVersion     "1.0.1"
#define MyAppPublisher   "u_were_never_rly_here"
#define MyAppExeName     "NetSdrMonitor.Desktop.exe"

[Setup]
; AppId фіксований — потрібен для чистого оновлення та ідентичності в «Програми та компоненти».
; НЕ перегенеровувати між версіями.
AppId={{A1C7E9F2-3D4B-4E8A-9F1C-7B2D5E6A8C90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; OutputDir=. → інсталятор з'являється поруч із setup.iss (NetSdrMonitor\install\).
OutputDir=.
OutputBaseFilename=NetSdrMonitorSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; Та сама іконка, що вшита у вікно застосунку — щоб майстер, ярлики й панель задач виглядали однаково.
SetupIconFile=..\NetSdrMonitor.Desktop\Assets\Icons\app.ico

[Languages]
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Опублікований застосунок — усе з `dotnet publish`, крім .pdb-символів.
Source: "..\NetSdrMonitor.Desktop\bin\Release\net10.0-windows\win-x64\publish\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

; Інсталятор .NET Desktop Runtime 10 — копіюється в {tmp} лише якщо рантайму ще немає.
; deleteafterinstall лишає теку встановлення чистою.
Source: "windowsdesktop-runtime-10.0.8-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not IsDotNet10DesktopInstalled

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Спершу ставимо .NET Desktop Runtime — тихо, без перезавантаження.
Filename: "{tmp}\windowsdesktop-runtime-10.0.8-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Встановлення .NET Desktop Runtime 10.0.8 (1-2 хв)..."; Check: not IsDotNet10DesktopInstalled

; Необов'язковий запуск застосунку після встановлення.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
{ True, якщо встановлено будь-який білд 10.0.x рантайму WindowsDesktop
  (major.minor мають збігтися, patch — будь-який). Дозволяє пропустити
  вшитий інсталятор рантайму, якщо він уже присутній. }
function IsDotNet10DesktopInstalled: Boolean;
var
  FindRec: TFindRec;
  BasePath: String;
begin
  Result := False;
  BasePath := ExpandConstant('{commonpf64}') + '\dotnet\shared\Microsoft.WindowsDesktop.App';
  if not DirExists(BasePath) then
    Exit;

  if FindFirst(BasePath + '\10.0.*', FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;
