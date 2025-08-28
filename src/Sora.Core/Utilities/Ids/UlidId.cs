using System;
using System.Security.Cryptography;

namespace Sora.Core.Utilities.Ids;

/// <summary>
/// ULID generator producing canonical 26-char Crockford Base32 strings.
/// Note: minimal, monotonic-in-ms implementation suitable for IDs; not a full library.
/// </summary>
public static class UlidId
{
    private static readonly object _lock = new();
    private static long _lastTime;
    private static ulong _lastRandHi;
    private static ulong _lastRandLo;

    /// <summary>Create a new ULID (26-char string).</summary>
    public static string New()
    {
        Span<byte> ulid = stackalloc byte[16];
        FillUlidBytes(ulid);
        return ToBase32(ulid);
    }

    /// <summary>Try to parse a ULID string into its 16-byte representation.</summary>
    public static bool TryParse(string? value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value) || value.Length != 26) return false;
        try
        {
            var b = FromBase32(value);
            if (b.Length != 16) return false;
            bytes = b;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Parse a ULID string; throws if invalid.</summary>
    public static byte[] Parse(string value)
    {
        if (!TryParse(value, out var b)) throw new FormatException("Invalid ULID.");
        return b;
    }

    /// <summary>Extract timestamp (ms since epoch) from ULID.</summary>
    public static long GetTimestampMillis(string ulid)
    {
        var b = Parse(ulid);
        return ((long)b[0] << 40) | ((long)b[1] << 32) | ((long)b[2] << 24) | ((long)b[3] << 16) | ((long)b[4] << 8) | b[5];
    }

    /// <summary>Get randomness (10 bytes) from ULID.</summary>
    public static byte[] GetEntropy(string ulid)
    {
        var b = Parse(ulid);
        var r = new byte[10];
        Array.Copy(b, 6, r, 0, 10);
        return r;
    }

    private static void FillUlidBytes(Span<byte> bytes)
    {
        // 48-bit time (ms since Unix epoch) + 80-bit randomness
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Span<byte> rand = stackalloc byte[10];

        lock (_lock)
        {
            if (nowMs == _lastTime)
            {
                // increment random part to keep monotonicity within the same ms
                IncrementLastRandom();
                WriteRandomFromLast(rand);
            }
            else
            {
                RandomNumberGenerator.Fill(rand);
                _lastTime = nowMs;
                // snapshot current random to last
                _lastRandHi = ((ulong)rand[0] << 40) | ((ulong)rand[1] << 32) | ((ulong)rand[2] << 24) | ((ulong)rand[3] << 16) | ((ulong)rand[4] << 8);
                _lastRandLo = ((ulong)rand[5] << 40) | ((ulong)rand[6] << 32) | ((ulong)rand[7] << 24) | ((ulong)rand[8] << 16) | ((ulong)rand[9] << 8);
            }
        }

        // time (6 bytes big-endian)
        bytes[0] = (byte)((nowMs >> 40) & 0xFF);
        bytes[1] = (byte)((nowMs >> 32) & 0xFF);
        bytes[2] = (byte)((nowMs >> 24) & 0xFF);
        bytes[3] = (byte)((nowMs >> 16) & 0xFF);
        bytes[4] = (byte)((nowMs >> 8) & 0xFF);
        bytes[5] = (byte)(nowMs & 0xFF);

        // randomness (10 bytes)
        if (rand[0] == 0 && _lastTime == nowMs)
        {
            WriteRandomFromLast(rand);
        }

        bytes[6] = rand[0]; bytes[7] = rand[1]; bytes[8] = rand[2]; bytes[9] = rand[3]; bytes[10] = rand[4];
        bytes[11] = rand[5]; bytes[12] = rand[6]; bytes[13] = rand[7]; bytes[14] = rand[8]; bytes[15] = rand[9];
    }

    private static void IncrementLastRandom()
    {
        // increment the 80-bit value stored as two 40-bit chunks
        if (_lastRandLo == 0xFFFFFFFFFF00UL)
        {
            _lastRandLo = 0;
            _lastRandHi = (_lastRandHi + 1) & 0xFFFFFFFFFF00UL;
        }
        else
        {
            _lastRandLo = (_lastRandLo + 1) & 0xFFFFFFFFFF00UL;
        }
    }

    private static void WriteRandomFromLast(Span<byte> rand)
    {
        rand[0] = (byte)((_lastRandHi >> 40) & 0xFF);
        rand[1] = (byte)((_lastRandHi >> 32) & 0xFF);
        rand[2] = (byte)((_lastRandHi >> 24) & 0xFF);
        rand[3] = (byte)((_lastRandHi >> 16) & 0xFF);
        rand[4] = (byte)((_lastRandHi >> 8) & 0xFF);

        rand[5] = (byte)((_lastRandLo >> 40) & 0xFF);
        rand[6] = (byte)((_lastRandLo >> 32) & 0xFF);
        rand[7] = (byte)((_lastRandLo >> 24) & 0xFF);
        rand[8] = (byte)((_lastRandLo >> 16) & 0xFF);
        rand[9] = (byte)((_lastRandLo >> 8) & 0xFF);
    }

    private const string _crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // no I,L,O,U

    private static string ToBase32(ReadOnlySpan<byte> ulid)
    {
        // ULID: 128 bits -> 26 chars base32 (Crockford). Implement per spec bit packing.
        Span<char> chars = stackalloc char[26];

        ulong time = ((ulong)ulid[0] << 40) | ((ulong)ulid[1] << 32) | ((ulong)ulid[2] << 24) | ((ulong)ulid[3] << 16) | ((ulong)ulid[4] << 8) | ulid[5];
        ulong r0 = ((ulong)ulid[6] << 32) | ((ulong)ulid[7] << 24) | ((ulong)ulid[8] << 16) | ((ulong)ulid[9] << 8) | ulid[10];
        ulong r1 = ((ulong)ulid[11] << 32) | ((ulong)ulid[12] << 24) | ((ulong)ulid[13] << 16) | ((ulong)ulid[14] << 8) | ulid[15];

        // 26 chars = 130 bits; ULID uses 128 bits, leading bits in top char may be smaller. We'll follow standard mapping.
        // Break into 5-bit chunks
        ulong v0 = (time >> 35) & 0x1F;
        ulong v1 = (time >> 30) & 0x1F;
        ulong v2 = (time >> 25) & 0x1F;
        ulong v3 = (time >> 20) & 0x1F;
        ulong v4 = (time >> 15) & 0x1F;
        ulong v5 = (time >> 10) & 0x1F;
        ulong v6 = (time >> 5) & 0x1F;
        ulong v7 = time & 0x1F;

        ulong v8  = (r0 >> 35) & 0x1F;
        ulong v9  = (r0 >> 30) & 0x1F;
        ulong v10 = (r0 >> 25) & 0x1F;
        ulong v11 = (r0 >> 20) & 0x1F;
        ulong v12 = (r0 >> 15) & 0x1F;
        ulong v13 = (r0 >> 10) & 0x1F;
        ulong v14 = (r0 >> 5)  & 0x1F;
        ulong v15 = r0 & 0x1F;

        ulong v16 = (r1 >> 35) & 0x1F;
        ulong v17 = (r1 >> 30) & 0x1F;
        ulong v18 = (r1 >> 25) & 0x1F;
        ulong v19 = (r1 >> 20) & 0x1F;
        ulong v20 = (r1 >> 15) & 0x1F;
        ulong v21 = (r1 >> 10) & 0x1F;
        ulong v22 = (r1 >> 5)  & 0x1F;
        ulong v23 = r1 & 0x1F;

    // the last two characters encode the final 10 bits of randomness
    ulong v24 = (r1 >> 0) & 0x1F;
    ulong v25 = 0; // ULID uses only 128 bits; top 2 bits of last char are zero

        chars[0]  = _crockford[(int)v0];
        chars[1]  = _crockford[(int)v1];
        chars[2]  = _crockford[(int)v2];
        chars[3]  = _crockford[(int)v3];
        chars[4]  = _crockford[(int)v4];
        chars[5]  = _crockford[(int)v5];
        chars[6]  = _crockford[(int)v6];
        chars[7]  = _crockford[(int)v7];
        chars[8]  = _crockford[(int)v8];
        chars[9]  = _crockford[(int)v9];
        chars[10] = _crockford[(int)v10];
        chars[11] = _crockford[(int)v11];
        chars[12] = _crockford[(int)v12];
        chars[13] = _crockford[(int)v13];
        chars[14] = _crockford[(int)v14];
        chars[15] = _crockford[(int)v15];
        chars[16] = _crockford[(int)v16];
        chars[17] = _crockford[(int)v17];
        chars[18] = _crockford[(int)v18];
        chars[19] = _crockford[(int)v19];
        chars[20] = _crockford[(int)v20];
        chars[21] = _crockford[(int)v21];
        chars[22] = _crockford[(int)v22];
        chars[23] = _crockford[(int)v23];
        chars[24] = _crockford[(int)v24];
        chars[25] = _crockford[(int)v25];

        return new string(chars);
    }

    private static byte[] FromBase32(string input)
    {
        Span<int> map = stackalloc int[128];
        for (int i = 0; i < map.Length; i++) map[i] = -1;
        for (int i = 0; i < _crockford.Length; i++) map[_crockford[i]] = i;
        // allow lowercase
        for (int i = 0; i < _crockford.Length; i++) map[char.ToLowerInvariant(_crockford[i])] = i;

        Span<int> vals = stackalloc int[26];
        for (int i = 0; i < 26; i++)
        {
            int ch = input[i] < 128 ? map[input[i]] : -1;
            if (ch < 0) throw new FormatException("Invalid ULID base32 character.");
            vals[i] = ch;
        }

        // Reassemble 48-bit time and 80-bit randomness
        ulong timeU =
            ((ulong)(uint)vals[0] << 35) | ((ulong)(uint)vals[1] << 30) | ((ulong)(uint)vals[2] << 25) | ((ulong)(uint)vals[3] << 20) |
            ((ulong)(uint)vals[4] << 15) | ((ulong)(uint)vals[5] << 10) | ((ulong)(uint)vals[6] << 5) | (ulong)(uint)vals[7];
        long time = (long)timeU;

        ulong r0 =
            ((ulong)(uint)vals[8] << 35) | ((ulong)(uint)vals[9] << 30) | ((ulong)(uint)vals[10] << 25) | ((ulong)(uint)vals[11] << 20) |
            ((ulong)(uint)vals[12] << 15) | ((ulong)(uint)vals[13] << 10) | ((ulong)(uint)vals[14] << 5) | (ulong)(uint)vals[15];

        ulong r1 =
            ((ulong)(uint)vals[16] << 35) | ((ulong)(uint)vals[17] << 30) | ((ulong)(uint)vals[18] << 25) | ((ulong)(uint)vals[19] << 20) |
            ((ulong)(uint)vals[20] << 15) | ((ulong)(uint)vals[21] << 10) | ((ulong)(uint)vals[22] << 5) | (ulong)(uint)vals[23];

        // vals[24] carries the low 5 bits; vals[25] carries top 2 bits (unused)
    r1 = (r1 << 5) | (ulong)(uint)vals[24];

        byte[] bytes = new byte[16];
        bytes[0] = (byte)((time >> 40) & 0xFF);
        bytes[1] = (byte)((time >> 32) & 0xFF);
        bytes[2] = (byte)((time >> 24) & 0xFF);
        bytes[3] = (byte)((time >> 16) & 0xFF);
        bytes[4] = (byte)((time >> 8) & 0xFF);
        bytes[5] = (byte)(time & 0xFF);

        bytes[6] = (byte)((r0 >> 32) & 0xFF);
        bytes[7] = (byte)((r0 >> 24) & 0xFF);
        bytes[8] = (byte)((r0 >> 16) & 0xFF);
        bytes[9] = (byte)((r0 >> 8) & 0xFF);
        bytes[10] = (byte)(r0 & 0xFF);

        bytes[11] = (byte)((r1 >> 32) & 0xFF);
        bytes[12] = (byte)((r1 >> 24) & 0xFF);
        bytes[13] = (byte)((r1 >> 16) & 0xFF);
        bytes[14] = (byte)((r1 >> 8) & 0xFF);
        bytes[15] = (byte)(r1 & 0xFF);
        return bytes;
    }
}
