using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KFTCTaxCAP.Security;
using KFTCTaxCAP.Services.Payment;

namespace KFTCTaxCAP.Services.Diagnostics;

/// <summary>
/// Phase 44 P44-4 — 902614 <c>#51</c> SEED 암호화(<see cref="SeedCipher"/>·<see cref="MasterKeyFile"/>·
/// <see cref="PinFieldEncoder"/>) 자가진단. <c>App.xaml.cs</c>가 <c>--pin-encryption-test</c> 인자로 실행될 때만
/// <see cref="RunAll"/>을 호출한다. 소켓·리더기 불필요, 순수 로직 테스트다.
///
/// 로그에는 테스트 PIN 리터럴(KAT의 <c>12</c> 등)과 통과/실패만 남긴다 — 마스터키·세션키(k1·k2)·
/// 운영 PIN 값은 어떤 로그에도 남기지 않는다.
/// </summary>
internal static class PinEncryptionSelfTest
{
    private const string TestPassword = PinFieldEncoder.MasterKeyPassword;

    private static int _pass;
    private static int _fail;

    public static void RunAll()
    {
        try
        {
            FileLogger.Info("[pin-encryption-test] Phase 44 #51 SEED 암호화 자가진단 시작");
            RunRfc4269Vectors();
            RunMasterKeyAndKat();
            RunOperationalPath();
            RunErrorPaths();
            FileLogger.Info($"[pin-encryption-test] 완료 — 통과 {_pass}건, 실패 {_fail}건, 종합={(_fail == 0 ? "통과" : "실패")}");
        }
        catch (Exception ex)
        {
            FileLogger.Error($"[pin-encryption-test] 하네스 자체 예외로 중단: {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void Check(string name, bool ok)
    {
        if (ok)
        {
            _pass++;
            FileLogger.Info($"[pin-encryption-test][OK] {name}");
        }
        else
        {
            _fail++;
            FileLogger.Error($"[pin-encryption-test][FAIL] {name}");
        }
    }

    private static byte[] Hex(string s)
    {
        byte[] r = new byte[s.Length / 2];
        for (int i = 0; i < r.Length; i++)
            r[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return r;
    }

    private static string ToHex(byte[] b) => string.Concat(b.Select(x => x.ToString("X2")));

    // 1. RFC 4269 벡터 4종 -----------------------------------------------------------------

    private static void RunRfc4269Vectors()
    {
        string[][] vectors =
        {
            new[] { "00000000000000000000000000000000", "000102030405060708090A0B0C0D0E0F", "5EBAC6E0054E166819AFF1CC6D346CDB" },
            new[] { "000102030405060708090A0B0C0D0E0F", "00000000000000000000000000000000", "C11F22F20140505084483597E4370F43" },
            new[] { "4706480851E61BE85D74BFB3FD956185", "83A2F8A288641FB9A4E9A5CC2F131C7D", "EE54D13EBCAE706D226BC3142CD40D4A" },
            new[] { "28DBC3BC49FFD87DCFA509B11D422BE7", "B41E6BE2EBA84A148E2EED84593C5EC7", "9B9B7BFCD1813CB95D0B3618F40F5122" },
        };

        for (int i = 0; i < vectors.Length; i++)
        {
            byte[] key = Hex(vectors[i][0]);
            byte[] plain = Hex(vectors[i][1]);
            byte[] expected = Hex(vectors[i][2]);
            byte[] enc = new byte[16];
            byte[] dec = new byte[16];
            using (var seed = new SeedCipher(key))
            {
                seed.EncryptBlock(plain, 0, enc, 0);
                seed.DecryptBlock(expected, 0, dec, 0);
            }
            Check($"RFC 4269 벡터 {i + 1} 암호화 일치", ToHex(enc) == vectors[i][2]);
            Check($"RFC 4269 벡터 {i + 1} 복호화 일치", ToHex(dec) == vectors[i][1]);
        }
    }

    // 2~4. 마스터키 로드 + KAT ---------------------------------------------------------------

    private static string KeyPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PinFieldEncoder.MasterKeyFileName);

    private static void RunMasterKeyAndKat()
    {
        using (MasterKey key = MasterKeyFile.Load(KeyPath, TestPassword))
        {
            Check("MASTKEY2.key 로드 — 키버전 2", key.Version == 2);
            Check("MASTKEY2.key 로드 — 키 길이 16", key.Key.Length == 16);

            const string rnd1 = "FE7CC5A548A21DDA3986EF43888C5E42";
            const string rnd2 = "155D0C29C67A375AB04A768B11660640";
            const string enc = "7E3BB137EF1A70F0B27BF182E88C257B";
            string expected = rnd1 + rnd2 + enc + "02" + "  ";

            char[] field = PinFieldEncoder.Encrypt("12".ToCharArray(), key, Hex(rnd1), Hex(rnd2));
            Check("KAT(기존 시스템 실데이터, PIN 12) — Encrypt 결과가 원본과 100자 일치", new string(field) == expected);
            SecureClear.Clear(field);

            char[] decrypted = PinFieldEncoder.DecryptForVerification(expected, key);
            Check("KAT — DecryptForVerification(KAT 필드) == 12", new string(decrypted) == "12");
            SecureClear.Clear(decrypted);
        }
    }

    // 5~6. 운영 경로 -------------------------------------------------------------------------

    private static void RunOperationalPath()
    {
        var format = new Regex("^[0-9A-F]{96}02  $");
        string[] pins = { "0000", "1234", "9999", "0507", "8642" };
        using (MasterKey key = MasterKeyFile.Load(KeyPath, TestPassword))
        {
            foreach (string pin in pins)
            {
                char[] field = PinFieldEncoder.ToTelegramValue(pin.ToCharArray());
                string s = new string(field);
                SecureClear.Clear(field);
                char[] back = PinFieldEncoder.DecryptForVerification(s, key);
                // 형식 검사 후 복호화 일치 — 이름에는 PIN 값을 쓰지 않는다(테스트 PIN이지만 규칙 통일).
                Check($"ToTelegramValue(PIN {pin.Length}자리, 표본 {Array.IndexOf(pins, pin) + 1}) — 길이 100, 형식 ^[0-9A-F]{{96}}02  $, 복호화 일치",
                    s.Length == 100 && format.IsMatch(s) && new string(back) == pin);
                SecureClear.Clear(back);
            }

            char[] a = PinFieldEncoder.ToTelegramValue("1234".ToCharArray());
            char[] b = PinFieldEncoder.ToTelegramValue("1234".ToCharArray());
            string sa = new string(a);
            string sb = new string(b);
            SecureClear.Clear(a);
            SecureClear.Clear(b);
            Check("같은 PIN 두 번 — #51 전체가 다름", sa != sb);
            Check("같은 PIN 두 번 — 암호화된값(65~96번째 자)도 다름", sa.Substring(64, 32) != sb.Substring(64, 32));
        }

        bool rejected = false;
        try { PinFieldEncoder.ToTelegramValue("123".ToCharArray()); }
        catch (InvalidOperationException) { rejected = true; }
        Check("4자리가 아닌 PIN은 InvalidOperationException(기존 검증 유지)", rejected);
    }

    // 7. 오류 경로 ---------------------------------------------------------------------------

    private static void RunErrorPaths()
    {
        ExpectInvalidOperation("없는 파일", () => MasterKeyFile.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NO_SUCH_MASTKEY.key"), TestPassword));
        ExpectInvalidOperation("틀린 비밀번호(패딩 오류)", () => MasterKeyFile.Load(KeyPath, "wrong-password-0"));

        string truncated = Path.Combine(Path.GetTempPath(), "pin_enc_selftest_truncated.key");
        try
        {
            byte[] all = File.ReadAllBytes(KeyPath);
            byte[] head = new byte[40];
            Array.Copy(all, head, 40);
            File.WriteAllBytes(truncated, head);
            ExpectInvalidOperation("잘린 파일(앞 40바이트)", () => MasterKeyFile.Load(truncated, TestPassword));
        }
        finally
        {
            try { File.Delete(truncated); } catch (IOException) { }
        }
    }

    private static void ExpectInvalidOperation(string caseName, Action action)
    {
        string? message = null;
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            message = ex.Message;
        }
        catch (Exception ex)
        {
            Check($"오류 경로 [{caseName}] — InvalidOperationException이어야 하나 {ex.GetType().Name}", false);
            return;
        }

        Check($"오류 경로 [{caseName}] — InvalidOperationException 발생", message != null);
        Check($"오류 경로 [{caseName}] — 메시지에 비밀번호 문자열 없음",
            message != null && !message.Contains(TestPassword) && !message.Contains("wrong-password-0"));
    }
}
