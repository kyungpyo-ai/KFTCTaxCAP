# KFTCTaxCAP(원캡) 최소 연동 예제 — PowerShell만으로 전문 하나를 보내고 응답을 받는다.
#
# 실제 키오스크처럼 501008/800000/902614 고정길이 전문을 127.0.0.1:8002로 보낸다.
# 원캡이 먼저 실행되어 있어야 한다. 사용 예:
#   powershell -ExecutionPolicy Bypass -File spec_client.ps1 -TxType 501008
#
# - 프레임: [본문 길이 4자리 ASCII][본문]. 인코딩 CP949. 한 요청 = 한 TCP 연결.
# - 아래 업무 필드 값은 테스트 값이다. 실제 키오스크는 연쇄 필드(902614 #11~#37 대부분, 800000 #15/#16)를
#   앞 전문(501008/800000) 응답에서 가져와 채운다 — 규칙은 Protocol/TelegramChainMap.cs와 README.md 참고.
# - 필드 위치·길이는 SPEC(전산설계서 POS-원캡, 2026-10-13판)과 Protocol/TelegramSchemas.cs가 정본이다.
param(
    [Parameter(Mandatory=$true)][ValidateSet("501008","800000","902614")][string]$TxType,
    [string]$MgmtNo = "",
    [int]$TimeoutSec = 180
)

$ErrorActionPreference = "Stop"
$cp949 = [System.Text.Encoding]::GetEncoding(949)

$totalLen = @{ "501008" = 706; "800000" = 500; "902614" = 1500 }[$TxType]

# 본문 전체를 space로 초기화 (SPEC: 값이 없는 필드는 space로 채워 총 길이로 전문 생성)
$body = New-Object byte[] $totalLen
for ($i = 0; $i -lt $totalLen; $i++) { $body[$i] = 0x20 }

function Set-Field {
    param([byte[]]$Buffer, [int]$Pos, [int]$Len, [string]$Value, [switch]$Numeric)
    $vb = $cp949.GetBytes($Value)
    if ($vb.Length -gt $Len) { throw "필드(pos=$Pos,len=$Len)에 값이 넘침: $($vb.Length)바이트 '$Value'" }
    if ($vb.Length -eq 0) { return }
    if ($Numeric) {
        # N: 우측정렬 + 앞을 '0'
        for ($i = 0; $i -lt $Len; $i++) { $Buffer[$Pos + $i] = 0x30 }
        [Array]::Copy($vb, 0, $Buffer, $Pos + $Len - $vb.Length, $vb.Length)
    } else {
        # 문자: 좌측정렬 + 뒤는 space(이미 space로 초기화됨)
        [Array]::Copy($vb, 0, $Buffer, $Pos, $vb.Length)
    }
}

# ===== 0 고정 필드 =====
# kiosk·원캡이 채우지 않는 숫자(N) 필드는 공백이 아니라 '0'으로 채워 보내야 한다(서버 요구 — 공백이면
# 서버가 오류 처리). 위치,길이 목록은 Protocol/TelegramSchemas.cs의 zeroFilled: true 필드와 같다.
$zeroFields = @{
    # #5 #13 #15 #23~#28 #30~#39 #47~#52 (25개)
    "501008" = @(@(16,3),@(68,2),@(89,3),@(251,1),@(252,4),@(256,15),@(271,8),@(279,15),@(294,8),
                 @(303,15),@(318,15),@(333,15),@(348,15),@(363,15),@(378,15),@(393,15),@(408,15),@(423,15),@(438,15),
                 @(631,7),@(638,14),@(652,8),@(660,1),@(661,15),@(676,15));
    # #5 #11 #12 #13 #24 #25 #26 (7개)
    "800000" = @(@(16,3),@(59,2),@(61,7),@(68,2),@(261,12),@(273,12),@(285,4));
    # #5 #13 (2개)
    "902614" = @(@(16,3),@(68,2))
}
foreach ($f in $zeroFields[$TxType]) {
    for ($i = 0; $i -lt $f[1]; $i++) { $body[$f[0] + $i] = 0x30 }
}

if ($MgmtNo -eq "") { $MgmtNo = "0EC0" + (Get-Random -Minimum 10000000 -Maximum 99999999).ToString() }
$now = Get-Date

