using System.Collections.Generic;
using Cosmos.Core.Memory;
using FileSystemOS.Utils;

namespace FileSystemOS.Memoire
{
    /// <summary>
    /// Zone memoire de 4 Ko ou l'on ecrit des textes.
    /// Format de chaque entree : [longueur : 1 octet][caracteres ASCII]
    /// </summary>
    public unsafe class MemoryManager
    {
        public const uint ZoneSize = 4096;

        private readonly byte* zone;

        private readonly byte[] zoneData;   // garde la zone vivante (ramasse-miettes)
        private readonly MemoryAccelerator acc;
        private uint used;

        public uint BaseAddress => (uint)zone;
        public uint Used => used;

        public MemoryManager(MemoryAccelerator accelerator)
        {
            acc = accelerator;
            zoneData = new byte[ZoneSize];
            fixed (byte* p = zoneData) { zone = p; }
            MemoryAccelerator.Fill(zone, 0, ZoneSize);
        }

        /// <summary>Ecrit un texte et renvoie son adresse physique (0 si plein).</summary>
        public uint Write(string text)
        {
            if (text.Length > 255) text = text.Substring(0, 255);
            uint len = (uint)text.Length;
            if (used + 1 + len > ZoneSize) return 0;

            // On prepare l'entree dans un bloc du pool (accelerateur) ...
            byte* tmp = acc.Alloc(len + 1);
            tmp[0] = (byte)len;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                tmp[i + 1] = c < 128 ? (byte)c : (byte)'?';
            }

            // ... puis copie rapide dans la zone
            byte* dest = zone + used;
            MemoryAccelerator.Copy(dest, tmp, len + 1);
            acc.Free(tmp);

            used += len + 1;
            return (uint)dest;
        }

        public List<string> ReadAll()
        {
            var res = new List<string>();
            uint off = 0;
            while (off < used)
            {
                byte len = zone[off];
                char[] ch = new char[len];
                for (int i = 0; i < len; i++) ch[i] = (char)zone[off + 1 + i];
                res.Add("0x" + Fmt.Hex((uint)(zone + off)) + " : \"" + new string(ch) + "\"");
                off += 1u + len;
            }
            return res;
        }

        /// <summary>Vidage hexadecimal : 8 octets par ligne + ASCII.</summary>
        public List<string> DumpAt(uint address, uint count)
        {
            var res = new List<string>();
            byte* p = (byte*)address;
            for (uint off = 0; off < count; off += 8)
            {
                uint n = count - off < 8 ? count - off : 8;
                string hex = "";
                string asc = "";
                for (uint i = 0; i < 8; i++)
                {
                    if (i < n)
                    {
                        byte b = p[off + i];
                        hex += Fmt.Hex(b, 2) + " ";
                        asc += (b >= 32 && b < 127) ? ((char)b).ToString() : ".";
                    }
                    else hex += "   ";
                }
                res.Add("0x" + Fmt.Hex(address + off) + "  " + hex + " " + asc);
            }
            return res;
        }

        public void Clear()
        {
            MemoryAccelerator.Fill(zone, 0, ZoneSize);
            used = 0;
        }
    }
}
