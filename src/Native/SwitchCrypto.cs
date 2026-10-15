#if KG_SWITCH
global using SHA256 = KuroakiGimmick.Native.SwitchSha256;
global using IncrementalHash = KuroakiGimmick.Native.SwitchIncrementalHash;

namespace KuroakiGimmick.Native;

// mono-nx has no OpenSSL. Asset identity still needs SHA-256 compatible with desktop exports.
internal static class SwitchSha256
{
    public static byte[] HashData(byte[] data)
    {
        using var hash = SwitchIncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(data, 0, data.Length);
        return hash.GetHashAndReset();
    }
    public static byte[] HashData(Stream stream)
    {
        using var hash = SwitchIncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        byte[] buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer)) > 0) hash.AppendData(buffer, 0, count);
        return hash.GetHashAndReset();
    }
}

internal sealed class SwitchIncrementalHash : IDisposable
{
    static readonly uint[] K =
    [
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2
    ];
    readonly uint[] state = new uint[8];
    readonly byte[] block = new byte[64];
    ulong length;
    int used;
    SwitchIncrementalHash() => Reset();
    public static SwitchIncrementalHash CreateHash(System.Security.Cryptography.HashAlgorithmName name) =>
        name == System.Security.Cryptography.HashAlgorithmName.SHA256 ? new() : throw new NotSupportedException(name.Name);
    void Reset()
    {
        uint[] initial = [0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19];
        initial.CopyTo(state, 0); length = 0; used = 0;
    }
    public void AppendData(byte[] data, int offset, int count)
    {
        var input = data.AsSpan(offset, count);
        length += (ulong)count;
        while (!input.IsEmpty)
        {
            int take = Math.Min(64 - used, input.Length);
            input[..take].CopyTo(block.AsSpan(used)); used += take; input = input[take..];
            if (used == 64) { Compress(); used = 0; }
        }
    }
    static uint Rotate(uint v, int n) => (v >> n) | (v << (32 - n));
    void Compress()
    {
        unchecked
        {
            Span<uint> w = stackalloc uint[64];
            for (int i = 0; i < 16; i++) w[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(i * 4, 4));
            for (int i = 16; i < 64; i++)
            {
                uint s0 = Rotate(w[i-15],7) ^ Rotate(w[i-15],18) ^ (w[i-15] >> 3);
                uint s1 = Rotate(w[i-2],17) ^ Rotate(w[i-2],19) ^ (w[i-2] >> 10);
                w[i] = w[i-16] + s0 + w[i-7] + s1;
            }
            uint a=state[0], b=state[1], c=state[2], d=state[3], e=state[4], f=state[5], g=state[6], h=state[7];
            for (int i = 0; i < 64; i++)
            {
                uint t1=h+(Rotate(e,6)^Rotate(e,11)^Rotate(e,25))+((e&f)^(~e&g))+K[i]+w[i];
                uint t2=(Rotate(a,2)^Rotate(a,13)^Rotate(a,22))+((a&b)^(a&c)^(b&c));
                h=g; g=f; f=e; e=d+t1; d=c; c=b; b=a; a=t1+t2;
            }
            state[0]+=a; state[1]+=b; state[2]+=c; state[3]+=d; state[4]+=e; state[5]+=f; state[6]+=g; state[7]+=h;
        }
    }
    public byte[] GetHashAndReset()
    {
        ulong bits = length * 8;
        block[used++] = 0x80;
        if (used > 56) { Array.Clear(block, used, 64 - used); Compress(); used = 0; }
        Array.Clear(block, used, 56 - used);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(block.AsSpan(56), bits);
        Compress();
        byte[] result = new byte[32];
        for (int i = 0; i < 8; i++) System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(i*4), state[i]);
        Reset(); return result;
    }
    public void Dispose() { Array.Clear(state); Array.Clear(block); }
}
#endif