# ===== 공통부 (3전문 동일 레이아웃) =====
Set-Field -Buffer $body -Pos 0  -Len 3  -Value "IGN"                              # #1 업무 구분
Set-Field -Buffer $body -Pos 3  -Len 3  -Value "095" -Numeric                     # #2 요청기관 코드
Set-Field -Buffer $body -Pos 6  -Len 4  -Value "0200" -Numeric                    # #3 전문 종별 코드(요청)
Set-Field -Buffer $body -Pos 10 -Len 6  -Value $TxType -Numeric                   # #4 거래 구분 코드
Set-Field -Buffer $body -Pos 19 -Len 1  -Value "G"                                # #6 송·수신 FLAG(요청기관)
Set-Field -Buffer $body -Pos 23 -Len 12 -Value $now.ToString("yyMMddHHmmss") -Numeric  # #8 전송 일시
Set-Field -Buffer $body -Pos 35 -Len 12 -Value $MgmtNo                            # #9 전문 관리 번호
if ($TxType -ne "800000") {
    # 800000의 #11/#12는 kiosk가 채우지 않는다(위에서 0 고정). 902614는 501008 응답 #11/#12에서 온다.
    Set-Field -Buffer $body -Pos 59 -Len 2  -Value "01" -Numeric                  # #11 이용기관 분류코드
    Set-Field -Buffer $body -Pos 61 -Len 7  -Value "1234567" -Numeric             # #12 이용기관 지로번호
}

# ===== 업무부 (전문별) =====
switch ($TxType) {
    "501008" {
        Set-Field -Buffer $body -Pos 70 -Len 19 -Value "1234567890123456789"      # #14 전자납부번호
    }
    "800000" {
        # #14 마스킹 카드번호(pos 70, 19)는 원캡이 카드 리딩으로 채운다 — 공백으로 둔다
        Set-Field -Buffer $body -Pos 89  -Len 15 -Value "1000" -Numeric           # #15 납부세액 (501008 응답 #30+#31+#32)
        Set-Field -Buffer $body -Pos 104 -Len 2  -Value "10"                      # #16 납세자 유형 (501008 응답 #55)
    }
    "902614" {
        Set-Field -Buffer $body -Pos 70  -Len 13 -Value "8001011234567"           # #14 주민등록번호
        Set-Field -Buffer $body -Pos 83  -Len 19 -Value "1234567890123456789"     # #15 전자납부번호
        Set-Field -Buffer $body -Pos 102 -Len 3  -Value "001"                     # #16 납부 순번
        Set-Field -Buffer $body -Pos 113 -Len 7  -Value "2601510" -Numeric        # #18 징수 과목 코드
        Set-Field -Buffer $body -Pos 120 -Len 6  -Value "123456"                  # #19 징수관 계좌번호
        Set-Field -Buffer $body -Pos 126 -Len 20 -Value "강남세무서"                # #20 징수 기관명(AHN)
        Set-Field -Buffer $body -Pos 146 -Len 20 -Value "부가가치세"                # #21 징수 과목명(AHN)
        Set-Field -Buffer $body -Pos 166 -Len 1  -Value "0" -Numeric              # #22 소계정
        Set-Field -Buffer $body -Pos 167 -Len 4  -Value "2026" -Numeric           # #23 징수 결의 회계 년도
        Set-Field -Buffer $body -Pos 171 -Len 15 -Value "1000" -Numeric           # #24 납부세액(본세)
        Set-Field -Buffer $body -Pos 186 -Len 15 -Value "0" -Numeric              # #25 납부세액(교육세)
        Set-Field -Buffer $body -Pos 201 -Len 15 -Value "0" -Numeric              # #26 납부세액(농어촌특별세)
        Set-Field -Buffer $body -Pos 216 -Len 15 -Value "1000" -Numeric           # #27 납부 세액
        Set-Field -Buffer $body -Pos 231 -Len 15 -Value "0" -Numeric              # #28 수수료
        Set-Field -Buffer $body -Pos 246 -Len 15 -Value "1000" -Numeric           # #29 총 납부 금액(#27+#28)
        Set-Field -Buffer $body -Pos 261 -Len 1  -Value "1"                       # #30 납기 내후 구분
        Set-Field -Buffer $body -Pos 262 -Len 8  -Value $now.ToString("yyyyMMdd") -Numeric  # #31 납기 일자
        Set-Field -Buffer $body -Pos 270 -Len 8  -Value $now.ToString("yyyyMMdd") -Numeric  # #32 납부 일자
        Set-Field -Buffer $body -Pos 278 -Len 2  -Value "01" -Numeric             # #33 카드사 코드
        Set-Field -Buffer $body -Pos 280 -Len 2  -Value "00" -Numeric             # #34 할부 개월 수(일시불)
        Set-Field -Buffer $body -Pos 282 -Len 14 -Value "01012345678"             # #35 연락 전화 번호
        Set-Field -Buffer $body -Pos 296 -Len 13 -Value "8001011234567"           # #36 납부자 주민등록번호
        Set-Field -Buffer $body -Pos 309 -Len 10 -Value "홍길동"                    # #37 납부자 성명(AHNS)
        Set-Field -Buffer $body -Pos 332 -Len 1  -Value "O"                       # #39 납부 이용 시스템(고정 "O")
        Set-Field -Buffer $body -Pos 334 -Len 1  -Value "Q"                       # #41 납부 형태 구분(베리어프리 조회납부)
        Set-Field -Buffer $body -Pos 335 -Len 20 -Value "1234567890BF0001"        # #42 키오스크 고유번호
        Set-Field -Buffer $body -Pos 610 -Len 1  -Value "0"                       # #49 납부카드 구분(개인카드)
        # #43~#46, #48, #50, #51, #53은 원캡이 카드 리딩·PIN 입력으로 채운다 — 공백으로 둔다
    }
}

