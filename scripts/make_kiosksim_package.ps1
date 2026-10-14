# 키오스크 시뮬레이터(KioskSim) 외부 업체 전달용 zip 생성기.
#
# 커밋된 소스(기본 HEAD)를 git archive로 꺼내 그 소스로 Release 빌드한다 — zip 안의 실행 파일과 소스가
# 정확히 같은 커밋에서 나오게 하기 위함이다(작업 중인 수정분은 들어가지 않으니, 먼저 커밋한다).
# 결과: delivery\KFTCTaxCAP_KioskSim_yyyyMMdd.zip
#   KFTCTaxCAP_KioskSim\읽어주세요.txt
#   KFTCTaxCAP_KioskSim\실행파일\KFTCTaxCAP.KioskSim.exe(.config)
#   KFTCTaxCAP_KioskSim\소스\...   (src\KFTCTaxCAP.KioskSim 폴더 그대로)
# 내부 전용 오류 주입 도구(src\KFTCTaxCAP.ErrorInjector)는 넣지 않는다.
#
# 사용: powershell -ExecutionPolicy Bypass -File scripts\make_kiosksim_package.ps1 [-Commit <ref>]
param(
    [string]$Commit = "HEAD"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$date = Get-Date -Format "yyyyMMdd"
$name = "KFTCTaxCAP_KioskSim"
$work = Join-Path $env:TEMP ("kiosksim_pkg_" + [Guid]::NewGuid().ToString("N"))
$stage = Join-Path $work $name
$srcDir = Join-Path $stage "소스"
$exeDir = Join-Path $stage "실행파일"
$buildDir = Join-Path $work "build"
$outDir = Join-Path $repo "delivery"
$zip = Join-Path $outDir "${name}_$date.zip"

New-Item -ItemType Directory -Force $srcDir, $exeDir, $buildDir, $outDir | Out-Null
try {
    $rev = (git -C $repo rev-parse --short $Commit).Trim()
    if ($LASTEXITCODE -ne 0) { throw "커밋을 찾을 수 없음: $Commit" }

    # 1) 커밋된 소스 꺼내기 — zip 형식으로 받아 .NET으로 푼다(tar.exe는 한글 파일명 "연쇄 필드 정리.txt"를
    #    처리하지 못하고, Git Bash에서 실행하면 /usr/bin/tar가 "C:"를 원격 호스트로 해석한다).
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $srcZip = Join-Path $work "src.zip"
    git -C $repo archive --format=zip -o $srcZip "${Commit}:src/KFTCTaxCAP.KioskSim"
    if ($LASTEXITCODE -ne 0) { throw "git archive 실패" }
    foreach ($dest in $srcDir, $buildDir) {
        [IO.Compression.ZipFile]::ExtractToDirectory($srcZip, $dest)
    }

    # 2) 같은 소스로 Release 빌드(작업 폴더에서 — 저장소의 bin\obj는 건드리지 않는다)
    dotnet build -c Release (Join-Path $buildDir "KFTCTaxCAP.KioskSim.sln") -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }
    $bin = Join-Path $buildDir "bin\Release\net48"
    Copy-Item (Join-Path $bin "KFTCTaxCAP.KioskSim.exe"), (Join-Path $bin "KFTCTaxCAP.KioskSim.exe.config") $exeDir

    # 3) 안내 파일
    $readme = @"
KFTCTaxCAP 키오스크 시뮬레이터 ($date, 소스 커밋 $rev)

실행파일\KFTCTaxCAP.KioskSim.exe
  - 바로 실행할 수 있는 시뮬레이터입니다(.NET Framework 4.8 필요, Windows 10 이상은 기본 설치됨).
  - KFTCTaxCAP(원캡)이 같은 PC에서 먼저 실행되어 있어야 합니다(127.0.0.1:8002).
  - .exe.config 파일을 같은 폴더에 함께 두세요.

소스\
  - 시뮬레이터 전체 소스(Visual Studio에서 KFTCTaxCAP.KioskSim.sln을 열면 바로 빌드됩니다).
  - 먼저 README.md를 읽어 주세요 — 통신 프레임, 전문 간 필드 연쇄, 직전 거래 조회(999999),
    0 고정 필드 규칙이 정리되어 있습니다.
  - 연쇄 필드 요약표: 연쇄 필드 정리.txt
  - PowerShell 최소 예제: tools\spec_client.ps1

기준 SPEC: 국세 베리어프리 키오스크용 전산설계서(POS-원캡) 2026-10-13판
소스의 필드 정의는 참고용 사본이며, SPEC 원문과 다르면 SPEC을 우선합니다.
"@
    [IO.File]::WriteAllText((Join-Path $stage "읽어주세요.txt"), ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding $true))

    # 4) 압축(한글 파일명은 UTF-8로 저장되어 Windows 기본 압축 풀기에서 깨지지 않는다)
    if (Test-Path $zip) { Remove-Item $zip -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

    Write-Host "생성: $zip (소스 커밋 $rev)"
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
