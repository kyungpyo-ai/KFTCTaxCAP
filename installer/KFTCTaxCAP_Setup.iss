#pragma codepage 65001
; KFTCTaxCAP Inno Setup 스크립트 (Inno Setup 6.x)
; 원본 KFTCOneCAP_Setup.iss를 바탕으로 KFTCTaxCAP에 맞게 다시 만들었다(2026-10-15).
; 원캡(KFTCOneCAP)과 한 PC에 같이 깔려도 서로 건드리지 않도록 이름·AppId·폴더·작업 이름·레지스트리를 모두 분리했다.
;
; 직접 컴파일하지 말고 scripts\make_installer.ps1로 만든다 — Release 빌드, VC++ 런타임 준비, 버전 전달을 한 번에 한다.
;   ISCC 직접 실행 시: ISCC /DAppVersion=1.0.0 installer\KFTCTaxCAP_Setup.iss

#define AppName "KFTCTaxCAP"
#define AppPublisher "KFTC"
#define AppExeName "KFTCTaxCAP.exe"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
; make_installer.ps1이 Release 빌드를 여기에 출력한다.
#define BinDir "build"
#define VcRedist "redist\vc_redist.x86.exe"

[Setup]
; 원캡과 다른 고유 AppId — 바꾸면 기존 설치본을 다른 프로그램으로 인식하므로 고정한다.
AppId={{7DCD0643-2564-4FD3-BC41-185F5AF6D239}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://www.kftcvan.or.kr
VersionInfoVersion={#AppVersion}
DefaultDirName={pf32}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\delivery
OutputBaseFilename=KFTCTaxCAP_Setup_{#AppVersion}
; 설치 파일(setup.exe) 아이콘 — 원캡 설치 파일과 같은 setupico.ico(D:\OneCap\RELEASE\KFTCOneCAP\, 16~256px 9종)를 복사해 뒀다.
; 앱 아이콘(KFTCTaxCAP.ico)과는 별개다 — 설치된 프로그램·바로가기·제거 항목은 계속 앱 아이콘을 쓴다.
SetupIconFile=setupico.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; 앱이 requireAdministrator(로그 경로 C:\KFTC_PosAgent)이고 작업 스케줄러·Defender 예외 등록에 관리자 권한이 필요하다.
PrivilegesRequired=admin
; Windows 7 SP1 이상(net48 요구사항, CLAUDE.md). 앱은 x86 전용이라 아키텍처 제한은 두지 않는다(32bit 모드로 설치).
MinVersion=6.1sp1
; 실행 중인 앱은 PrepareToInstall에서 직접 종료한다.
CloseApplications=no
; 설치 끝에 재시작을 묻지 않는다(2026-10-15). VC++ 런타임이 쓰는 중인 DLL을 "재부팅 때 교체"로 걸어 두면 Inno가
; 기본값(yes)으로 재시작을 권하는데, 앱은 기존 런타임으로도 동작하고 재부팅 없이 바로 실행·상주한다.
RestartIfNeededByRun=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
; Release 출력 폴더 전체(exe·.exe.config·관리 DLL·ReaderSerial.dll·KFTC_GIRO.dll·e_sqlite3.dll·MASTKEY2.key).
; pdb와 빌드 중 생긴 log 폴더는 넣지 않는다. 하위 폴더는 없다(recursesubdirs 미사용).
Source: "{#BinDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion
; ReaderSerial.dll이 쓰는 VC++ 2015-2022 런타임(x86). 설치 후 지운다.
Source: "{#VcRedist}"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 아이콘 만들기"; GroupDescription: "추가 작업:"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{group}\{#AppName} 제거"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Windows 7은 Microsoft Security Essentials 예외 경로에 직접 쓴다(Defender PowerShell 모듈 없음). Win8+는 [Code]에서 처리.
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Microsoft Antimalware\Exclusions\Paths"; ValueType: dword; ValueName: "{app}"; ValueData: "0"; Flags: uninsdeletevalue noerror; Check: IsWindows7

[Run]
; 1) VC++ 런타임 — 이미 같은/새 버전이 있으면 아무것도 하지 않고 끝난다(종료코드 1638).
Filename: "{tmp}\vc_redist.x86.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Visual C++ 런타임 설치 중..."; Flags: waituntilterminated
; 2) 작업 스케줄러 — 로그온 20초 후 관리자 권한으로 자동 실행(UAC 창 없이 상주 시작, 트레이 준비 전 실행 방지).
Filename: "{sys}\schtasks.exe"; Parameters: "/create /tn ""{#AppName}"" /tr ""\""{app}\{#AppExeName}\"""" /sc onlogon /rl highest /delay 0000:20 /f"; Flags: runhidden waituntilterminated
; 3) schtasks CLI로 지정할 수 없는 설정을 Task Scheduler COM으로 보정한다.
;    ExecutionTimeLimit 기본 72시간 -> 무기한(상주 프로그램이 3일마다 강제 종료되는 것 방지)
;    배터리 관련 2개 -> 해제
;    시작 위치 -> {app}: 지정하지 않으면 작업 폴더가 System32가 되고, KFTC_GIRO.dll이 작업 폴더 기준으로 log\를 만든다.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-ExecutionPolicy Bypass -NonInteractive -Command ""$s=New-Object -ComObject Schedule.Service;$s.Connect();$f=$s.GetFolder('\');$t=$f.GetTask('{#AppName}');$d=$t.Definition;$d.Settings.ExecutionTimeLimit='PT0S';$d.Settings.DisallowStartIfOnBatteries=$false;$d.Settings.StopIfGoingOnBatteries=$false;$d.Actions.Item(1).WorkingDirectory='{app}';$f.RegisterTaskDefinition('{#AppName}',$d,6,$null,$null,$d.Principal.LogonType)|Out-Null"""; Flags: runhidden waituntilterminated
; 4) 설치 완료 후 실행
Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Description: "설치 후 실행"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ""{#AppName}"" /f"; Flags: runhidden; RunOnceId: "DeleteTask"

; 제거 시 데이터는 남긴다(2026-10-15 결정): C:\KFTC_PosAgent\KFTCTaxLog\(로그), %LOCALAPPDATA%\KFTCTaxCAP\(DB),
; HKCU\Software\KFTC_VAN\KFTCTaxCAP\(설정)은 지우지 않는다. 그래서 [UninstallDelete]·레지스트리 삭제 항목이 없다.

[Code]
function IsWindows7: Boolean;
var ver: Cardinal;
begin
  ver := GetWindowsVersion;
  Result := (ver >= $06010000) and (ver < $06020000);
end;

function IsWindows8OrAbove: Boolean;
begin
  Result := (GetWindowsVersion >= $06020000);
end;

// .NET Framework 4.8 (Release >= 528040). Win10 1903+에는 기본 포함, Win7·구버전 Win10은 별도 설치가 필요하다.
function IsDotNet48Installed: Boolean;
var release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release)
            and (release >= 528040);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsDotNet48Installed then
  begin
    MsgBox('이 프로그램을 실행하려면 .NET Framework 4.8이 필요합니다.' #13#10 +
           '.NET Framework 4.8을 먼저 설치한 뒤 다시 실행해 주세요.', mbCriticalError, MB_OK);
    Result := False;
  end;
end;

// 32bit 설치 프로그램에서 64bit PowerShell을 띄운다(Defender cmdlet은 OS 비트수와 맞는 PowerShell에서 확실히 동작).
function GetPSPath: String;
begin
  if IsWin64 then
    Result := GetWinDir + '\Sysnative\WindowsPowerShell\v1.0\powershell.exe'
  else
    Result := GetSystemDir + '\WindowsPowerShell\v1.0\powershell.exe';
end;

// Win8+ Defender 예외 등록/해제. 레지스트리를 직접 쓰지 않는다 — 변조 방지(Tamper Protection)가 켜져 있으면 막히고,
// 제거 때 지우지도 못해 예외가 남는다. Add/Remove-MpPreference는 관리자 권한이면 정상 경로다. 실패해도 설치는 계속한다.
procedure SetDefenderExclusion(Add: Boolean);
var
  PSPath, Cmd: String;
  ResultCode: Integer;
begin
  if not IsWindows8OrAbove then Exit;
  PSPath := GetPSPath;
  if not FileExists(PSPath) then Exit;
  if Add then Cmd := 'Add-MpPreference' else Cmd := 'Remove-MpPreference';
  Exec(PSPath, '-ExecutionPolicy Bypass -NonInteractive -Command "try { ' + Cmd + ' -ExclusionPath ''' +
       ExpandConstant('{app}') + ''' -ErrorAction Stop } catch {}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then SetDefenderExclusion(True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then SetDefenderExclusion(False);
end;

// 실행 중인 KFTCTaxCAP만 종료한다(원캡 KFTCOneCAP.exe는 건드리지 않는다). 앱이 관리자 권한이라 설치 프로그램도 관리자여야 종료된다.
procedure StopRunningApp;
var ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // 종료됐을 때만(0) 파일 잠금 해제를 기다린다. 실행 중이 아니면(128) 바로 진행.
  if ResultCode = 0 then Sleep(1500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp;
  Result := True;
end;