# ===== 프레임 전송 =====
$lenBytes = $cp949.GetBytes($totalLen.ToString("D4"))
$client = New-Object System.Net.Sockets.TcpClient
$client.Connect("127.0.0.1", 8002)
$stream = $client.GetStream()

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$stream.Write($lenBytes, 0, 4)
$stream.Write($body, 0, $body.Length)
Write-Host "[$TxType] 요청 전송 완료 (본문 $totalLen bytes, 전체 $($totalLen+4) bytes), #9=$MgmtNo"

# 응답 수신 — TCP는 스트림이라 한 번의 Read로 다 온다는 보장이 없다. 길이 헤더 4바이트를 읽은 뒤
# 그 길이만큼 본문이 다 모일 때까지 반복해서 누적한다.
$stream.ReadTimeout = $TimeoutSec * 1000
$recv = New-Object System.Collections.Generic.List[byte]
try {
    $buf = New-Object byte[] 4096
    while ($true) {
        $n = $stream.Read($buf, 0, $buf.Length)
        if ($n -le 0) { break }
        for ($i = 0; $i -lt $n; $i++) { $recv.Add($buf[$i]) }
        if ($recv.Count -ge 4) {
            $declared = [int]$cp949.GetString($recv.ToArray(), 0, 4)
            if ($recv.Count -ge 4 + $declared) { break }
        }
    }
    $sw.Stop()

    if ($recv.Count -lt 4) { Write-Host "[$TxType] 응답 없음/불완전 ($($recv.Count) bytes)"; exit 1 }

    $all = $recv.ToArray()
    $bodyLen = [int]$cp949.GetString($all, 0, 4)
    if ($all.Length -lt 4 + $bodyLen) { Write-Host "[$TxType] 응답 본문 불완전 (선언 $bodyLen, 수신 $($all.Length - 4) bytes)"; exit 1 }
    $rb = New-Object byte[] $bodyLen
    [Array]::Copy($all, 4, $rb, 0, $bodyLen)

    $respType = $cp949.GetString($rb, 6, 4)
    $flag     = $cp949.GetString($rb, 19, 1)
    $code     = $cp949.GetString($rb, 20, 3)
    $mgmt     = $cp949.GetString($rb, 35, 12).TrimEnd()

    Write-Host ("[{0}] 응답 수신 ({1:N1}초) 본문={2}bytes  #3={3} #6={4} #7=[{5}] #9={6}" -f `
        $TxType, $sw.Elapsed.TotalSeconds, $bodyLen, $respType, $flag, $code, $mgmt)

    # 오류 응답(E41 등)은 공통부 70바이트만 올 수 있다 — 전문별 필드는 본문이 정상 길이일 때만 읽는다.
    if ($bodyLen -eq $totalLen) {
        if ($TxType -eq "800000") {
            Write-Host ("    #14 마스킹 카드번호=[{0}]" -f $cp949.GetString($rb, 70, 19))
        }
        if ($TxType -eq "902614") {
            # 카드·인증 관련 필드는 값 대신 채워진 길이만 보여 준다.
            $lens = @(
                @("#43 보안단말기 인증번호", 355, 32), @("#46 암호화된 카드정보", 407, 196),
                @("#53 EMV DATA", 724, 604))
            foreach ($l in $lens) {
                Write-Host ("    {0}: {1}/{2} bytes" -f $l[0], $cp949.GetString($rb, $l[1], $l[2]).TrimEnd().Length, $l[2])
            }
            Write-Host ("    #48 거래입력유형=[{0}]  #50 인증방식=[{1}]" -f $cp949.GetString($rb, 609, 1), $cp949.GetString($rb, 611, 1))
        }
    }
} catch {
    Write-Host "[$TxType] 예외/타임아웃: $($_.Exception.Message)"
} finally {
    $client.Close()
}
