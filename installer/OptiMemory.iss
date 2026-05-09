#define MyAppName "OptiMemory"
#define MyAppVersion "0.2.1"
#define MyAppPublisher "MiaowCham"
#define MyAppURL "https://github.com/MiaowCham/OptiMemory"
#define MyAppExeName "OptiMemory.exe"
#define MyAppSourceDir "..\publish\release"
#define MyAppIconPath "..\OptiMemory.ico"
#define DotNetDownloadURL "https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0"

[Languages]
Name: "chinese_simplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Setup]
AppId={{A3F7E2B1-4C8D-4E9F-B5A6-7D2E8F3C1A94}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=OptiMemory-{#MyAppVersion}-Setup
SetupIconFile={#MyAppIconPath}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; 需要管理员权限以写入 Program Files 和修改系统 PATH
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "addtopath"; Description: "Add install directory to system PATH"; GroupDescription: "Additional tasks:"

[Files]
Source: "{#MyAppSourceDir}\OptiMemory.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppSourceDir}\OptiMemory.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppSourceDir}\OptiMemory.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppSourceDir}\OptiMemory.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: not addtopath
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: addtopath

[Registry]
; 添加到系统 PATH
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; \
    ValueType: expandsz; ValueName: "Path"; \
    ValueData: "{olddata};{app}"; \
    Check: NeedsAddPath(ExpandConstant('{app}')); \
    Tasks: addtopath; Flags: preservestringtype uninsdeletevalue

[Code]
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', OrigPath)
  then begin
    Result := True;
    exit;
  end;
  // 检查路径是否已存在（大小写不敏感）
  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(OrigPath) + ';') = 0;
end;

function HasDotNet9DesktopRuntime: Boolean;
var
  BasePath: string;
  FindRec: TFindRec;
begin
  Result := False;
  BasePath := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(BasePath) then
    Exit;

  if FindFirst(BasePath + '\\9.*', FindRec) then
  begin
    try
      Result := (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0;
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  OpenDownload: Integer;
  ResultCode: Integer;
begin
  Result := True;
  if not HasDotNet9DesktopRuntime then
  begin
    OpenDownload := MsgBox(
      'This installer is framework-dependent and requires .NET 9 Desktop Runtime.' + #13#10 +
      'The app may not start if runtime is missing.' + #13#10#13#10 +
      'Open the official download page now?',
      mbConfirmation,
      MB_YESNO
    );

    if OpenDownload = IDYES then
      ShellExec('open', '{#DotNetDownloadURL}', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  OrigPath: string;
  AppPath: string;
  NewPath: string;
  P: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppPath := ExpandConstant('{app}');
    if RegQueryStringValue(HKEY_LOCAL_MACHINE,
      'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
      'Path', OrigPath)
    then begin
      // 移除路径（处理末尾分号和中间分号两种情况）
      NewPath := OrigPath;
      P := Pos(';' + AppPath, NewPath);
      if P > 0 then
        Delete(NewPath, P, Length(';' + AppPath))
      else begin
        P := Pos(AppPath + ';', NewPath);
        if P > 0 then
          Delete(NewPath, P, Length(AppPath + ';'));
      end;
      if NewPath <> OrigPath then
        RegWriteStringValue(HKEY_LOCAL_MACHINE,
          'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
          'Path', NewPath);
    end;
  end;
end;
