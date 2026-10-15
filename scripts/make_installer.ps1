# KFTCTaxCAP 설치 파일(Inno Setup) 생성기.
#
# 1) 작업 트리를 Release로 installer\build\에 따로 빌드한다(작업 중 수정분도 들어간다 — 배포본은 커밋한 뒤 만든다).
#    bin\Release를 쓰지 않는 것은 개발 중 실행해 둔 앱(관리자 권한)이 그 폴더 파일을 잡고 있어 정리가 막히기 때문이다.
# 2) VC++ 2015-2022 런타임(x86)을 installer\redist\에 준비한다. 없으면 Microsoft 공식 주소에서 받고,
#    Microsoft 서명을 확인한다(ReaderSerial.dll이 MSVCP140/VCRUNTIME140에 의존 — 깨끗한 PC에서 리더기 DLL 로드 실패 방지).
# 3) ISCC로 컴파일한다. 결과: delivery\KFTCTaxCAP_Setup_<Version>.exe
#
# 사용: powershell -ExecutionPolicy Bypass -File scripts\make_installer.ps1
#   버전은 src\KFTCTaxCAP\KFTCTaxCAP.csproj의 <Version>이 정본이다(화면 표기와 같은 값, PRD_WPF.md §3.2).
#   -Version은 그 값과 다를 때 멈추게 하는 확인용이다(설치 파일 이름과 화면 버전이 어긋나는 것 방지).
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $repo "installer\KFTCTaxCAP_Setup.iss"
$redistDir = Join-Path $repo "installer\redist"
$redist = Join-Path $redistDir "vc_redist.x86.exe"
$binDir = Join-Path $repo "installer\build"
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"

if (-not (Test-Path $iscc)) { throw "Inno Setup 6이 없습니다: $iscc" }

$rev = (git -C $repo rev-parse --short HEAD).Trim()
$dirty = git -C $repo status --porcelain -- src
if ($dirty) { Write-Warning "src에 커밋하지 않은 변경이 있습니다 — 그대로 설치 파일에 들어갑니다." }

$csproj = Join-Path $repo "src\KFTCTaxCAP\KFTCTaxCAP.csproj"
$projVersion = ([xml](Get-Content $csproj -Raw -Encoding UTF8)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $projVersion) { throw "KFTCTaxCAP.csproj에 <Version>이 없습니다" }
if ($Version -and $Version -ne $projVersion) { throw "-Version $Version 이 프로젝트 버전 $projVersion 과 다릅니다 — csproj의 <Version>을 고치세요" }
$Version = $projVersion
# 기동 로그의 [버전] 줄에 커밋 번호가 남는다(InformationalVersion = 1.0.0+커밋). 미커밋 변경이 있으면 -dirty.
$sourceRev = $rev + $(if ($dirty) { "-dirty" } else { "" })

# 1) Release 빌드. 이전 빌드에서 남은 파일이 섞이지 않게 출력 폴더를 비운다.
if (Test-Path $binDir) { Remove-Item -Recurse -Force $binDir }
dotnet build (Join-Path $repo "src\KFTCTaxCAP\KFTCTaxCAP.csproj") -c Release -nologo -v q "-p:OutputPath=$binDir\" -p:KftcDistribution=true "-p:SourceRevisionId=$sourceRev"
if ($LASTEXITCODE -ne 0) { throw "Release 빌드 실패" }
foreach ($f in "KFTCTaxCAP.exe", "KFTCTaxCAP.exe.config", "ReaderSerial.dll", "KFTC_GIRO.dll", "MASTKEY2.key", "e_sqlite3.dll") {
    if (-not (Test-Path (Join-Path $binDir $f))) { throw "빌드 출력에 $f 가 없습니다" }
}

# 2) VC++ 런타임
if (-not (Test-Path $redist)) {
    New-Item -ItemType Directory -Force $redistDir | Out-Null
    Write-Host "VC++ 런타임(x86) 다운로드..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest "https://aka.ms/vs/17/release/vc_redist.x86.exe" -OutFile $redist -UseBasicParsing
}
$sig = Get-AuthenticodeSignature $redist
if ($sig.Status -ne "Valid" -or $sig.SignerCertificate.Subject -notmatch "O=Microsoft Corporation") {
    Remove-Item -Force $redist
    throw "vc_redist.x86.exe 서명이 Microsoft 서명이 아닙니다(삭제함): $($sig.Status)"
}
Write-Host ("VC++ 런타임: " + (Get-Item $redist).VersionInfo.ProductVersion)

# 3) 컴파일
& $iscc /Q "/DAppVersion=$Version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC 컴파일 실패" }

$out = Join-Path $repo "delivery\KFTCTaxCAP_Setup_$Version.exe"
Write-Host "완료: $out (커밋 $rev$(if ($dirty) { ' + 미커밋 변경' }))"
