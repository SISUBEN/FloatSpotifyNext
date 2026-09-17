; ============================================================================
;  FloatSpotify Next — Inno Setup 安装脚本
;
;  离线版（自包含载荷，无需联网）：
;     ISCC.exe /DAppVersion=1.0.0 FloatSpotify.Next.iss
;
;  在线版（框架依赖载荷，仅缺运行时时联网下载）：
;     ISCC.exe /DAppVersion=1.0.0 /DONLINE FloatSpotify.Next.iss
;
;  载荷由 build\build-release.ps1 生成到 artifacts\publish\ 下。
;  需要 Inno Setup 6（在线版用到 DownloadTemporaryFile，建议用最新版）。
;  本脚本已在 Inno Setup 6.7.3 上验证编译通过。
; ============================================================================

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName       "FloatSpotify Next"
#define AppExeName    "FloatSpotify.Next.exe"
#define AppPublisher  "SISUBENY"
; 仓库地址用 GitHub 用户名 SISUBEN，和 AppPublisher 的署名 SISUBENY 不是一个值
#define AppUrl        "https://github.com/SISUBEN/FloatSpotifyNext"
#define AppMutex      "Local\FloatSpotify.Next"

; 发布产物统一命名：{ProductName}-{Arch}-v{版本}-{变体}.{扩展名}
; 例：FloatSpotifyNext-x86_64-v1.0.0-setup-offline.exe
#define ProductName   "FloatSpotifyNext"
#define Arch          "x86_64"

#ifdef ONLINE
  #define PayloadDir  "..\artifacts\publish\framework-dependent"
  #define Variant     "setup-online"
  #define RuntimeUrl  "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
#else
  #define PayloadDir  "..\artifacts\publish\self-contained"
  #define Variant     "setup-offline"
#endif

[Setup]
; AppId 是安装包的永久标识，升级安装靠它识别，不要随意更改。
AppId={{AFFEE62A-0BF7-4762-827E-456F361D8FC5}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
; 安装程序自身的图标，与主程序共用同一个 .ico（路径相对本脚本所在目录）。
SetupIconFile=..\src\FloatSpotify\Assets\app.ico
OutputDir=Output
OutputBaseFilename={#ProductName}-{#Arch}-v{#AppVersion}-{#Variant}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.17763
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; 中文安装界面：把 ChineseSimplified.isl 放到本目录后取消下一行注释。
; 翻译文件：https://github.com/jrsoftware/issrc/tree/main/Files/Languages/Unofficial
; Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]

const
  RuntimeRegKey =
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

{ 强制结束正在运行的实例。这是托盘程序，设置每次变更都会立即落盘，
  强杀不会丢数据。进程不存在时 taskkill 返回 128，属正常情况。 }
procedure KillApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'),
       '/IM {#AppExeName} /F',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ 安装/卸载前若程序在运行，先征求用户同意。 }
function CloseRunningApp(): Boolean;
begin
  Result := True;
  if CheckForMutexes('{#AppMutex}') then
  begin
    if MsgBox('FloatSpotify Next 正在运行，需要先关闭才能继续。' + #13#10 + #13#10 +
              '是否立即关闭？', mbConfirmation, MB_YESNO) = IDYES then
      KillApp()
    else
      Result := False;
  end;
end;

#ifdef ONLINE
{ 检测 .NET 8 桌面运行时是否已安装。先查注册表（不依赖安装路径），
  再用默认安装目录兜底。 }
function HasDotNet8DesktopRuntime(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  FindRec: TFindRec;
begin
  Result := False;

  if RegGetValueNames(HKLM, RuntimeRegKey, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
    begin
      if Pos('8.', Names[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;

  if FindFirst(ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
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
end;

{ 下载并静默安装 .NET 8 桌面运行时。返回 3010 表示成功但需重启。 }
function InstallDotNet8DesktopRuntime(): Boolean;
var
  FileName: String;
  ResultCode: Integer;
begin
  Result := False;
  FileName := 'windowsdesktop-runtime-8-x64.exe';

  { 该函数返回 Int64：成功时为下载字节数，失败时 <= 0。 }
  if DownloadTemporaryFile('{#RuntimeUrl}', FileName, '', nil) <= 0 then
    Exit;

  if not Exec(ExpandConstant('{tmp}\' + FileName),
              '/install /quiet /norestart',
              '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;

  Result := (ResultCode = 0) or (ResultCode = 3010);
end;
#endif

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if not CloseRunningApp() then
  begin
    Result := '安装已取消：请先退出 FloatSpotify Next 后重试。';
    Exit;
  end;

#ifdef ONLINE
  if not HasDotNet8DesktopRuntime() then
  begin
    if not InstallDotNet8DesktopRuntime() then
      Result := '.NET 8 桌面运行时安装失败，无法继续。' + #13#10 + #13#10 +
                '请手动安装后重新运行本安装程序：' + #13#10 + '{#RuntimeUrl}';
  end;
#endif
end;

{ 卸载前关闭程序；用户数据（%LocalAppData%\FloatSpotify.Next）保留不动，
  这样重装后无需重新授权 Spotify。 }
function InitializeUninstall(): Boolean;
begin
  Result := CloseRunningApp();
  if Result then
    KillApp();
end;
