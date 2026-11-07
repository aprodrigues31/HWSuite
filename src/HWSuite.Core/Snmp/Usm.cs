using System.Security.Cryptography;
using System.Text;

namespace HWSuite.Core.Snmp;

/// <summary>SNMPv3 authentication protocols (RFC 3414, RFC 7860).</summary>
public enum SnmpAuth
{
    None,
    Md5,
    Sha1,
    Sha224,
    Sha256,
    Sha384,
    Sha512,
}

/// <summary>SNMPv3 privacy protocols: DES-CBC (RFC 3414), AES-CFB (RFC 3826; 192/256 with the Blumenthal key extension).</summary>
public enum SnmpPriv
{
    None,
    Des,
    Aes128,
    Aes192,
    Aes256,
}

/// <summary>
/// User-based Security Model: password-to-key (one megabyte of the repeated password, hashed), localization to the
/// agent's engine id, HMAC authentication and DES/AES privacy.
/// </summary>
#pragma warning disable CA5350, CA5351 // MD5 and SHA-1 are SNMPv3 protocols the devices offer (RFC 3414); SHA-2 and AES are preferred
public static class Usm
{
    /// <summary>Length of the authentication parameter (the truncated HMAC).</summary>
    public static int MacLength(SnmpAuth auth) => auth switch
    {
        SnmpAuth.Md5 or SnmpAuth.Sha1 => 12,
        SnmpAuth.Sha224 => 16,
        SnmpAuth.Sha256 => 24,
        SnmpAuth.Sha384 => 32,
        SnmpAuth.Sha512 => 48,
        _ => 0,
    };

    public static byte[] Hash(SnmpAuth auth, ReadOnlySpan<byte> data) => auth switch
    {
        SnmpAuth.Md5 => MD5.HashData(data),
        SnmpAuth.Sha1 => SHA1.HashData(data),
        SnmpAuth.Sha224 => Sha224(data),
        SnmpAuth.Sha256 => SHA256.HashData(data),
        SnmpAuth.Sha384 => SHA384.HashData(data),
        SnmpAuth.Sha512 => SHA512.HashData(data),
        _ => throw new ArgumentOutOfRangeException(nameof(auth)),
    };

    public static byte[] Hmac(SnmpAuth auth, byte[] key, ReadOnlySpan<byte> data) => auth switch
    {
        SnmpAuth.Md5 => HMACMD5.HashData(key, data),
        SnmpAuth.Sha1 => HMACSHA1.HashData(key, data),
        SnmpAuth.Sha224 => HmacSha224(key, data),
        SnmpAuth.Sha256 => HMACSHA256.HashData(key, data),
        SnmpAuth.Sha384 => HMACSHA384.HashData(key, data),
        SnmpAuth.Sha512 => HMACSHA512.HashData(key, data),
        _ => throw new ArgumentOutOfRangeException(nameof(auth)),
    };

    /// <summary>RFC 3414 A.2: hash of 1,048,576 bytes made of the password repeated.</summary>
    public static byte[] PasswordToKey(SnmpAuth auth, string password)
    {
        var pass = Encoding.UTF8.GetBytes(password);
        if (pass.Length == 0)
        {
            throw new ArgumentException("Empty SNMPv3 password", nameof(password));
        }

        const int total = 1_048_576;
        var buffer = new byte[total];
        for (var i = 0; i < total; i++)
        {
            buffer[i] = pass[i % pass.Length];
        }

        return Hash(auth, buffer);
    }

    /// <summary>RFC 3414 A.2: Kul = H(Ku || engineID || Ku).</summary>
    public static byte[] Localize(SnmpAuth auth, byte[] key, byte[] engineId) =>
        Hash(auth, Ber.Concat(key, engineId, key));

    /// <summary>Localized privacy key, extended (Blumenthal: K || H(K) || H(K || H(K))...) when AES-192/256 need more bytes.</summary>
    public static byte[] PrivKey(SnmpAuth auth, SnmpPriv priv, string password, byte[] engineId)
    {
        var key = Localize(auth, PasswordToKey(auth, password), engineId);
        var needed = priv switch { SnmpPriv.Aes192 => 24, SnmpPriv.Aes256 => 32, _ => 16 };
        while (key.Length < needed)
        {
            key = Ber.Concat(key, Hash(auth, key));
        }

        return key;
    }

    public static byte[] Encrypt(SnmpPriv priv, byte[] key, byte[] plain, uint boots, uint time, ulong salt, out byte[] privParams)
    {
        if (priv == SnmpPriv.Des)
        {
            // salt = engineBoots || local counter; IV = pre-IV (last 8 bytes of the key) XOR salt.
            privParams = Ber.Concat(BigEndian(boots), BigEndian((uint)salt));
            var iv = new byte[8];
            for (var i = 0; i < 8; i++)
            {
                iv[i] = (byte)(key[8 + i] ^ privParams[i]);
            }

            var padded = new byte[(plain.Length + 7) / 8 * 8];
            plain.CopyTo(padded, 0);
#pragma warning disable CA5351 // DES is what the device speaks (SNMPv3 usmDESPrivProtocol); AES is offered and preferred
            using var des = DES.Create();
#pragma warning restore CA5351
            des.Key = key[..8];
            return des.EncryptCbc(padded, iv, PaddingMode.None);
        }

        // AES-CFB128: IV = boots || time || 64-bit salt.
        privParams = BigEndian(salt);
        using var aes = Aes.Create();
        aes.Key = key[..AesKeyLength(priv)];
        var aesIv = Ber.Concat(BigEndian(boots), BigEndian(time), privParams);
        var block = new byte[(plain.Length + 15) / 16 * 16];
        plain.CopyTo(block, 0);
        return aes.EncryptCfb(block, aesIv, PaddingMode.None, 128)[..plain.Length];
    }

