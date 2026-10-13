using System;
using System.IO;
using System.Security.Cryptography;

namespace KFTCTaxCAP.Security;

/// <summary>
/// <see cref="MasterKeyFile.Load"/>가 꺼낸 마스터키(키버전 + 16바이트 키). <see cref="Dispose"/>가 키를 지운다.
/// </summary>
internal sealed class MasterKey : IDisposable
{
    public int Version { get; }
    public byte[] Key { get; }

    internal MasterKey(int version, byte[] key)
    {
        Version = version;
        Key = key;
    }

    public void Dispose() => SecureClear.Clear(Key);
}

/// <summary>
/// 금융결제원 마스터키 파일(<c>MASTKEY2.key</c>)을 열어 키버전·마스터키(16바이트)를 꺼낸다
/// (Phase 44 P44-2, PRD.md §16.1). 구조는 국내 공동인증서 개인키(<c>signPri.key</c>)와 같은
/// PBES(pbeWithSHA1AndSEED-CBC, OID 1.2.410.200004.1.15)다.
///
/// <b>처리 원칙(PRD §16.3)</b> — 거래마다 읽고 쓴 뒤 <see cref="MasterKey.Dispose"/>로 지운다(상주 금지).
/// 파일 바이트·DK·IV·CBC 평문 등 모든 중간 버퍼는 <c>finally</c>에서 <see cref="SecureClear"/>로 지운다.
/// 실패 사유는 <see cref="InvalidOperationException"/>이며 메시지에는 원인 종류와 경로만 담는다 —
/// 키·DK·평문·비밀번호는 어떤 예외 메시지에도 넣지 않는다.
/// </summary>
internal static class MasterKeyFile
{
    /// <summary>1.2.410.200004.1.15 (pbeWithSHA1AndSEED-CBC)의 DER 값 바이트.</summary>
    private static readonly byte[] ExpectedOid = { 0x2A, 0x83, 0x1A, 0x8C, 0x9A, 0x44, 0x01, 0x0F };

    private const byte TagInteger = 0x02;
    private const byte TagOctetString = 0x04;
    private const byte TagOid = 0x06;
    private const byte TagSequence = 0x30;

