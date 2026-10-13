using System;
using System.IO;
using System.Security.Cryptography;
using KFTCTaxCAP.Security;

namespace KFTCTaxCAP.Services.Payment;

/// <summary>
/// <c>902614</c> 승인요청 전문의 <c>#51</c>(암호화된 비밀번호 정보, ANS 100)에 넣을 값을 만드는 유일한
/// 지점(development_plan.md Phase 18 P18-5, Phase 44 P44-3). 방식은 PRD §16이 정본이다 — 마스터키
/// (<c>MASTKEY2.key</c>)에서 거래마다 새 난수 2개로 세션키를 유도해 PIN 블록을 SEED로 암호화한다.
/// 호출부(<c>PaymentOrchestrator.FillCardApprovalFields</c>)는 <see cref="ToTelegramValue"/> 하나만 안다.
///
/// <b>타입(Phase 25 P25-4)</b>: 입력·반환 모두 <c>char[]</c>다. <b>반환값은 입력과 무관한 새 배열</b>이므로
/// 호출부가 전문에 쓴 직후 <see cref="SecureClear"/>로 지워야 한다(입력 <c>pin</c>의 클리어로는 커버되지 않는다).
/// 이 클래스는 자기가 만든 중간 버퍼(마스터키·세션키·평문 블록·암호문·난수)를 전부 직접 지운다.
/// </summary>
internal static class PinFieldEncoder
{
    /// <summary>마스터키 파일명 — 실행 파일 옆에 복사된다(csproj의 <c>None</c> 항목).</summary>
    internal const string MasterKeyFileName = "MASTKEY2.key";

    /// <summary>마스터키 파일 비밀번호 — 기존 시스템과 같은 방식으로 앱 상수(PRD §16.1, 사용자 확정).</summary>
    internal const string MasterKeyPassword = "kftc123!@";

    private const int FieldLength = 100;
    private const int HexLength = 32;

