using System;

namespace FileSystemOS.Utils
{
    /// <summary>
    /// Decompresseur DEFLATE / zlib ecrit a la main (d'apres l'algorithme de reference "puff").
    /// Sert aux fichiers PDF (FlateDecode) et aux images PNG.
    /// </summary>
    public class Inflate
    {
        public const int MaxOutput = 48 * 1024 * 1024;

        private static readonly int[] LBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
        private static readonly int[] LExt = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
        private static readonly int[] DBase = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
        private static readonly int[] DExt = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
        private static readonly int[] Order = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        private class Huff
        {
            public readonly int[] Count = new int[16];
            public readonly int[] Symbol;
            public Huff(int n) { Symbol = new int[n]; }
        }

        private readonly byte[] src;
        private int pos;
        private readonly int end;
        private uint bitBuf;
        private int bitCnt;
        private byte[] outBuf;
        private int outLen;

        private Inflate(byte[] data, int offset, int length)
        {
            src = data; pos = offset; end = offset + length;
            outBuf = new byte[length * 4 + 1024];
        }

        /// <summary>Flux zlib (2 octets d'en-tete) ou deflate brut.</summary>
        public static byte[] Zlib(byte[] data, int offset, int length)
        {
            if (length >= 2 && (data[offset] & 0x0F) == 8 && ((data[offset] << 8) | data[offset + 1]) % 31 == 0)
            {
                offset += 2;
                length -= 2;
            }
            var inf = new Inflate(data, offset, length);
            inf.Run();
            byte[] r = new byte[inf.outLen];
            Array.Copy(inf.outBuf, r, inf.outLen);
            return r;
        }

        private int Bits(int need)
        {
            uint val = bitBuf;
            while (bitCnt < need)
            {
                if (pos >= end) throw new Exception("donnees compressees tronquees");
                val |= (uint)src[pos++] << bitCnt;
                bitCnt += 8;
            }
            bitBuf = val >> need;
            bitCnt -= need;
            return (int)(val & ((1u << need) - 1));
        }

        private void Put(byte b)
        {
            if (outLen >= outBuf.Length)
            {
                if (outBuf.Length >= MaxOutput) throw new Exception("donnees decompressees trop grandes");
                byte[] nb = new byte[outBuf.Length * 2];
                Array.Copy(outBuf, nb, outLen);
                outBuf = nb;
            }
            outBuf[outLen++] = b;
        }

        private void Run()
        {
            int last;
            do
            {
                last = Bits(1);
                int type = Bits(2);
                if (type == 0) Stored();
                else if (type == 1) Codes(FixedLen, FixedDist);
                else if (type == 2) Dynamic();
                else throw new Exception("bloc deflate invalide");
            } while (last == 0);
        }

        private void Stored()
        {
            bitBuf = 0; bitCnt = 0;
            if (pos + 4 > end) throw new Exception("bloc tronque");
            int len = src[pos] | (src[pos + 1] << 8);
            pos += 4;
            if (pos + len > end) throw new Exception("bloc tronque");
            for (int i = 0; i < len; i++) Put(src[pos++]);
        }

        private int Decode(Huff h)
        {
            int code = 0, first = 0, index = 0;
            for (int len = 1; len < 16; len++)
            {
                code |= Bits(1);
                int count = h.Count[len];
                if (code - count < first) return h.Symbol[index + (code - first)];
                index += count;
                first += count;
                first <<= 1;
                code <<= 1;
            }
            throw new Exception("code de Huffman invalide");
        }

        private static void Build(Huff h, int[] lengths, int offset, int n)
        {
            for (int i = 0; i < 16; i++) h.Count[i] = 0;
            for (int s = 0; s < n; s++) h.Count[lengths[offset + s]]++;
            int[] offs = new int[16];
            for (int len = 1; len < 15; len++) offs[len + 1] = offs[len] + h.Count[len];
            for (int s = 0; s < n; s++)
                if (lengths[offset + s] != 0) h.Symbol[offs[lengths[offset + s]]++] = s;
        }

        private void Codes(Huff lencode, Huff distcode)
        {
            while (true)
            {
                int sym = Decode(lencode);
                if (sym < 256) { Put((byte)sym); continue; }
                if (sym == 256) return;
                sym -= 257;
                if (sym >= 29) throw new Exception("longueur invalide");
                int len = LBase[sym] + Bits(LExt[sym]);
                int ds = Decode(distcode);
                if (ds >= 30) throw new Exception("distance invalide");
                int dist = DBase[ds] + Bits(DExt[ds]);
                if (dist > outLen) throw new Exception("distance trop grande");
                for (int i = 0; i < len; i++) Put(outBuf[outLen - dist]);
            }
        }

        private static Huff fixedLen, fixedDist;

        private static Huff FixedLen { get { MakeFixed(); return fixedLen; } }
        private static Huff FixedDist { get { MakeFixed(); return fixedDist; } }

        private static void MakeFixed()
        {
            if (fixedLen != null) return;
            int[] l = new int[288 + 30];
            int s = 0;
            for (; s < 144; s++) l[s] = 8;
            for (; s < 256; s++) l[s] = 9;
            for (; s < 280; s++) l[s] = 7;
            for (; s < 288; s++) l[s] = 8;
            for (int d = 0; d < 30; d++) l[288 + d] = 5;
            fixedLen = new Huff(288); Build(fixedLen, l, 0, 288);
            fixedDist = new Huff(30); Build(fixedDist, l, 288, 30);
        }

        private void Dynamic()
        {
            int nlen = Bits(5) + 257, ndist = Bits(5) + 1, ncode = Bits(4) + 4;
            if (nlen > 286 || ndist > 30) throw new Exception("en-tete dynamique invalide");
            int[] lengths = new int[320];
            for (int i = 0; i < ncode; i++) lengths[Order[i]] = Bits(3);
            var lencode = new Huff(288);
            Build(lencode, lengths, 0, 19);

            int idx = 0;
            while (idx < nlen + ndist)
            {
                int sym = Decode(lencode);
                if (sym < 16) { lengths[idx++] = sym; continue; }
                int len = 0, rep;
                if (sym == 16)
                {
                    if (idx == 0) throw new Exception("repetition invalide");
                    len = lengths[idx - 1];
                    rep = 3 + Bits(2);
                }
                else if (sym == 17) rep = 3 + Bits(3);
                else rep = 11 + Bits(7);
                if (idx + rep > nlen + ndist) throw new Exception("trop de longueurs");
                while (rep-- > 0) lengths[idx++] = len;
            }

            var lc = new Huff(288);
            Build(lc, lengths, 0, nlen);
            int[] dl = new int[30];
            for (int i = 0; i < ndist; i++) dl[i] = lengths[nlen + i];
            var dc = new Huff(30);
            Build(dc, dl, 0, ndist);
            Codes(lc, dc);
        }
    }
}
