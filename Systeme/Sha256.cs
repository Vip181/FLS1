namespace FileSystemOS.Systeme
{
    /// <summary>SHA-256 ecrit a la main (Cosmos n'a pas System.Security.Cryptography).</summary>
    public static class Sha256
    {
        private static readonly uint[] K =
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        };

        private static uint R(uint x, int n) => (x >> n) | (x << (32 - n));

        public static string HashHex(string text)
        {
            int len = text.Length;
            int total = ((len + 9 + 63) / 64) * 64;
            byte[] m = new byte[total];
            for (int i = 0; i < len; i++) m[i] = (byte)text[i];
            m[len] = 0x80;
            ulong bits = (ulong)len * 8;
            for (int i = 0; i < 8; i++) m[total - 1 - i] = (byte)(bits >> (8 * i));

            uint h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a;
            uint h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
            uint[] w = new uint[64];

            for (int off = 0; off < total; off += 64)
            {
                for (int i = 0; i < 16; i++)
                    w[i] = ((uint)m[off + i * 4] << 24) | ((uint)m[off + i * 4 + 1] << 16) |
                           ((uint)m[off + i * 4 + 2] << 8) | m[off + i * 4 + 3];
                for (int i = 16; i < 64; i++)
                {
                    uint s0 = R(w[i - 15], 7) ^ R(w[i - 15], 18) ^ (w[i - 15] >> 3);
                    uint s1 = R(w[i - 2], 17) ^ R(w[i - 2], 19) ^ (w[i - 2] >> 10);
                    w[i] = w[i - 16] + s0 + w[i - 7] + s1;
                }

                uint a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
                for (int i = 0; i < 64; i++)
                {
                    uint S1 = R(e, 6) ^ R(e, 11) ^ R(e, 25);
                    uint ch = (e & f) ^ (~e & g);
                    uint t1 = h + S1 + ch + K[i] + w[i];
                    uint S0 = R(a, 2) ^ R(a, 13) ^ R(a, 22);
                    uint maj = (a & b) ^ (a & c) ^ (b & c);
                    uint t2 = S0 + maj;
                    h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
                }
                h0 += a; h1 += b; h2 += c; h3 += d; h4 += e; h5 += f; h6 += g; h7 += h;
            }

            return Utils.Fmt.Hex(h0) + Utils.Fmt.Hex(h1) + Utils.Fmt.Hex(h2) + Utils.Fmt.Hex(h3) +
                   Utils.Fmt.Hex(h4) + Utils.Fmt.Hex(h5) + Utils.Fmt.Hex(h6) + Utils.Fmt.Hex(h7);
        }
    }
}