    /// <summary>
    /// 입력받은 4자리 PIN을 #51에 넣을 값(100자)으로 바꾼다. 키 파일 없음·손상·비밀번호 불일치는
    /// <see cref="InvalidOperationException"/>으로 전파되며 호출부의 기존 내부 오류 경로(<c>InternalError</c>)를 탄다.
    /// 예외 메시지에는 PIN·키 값을 담지 않는다.
    /// </summary>
    internal static char[] ToTelegramValue(char[]? pin)
    {
        // VAN이 조용히 거절하기 전에 여기서 드러나야 한다(FillCardApprovalFields의 #43 길이 체크와
        // 같은 방어적 검증 스타일). PIN 값 자체는 예외 메시지에도 담지 않는다 — 로그 금지 규칙은
        // 예외 텍스트에도 동일하게 적용된다.
        if (pin == null || pin.Length != 4)
        {
            throw new InvalidOperationException(
                $"#51에 넣을 PIN이 4자리가 아님(값은 로그·예외 메시지에 남기지 않음): 길이={pin?.Length.ToString() ?? "null"}");
        }

        foreach (char c in pin)
        {
            if (c < '0' || c > '9')
                throw new InvalidOperationException("#51에 넣을 PIN에 숫자가 아닌 문자가 포함됨(값은 로그·예외 메시지에 남기지 않음)");
        }

        byte[] rnd1 = new byte[16];
        byte[] rnd2 = new byte[16];
        try
        {
            string keyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MasterKeyFileName);
            using (MasterKey key = MasterKeyFile.Load(keyPath, MasterKeyPassword))
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(rnd1);
                rng.GetBytes(rnd2);
                return Encrypt(pin, key, rnd1, rnd2);
            }
        }
        finally
        {
            SecureClear.Clear(rnd1);
            SecureClear.Clear(rnd2);
        }
    }

    /// <summary>
    /// 결정적 암호화(자가진단 KAT용으로 분리). <c>k1 = SEED(마스터키, rnd1)</c>, <c>k2 = SEED(k1, rnd2)</c>,
    /// <c>enc = SEED(k2, PIN ‖ 0x80 ‖ 0x00…)</c> → HEX대문자(rnd1)32 + HEX(rnd2)32 + HEX(enc)32 + 키버전 2자리 + 공백 2.
    /// PIN 길이는 1~15를 허용한다(KAT가 2자리) — 운영 경로의 4자리 검증은 <see cref="ToTelegramValue"/>가 한다.
    /// </summary>
    internal static char[] Encrypt(char[] pin, MasterKey key, byte[] rnd1, byte[] rnd2)
    {
        if (pin == null || pin.Length < 1 || pin.Length > 15)
            throw new ArgumentException("PIN 길이는 1~15여야 한다.", nameof(pin));
        if (rnd1 == null || rnd1.Length != 16 || rnd2 == null || rnd2.Length != 16)
            throw new ArgumentException("난수는 각각 16바이트여야 한다.");

        byte[]? k1 = null;
        byte[]? k2 = null;
        byte[]? blk = null;
        byte[]? enc = null;
        try
        {
            k1 = new byte[16];
            using (var seed = new SeedCipher(key.Key))
                seed.EncryptBlock(rnd1, 0, k1, 0);

            k2 = new byte[16];
            using (var seed = new SeedCipher(k1))
                seed.EncryptBlock(rnd2, 0, k2, 0);

            // ISO/IEC 7816-4 패딩: PIN(ASCII) ‖ 0x80 ‖ 0x00…
            blk = new byte[16];
            for (int i = 0; i < pin.Length; i++)
                blk[i] = (byte)pin[i];
            blk[pin.Length] = 0x80;

            enc = new byte[16];
            using (var seed = new SeedCipher(k2))
                seed.EncryptBlock(blk, 0, enc, 0);

            char[] field = new char[FieldLength];
            WriteHex(rnd1, field, 0);
            WriteHex(rnd2, field, HexLength);
            WriteHex(enc, field, HexLength * 2);
            string version = key.Version.ToString("D2");
            field[HexLength * 3] = version[0];
            field[HexLength * 3 + 1] = version[1];
            field[HexLength * 3 + 2] = ' ';
            field[HexLength * 3 + 3] = ' ';
            return field;
        }
        finally
        {
            SecureClear.Clear(k1);
            SecureClear.Clear(k2);
            SecureClear.Clear(blk);
            SecureClear.Clear(enc);
        }
    }

    /// <summary>
    /// 자가진단 전용 역연산 — 운영 경로에서 호출하지 않는다. <paramref name="field"/>는 100자 또는 끝 공백이
    /// 잘린 98자(<c>PosTelegram.Read</c>의 ANS trim 결과)를 받는다. 형식 불일치는 <see cref="FormatException"/>.
    /// 반환 <c>char[]</c>는 호출자가 지운다.
    /// </summary>
    internal static char[] DecryptForVerification(string field, MasterKey key)
    {
        if (field == null || (field.Length != FieldLength && field.Length != HexLength * 3 + 2))
            throw new FormatException("#51 길이가 98/100자가 아님");
        for (int i = HexLength * 3 + 2; i < field.Length; i++)
        {
            if (field[i] != ' ')
                throw new FormatException("#51 끝 공백 영역에 공백이 아닌 문자가 있음");
        }

        byte[]? rnd2 = null;
        byte[]? k1 = null;
        byte[]? k2 = null;
        byte[]? enc = null;
        byte[]? blk = null;
        try
        {
            byte[] rnd1 = ReadHex(field, 0);
            rnd2 = ReadHex(field, HexLength);
            enc = ReadHex(field, HexLength * 2);
            if (field.Substring(HexLength * 3, 2) != key.Version.ToString("D2"))
                throw new FormatException("#51 키버전이 마스터키와 다름");

            k1 = new byte[16];
            using (var seed = new SeedCipher(key.Key))
                seed.EncryptBlock(rnd1, 0, k1, 0);
            SecureClear.Clear(rnd1);

            k2 = new byte[16];
            using (var seed = new SeedCipher(k1))
                seed.EncryptBlock(rnd2, 0, k2, 0);

            blk = new byte[16];
            using (var seed = new SeedCipher(k2))
                seed.DecryptBlock(enc, 0, blk, 0);

            int end = 15;
            while (end >= 0 && blk[end] == 0x00)
                end--;
            if (end < 1 || blk[end] != 0x80)
                throw new FormatException("#51 PIN 블록 패딩이 올바르지 않음");

            char[] pin = new char[end];
            for (int i = 0; i < end; i++)
                pin[i] = (char)blk[i];
            return pin;
        }
        finally
        {
            SecureClear.Clear(rnd2);
            SecureClear.Clear(k1);
            SecureClear.Clear(k2);
            SecureClear.Clear(enc);
            SecureClear.Clear(blk);
        }
    }

    private static void WriteHex(byte[] src, char[] dst, int dstOff)
    {
        const string digits = "0123456789ABCDEF";
        for (int i = 0; i < src.Length; i++)
        {
            dst[dstOff + i * 2] = digits[src[i] >> 4];
            dst[dstOff + i * 2 + 1] = digits[src[i] & 0x0F];
        }
    }

    private static byte[] ReadHex(string s, int off)
    {
        byte[] result = new byte[HexLength / 2];
        for (int i = 0; i < result.Length; i++)
        {
            int hi = HexValue(s[off + i * 2]);
            int lo = HexValue(s[off + i * 2 + 1]);
            result[i] = (byte)(hi << 4 | lo);
        }
        return result;
    }

    private static int HexValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        throw new FormatException("#51 HEX 영역에 대문자 16진수가 아닌 문자가 있음");
    }
}