    /// <summary>키 파일을 읽어 복호화한다. 호출자가 반환값을 <c>using</c>으로 정리해야 한다.</summary>
    internal static MasterKey Load(string path, string password)
    {
        byte[]? file = null;
        byte[]? salt = null;
        byte[]? passwordBytes = null;
        byte[]? hashInput = null;
        byte[]? dk = null;
        byte[]? ivSource = null;
        byte[]? iv = null;
        byte[]? encKey = null;
        byte[]? cipherText = null;
        byte[]? plain = null;
        byte[]? keyCopy = null;
        try
        {
            try
            {
                file = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                throw new InvalidOperationException($"마스터키 파일을 읽을 수 없음(원인: {ex.GetType().Name}): {path}");
            }

            // SEQUENCE { SEQUENCE { OID, SEQUENCE { OCTET STRING salt, INTEGER iter } }, OCTET STRING enc }
            int pos = 0;
            int outerEnd = EnterConstructed(file, ref pos, file.Length, TagSequence, path);
            int algEnd = EnterConstructed(file, ref pos, outerEnd, TagSequence, path);

            ReadHeader(file, ref pos, algEnd, TagOid, out int oidLen, path);
            if (!SpanEquals(file, pos, oidLen, ExpectedOid))
                throw new InvalidOperationException($"마스터키 파일의 알고리즘 OID가 pbeWithSHA1AndSEED-CBC가 아님: {path}");
            pos += oidLen;

            int paramEnd = EnterConstructed(file, ref pos, algEnd, TagSequence, path);
            ReadHeader(file, ref pos, paramEnd, TagOctetString, out int saltLen, path);
            salt = new byte[saltLen];
            Buffer.BlockCopy(file, pos, salt, 0, saltLen);
            pos += saltLen;

            ReadHeader(file, ref pos, paramEnd, TagInteger, out int iterLen, path);
            if (iterLen < 1 || iterLen > 4)
                throw new InvalidOperationException($"마스터키 파일의 반복횟수 형식이 올바르지 않음: {path}");
            long iterations = 0;
            for (int i = 0; i < iterLen; i++)
                iterations = (iterations << 8) | file[pos + i];
            if (iterations < 1 || iterations > 100000)
                throw new InvalidOperationException($"마스터키 파일의 반복횟수가 허용 범위 밖: {path}");

            pos = algEnd;
            ReadHeader(file, ref pos, outerEnd, TagOctetString, out int encLen, path);
            if (encLen < 16 || encLen % 16 != 0)
                throw new InvalidOperationException($"마스터키 파일의 암호문 길이가 16의 배수가 아님: {path}");
            cipherText = new byte[encLen];
            Buffer.BlockCopy(file, pos, cipherText, 0, encLen);

            // PBKDF1-SHA1: DK = SHA1^iter(password || salt). SHA1 적용 횟수 = iter.
            passwordBytes = System.Text.Encoding.ASCII.GetBytes(password);
            hashInput = new byte[passwordBytes.Length + salt.Length];
            Buffer.BlockCopy(passwordBytes, 0, hashInput, 0, passwordBytes.Length);
            Buffer.BlockCopy(salt, 0, hashInput, passwordBytes.Length, salt.Length);

            using (SHA1 sha1 = SHA1.Create())
            {
                // 반복 중간값도 dk에 바로 담아 둔다 — 반복 도중 예외가 나도 바깥 finally가 마지막 중간값을 지운다.
                dk = sha1.ComputeHash(hashInput);
                for (long i = 1; i < iterations; i++)
                {
                    byte[] next = sha1.ComputeHash(dk);
                    SecureClear.Clear(dk);
                    dk = next;
                }

                encKey = new byte[16];
                Buffer.BlockCopy(dk, 0, encKey, 0, 16);

                ivSource = new byte[4];
                Buffer.BlockCopy(dk, 16, ivSource, 0, 4);
                byte[] ivFull = sha1.ComputeHash(ivSource);
                iv = new byte[16];
                Buffer.BlockCopy(ivFull, 0, iv, 0, 16);
                SecureClear.Clear(ivFull);
            }

            // SEED-CBC 복호화(블록 단위 직접 구현).
            plain = new byte[encLen];
            byte[] block = new byte[16];
            try
            {
                using (var seed = new SeedCipher(encKey))
                {
                    for (int off = 0; off < encLen; off += 16)
                    {
                        seed.DecryptBlock(cipherText, off, block, 0);
                        for (int j = 0; j < 16; j++)
                            plain[off + j] = (byte)(block[j] ^ (off == 0 ? iv[j] : cipherText[off - 16 + j]));
                    }
                }
            }
            finally
            {
                SecureClear.Clear(block);
            }

            // PKCS#7 패딩 검증 — 비밀번호가 틀리면 여기서 대부분 걸린다.
            int pad = plain[encLen - 1];
            if (pad < 1 || pad > 16)
                throw new InvalidOperationException($"마스터키 파일 복호화 실패(패딩 오류 — 비밀번호 불일치 또는 파일 손상): {path}");
            for (int i = 0; i < pad; i++)
            {
                if (plain[encLen - 1 - i] != pad)
                    throw new InvalidOperationException($"마스터키 파일 복호화 실패(패딩 오류 — 비밀번호 불일치 또는 파일 손상): {path}");
            }
            int plainLen = encLen - pad;

            // SEQUENCE { INTEGER version, OCTET STRING(16) key }
            int p = 0;
            int seqEnd = EnterConstructed(plain, ref p, plainLen, TagSequence, path);
            ReadHeader(plain, ref p, seqEnd, TagInteger, out int verLen, path);
            if (verLen < 1 || verLen > 2)
                throw new InvalidOperationException($"마스터키 파일의 키버전 형식이 올바르지 않음: {path}");
            int version = 0;
            for (int i = 0; i < verLen; i++)
                version = (version << 8) | plain[p + i];
            p += verLen;
            if (version < 0 || version > 99)
                throw new InvalidOperationException($"마스터키 파일의 키버전이 0~99 범위 밖: {path}");

            ReadHeader(plain, ref p, seqEnd, TagOctetString, out int keyLen, path);
            if (keyLen != 16)
                throw new InvalidOperationException($"마스터키 파일의 키 길이가 16바이트가 아님: {path}");
            keyCopy = new byte[16];
            Buffer.BlockCopy(plain, p, keyCopy, 0, 16);

            var result = new MasterKey(version, keyCopy);
            keyCopy = null; // 소유권이 MasterKey로 넘어갔다 — finally에서 지우지 않는다.
            return result;
        }
        finally
        {
            SecureClear.Clear(file);
            SecureClear.Clear(salt);
            SecureClear.Clear(passwordBytes);
            SecureClear.Clear(hashInput);
            SecureClear.Clear(dk);
            SecureClear.Clear(ivSource);
            SecureClear.Clear(iv);
            SecureClear.Clear(encKey);
            SecureClear.Clear(cipherText);
            SecureClear.Clear(plain);
            SecureClear.Clear(keyCopy);
        }
    }

    /// <summary>태그 <paramref name="tag"/>의 구성(constructed) 요소 안으로 들어가고, 그 요소의 끝 위치를 돌려준다.</summary>
    private static int EnterConstructed(byte[] data, ref int pos, int limit, byte tag, string path)
    {
        ReadHeader(data, ref pos, limit, tag, out int len, path);
        return pos + len;
    }

    /// <summary>DER 태그·길이(단형/장형)를 읽는다. <paramref name="pos"/>는 값 시작으로 이동하고 값 범위가 <paramref name="limit"/> 안인지 검사한다.</summary>
    private static void ReadHeader(byte[] data, ref int pos, int limit, byte tag, out int length, string path)
    {
        if (pos + 2 > limit || data[pos] != tag)
            throw new InvalidOperationException($"마스터키 파일의 DER 구조가 예상과 다름: {path}");
        pos++;

        int first = data[pos++];
        if (first < 0x80)
        {
            length = first;
        }
        else
        {
            int n = first & 0x7F;
            if (n < 1 || n > 3 || pos + n > limit)
                throw new InvalidOperationException($"마스터키 파일의 DER 길이 형식이 올바르지 않음: {path}");
            int v = 0;
            for (int i = 0; i < n; i++)
                v = (v << 8) | data[pos++];
            length = v;
        }

        if (length < 0 || pos + length > limit)
            throw new InvalidOperationException($"마스터키 파일의 DER 길이가 범위를 벗어남: {path}");
    }

    private static bool SpanEquals(byte[] data, int pos, int len, byte[] expected)
    {
        if (len != expected.Length)
            return false;
        for (int i = 0; i < len; i++)
        {
            if (data[pos + i] != expected[i])
                return false;
        }
        return true;
    }
}