    public static byte[] Decrypt(SnmpPriv priv, byte[] key, byte[] cipher, uint boots, uint time, byte[] privParams)
    {
        if (privParams.Length != 8)
        {
            throw new SnmpException(SnmpError.DecryptionError, "Bad privacy parameters");
        }

        if (priv == SnmpPriv.Des)
        {
            if (cipher.Length % 8 != 0)
            {
                throw new SnmpException(SnmpError.DecryptionError, "Bad DES block");
            }

            var iv = new byte[8];
            for (var i = 0; i < 8; i++)
            {
                iv[i] = (byte)(key[8 + i] ^ privParams[i]);
            }

#pragma warning disable CA5351
            using var des = DES.Create();
#pragma warning restore CA5351
            des.Key = key[..8];
            return des.DecryptCbc(cipher, iv, PaddingMode.None);
        }

        using var aes = Aes.Create();
        aes.Key = key[..AesKeyLength(priv)];
        var aesIv = Ber.Concat(BigEndian(boots), BigEndian(time), privParams);
        var block = new byte[(cipher.Length + 15) / 16 * 16];
        cipher.CopyTo(block, 0);
        return aes.DecryptCfb(block, aesIv, PaddingMode.None, 128)[..cipher.Length];
    }

    private static int AesKeyLength(SnmpPriv priv) => priv switch { SnmpPriv.Aes192 => 24, SnmpPriv.Aes256 => 32, _ => 16 };

    public static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    public static byte[] BigEndian(ulong value) => [.. BigEndian((uint)(value >> 32)), .. BigEndian((uint)value)];

    // ---- SHA-224 (not in .NET): SHA-256 with other initial values, truncated to 28 bytes. --------------------------

    private static readonly uint[] Sha224Init = [0xc1059ed8, 0x367cd507, 0x3070dd17, 0xf70e5939, 0xffc00b31, 0x68581511, 0x64f98fa7, 0xbefa4fa4];

    private static readonly uint[] K256 =
    [
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01,
        0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
        0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
        0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116, 0x1e376c08,
        0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
        0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
    ];

    public static byte[] Sha224(ReadOnlySpan<byte> data)
    {
        var h = (uint[])Sha224Init.Clone();
        var bitLength = (ulong)data.Length * 8;
        var padded = new byte[((data.Length + 9 + 63) / 64) * 64];
        data.CopyTo(padded);
        padded[data.Length] = 0x80;
        BigEndian(bitLength).CopyTo(padded, padded.Length - 8);
        var w = new uint[64];
        for (var block = 0; block < padded.Length; block += 64)
        {
            for (var i = 0; i < 16; i++)
            {
                w[i] = (uint)((padded[block + (4 * i)] << 24) | (padded[block + (4 * i) + 1] << 16) | (padded[block + (4 * i) + 2] << 8) | padded[block + (4 * i) + 3]);
            }

            for (var i = 16; i < 64; i++)
            {
                var s0 = uint.RotateRight(w[i - 15], 7) ^ uint.RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
                var s1 = uint.RotateRight(w[i - 2], 17) ^ uint.RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
                w[i] = w[i - 16] + s0 + w[i - 7] + s1;
            }

            uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
            for (var i = 0; i < 64; i++)
            {
                var s1 = uint.RotateRight(e, 6) ^ uint.RotateRight(e, 11) ^ uint.RotateRight(e, 25);
                var ch = (e & f) ^ (~e & g);
                var t1 = hh + s1 + ch + K256[i] + w[i];
                var s0 = uint.RotateRight(a, 2) ^ uint.RotateRight(a, 13) ^ uint.RotateRight(a, 22);
                var maj = (a & b) ^ (a & c) ^ (b & c);
                var t2 = s0 + maj;
                hh = g;
                g = f;
                f = e;
                e = d + t1;
                d = c;
                c = b;
                b = a;
                a = t1 + t2;
            }

            h[0] += a;
            h[1] += b;
            h[2] += c;
            h[3] += d;
            h[4] += e;
            h[5] += f;
            h[6] += g;
            h[7] += hh;
        }

        return [.. h.Take(7).SelectMany(BigEndian)];
    }

    private static byte[] HmacSha224(byte[] key, ReadOnlySpan<byte> data)
    {
        const int blockSize = 64;
        var k = key.Length > blockSize ? Sha224(key) : key;
        var ipad = new byte[blockSize];
        var opad = new byte[blockSize];
        for (var i = 0; i < blockSize; i++)
        {
            var b = i < k.Length ? k[i] : (byte)0;
            ipad[i] = (byte)(b ^ 0x36);
            opad[i] = (byte)(b ^ 0x5c);
        }

        return Sha224(Ber.Concat(opad, Sha224(Ber.Concat(ipad, data.ToArray()))));
    }
}
#pragma warning restore CA5350, CA5351
