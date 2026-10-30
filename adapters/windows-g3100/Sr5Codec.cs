// ALTERED VERSION: cipher extracted for an experimental G3100 adapter. These tables
// were originally validated on G6000. This adapter subsequently read and cleared
// D on one real G3100 V3.010, including a post-power-cycle D=0 reread.
// Derived from Frelsi778877/canon-g6000-win Program.cs, blob 9a39b98e111abca822409277c20feb7134d32bca.
// Original authors and zlib terms retained in LICENSE. No claim of original authorship.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CanonG3100
{
    readonly struct ShiftStep
    {
        public readonly char Sign; public readonly uint Data;
        public ShiftStep(char sign, long data){ Sign = sign; Data = unchecked((uint)data); }
    }
    readonly struct FunctionBlock
    {
        public readonly byte[] Special; public readonly int[] Indexes;
        public FunctionBlock(byte[] special, int[] indexes){ Special = special; Indexes = indexes; }
    }

    static class Sr5Codec
    {
        // keyword tables
        static readonly byte[] KeywordCodes = new byte[] { 0x4D, 0x49, 0x53, 0x00 };
        static readonly byte[] KeywordIndex = new byte[] { 0x03, 0x01, 0x00, 0x02 };
        static readonly byte[] EnvFixed16  = new byte[] { 0xE9, 0x3F, 0x0D, 0xA1, 0x96, 0x95, 0x31, 0x04, 0x49, 0x2D, 0x9E, 0x61, 0x83, 0xCF, 0x09, 0x6F };
        static readonly byte[][] CommandIndex = new byte[][] {
            new byte[] { 0x0F, 0x03, 0x13, 0x0C, 0x01, 0x08, 0x00, 0x12, 0x07, 0x05, 0x10, 0x04, 0x0E, 0x06, 0x02, 0x11, 0x0B, 0x0D, 0x09, 0x0A },
            new byte[] { 0x08, 0x12, 0x00, 0x06, 0x0F, 0x04, 0x0B, 0x0E, 0x01, 0x0A, 0x02, 0x13, 0x03, 0x05, 0x11, 0x07, 0x0D, 0x09, 0x10, 0x0C },
            new byte[] { 0x04, 0x0B, 0x13, 0x09, 0x07, 0x11, 0x0F, 0x02, 0x0D, 0x00, 0x01, 0x0E, 0x05, 0x03, 0x06, 0x10, 0x08, 0x0A, 0x0C, 0x12 },
            new byte[] { 0x12, 0x0E, 0x00, 0x0F, 0x07, 0x08, 0x0B, 0x01, 0x13, 0x03, 0x11, 0x02, 0x10, 0x04, 0x0D, 0x09, 0x0C, 0x06, 0x0A, 0x05 },
            new byte[] { 0x07, 0x05, 0x08, 0x0A, 0x11, 0x03, 0x00, 0x0E, 0x13, 0x12, 0x04, 0x01, 0x09, 0x06, 0x02, 0x0D, 0x0F, 0x0B, 0x0C, 0x10 }
        };
        static readonly byte[][] CommandCodes = new byte[][] {
            new byte[] { 0x09, 0x12, 0xDD, 0x1D, 0x41, 0x13, 0x63, 0x6B, 0x44, 0x2A, 0x17, 0xBD, 0xAF, 0xD2, 0x88, 0x31, 0x3B, 0x71, 0xBB, 0xE5 },
            new byte[] { 0xBB, 0x41, 0x2C, 0xD2, 0x18, 0x89, 0x77, 0x45, 0x4F, 0x11, 0x05, 0x67, 0x25, 0xD3, 0x7C, 0x4E, 0xD4, 0x4A, 0x88, 0x91 },
            new byte[] { 0x3F, 0x03, 0x26, 0x3D, 0xCA, 0x72, 0x89, 0x44, 0x09, 0xE2, 0xB8, 0x20, 0xA9, 0x4E, 0x2B, 0x04, 0x0C, 0x69, 0x6B, 0x25 },
            new byte[] { 0x68, 0x12, 0x63, 0x7F, 0x8F, 0xDE, 0x70, 0x40, 0x38, 0x2F, 0x09, 0x64, 0x81, 0xBB, 0x31, 0xC5, 0x1A, 0x72, 0x6D, 0x3A },
            new byte[] { 0xCA, 0xCC, 0x1D, 0xE4, 0x78, 0x1C, 0x1B, 0x7E, 0x7F, 0x14, 0xD5, 0x18, 0xD1, 0xCF, 0x80, 0x7B, 0x02, 0xD3, 0x19, 0xDE },
            new byte[] { 0x14, 0x27, 0x22, 0x08, 0x0A, 0x9D, 0xA7, 0xCA, 0xE8, 0x3A, 0x4B, 0x47, 0x6B, 0x89, 0x95, 0x90, 0x23, 0x6F, 0xB2, 0xC7 },
            new byte[] { 0x38, 0x42, 0x2F, 0x4F, 0x6D, 0x70, 0x9B, 0xDD, 0xE6, 0x09, 0x05, 0x07, 0xF0, 0x12, 0xA8, 0x17, 0x62, 0x4C, 0x31, 0x95 }
        };
        static readonly ShiftStep[][][] CommandShift = new ShiftStep[][][] {
            new ShiftStep[][] { new ShiftStep[] { new ShiftStep('=', 0) }, new ShiftStep[] { new ShiftStep('&', 1) }, new ShiftStep[] { new ShiftStep('=', 0) }, new ShiftStep[] { new ShiftStep('=', 0) } },
            new ShiftStep[][] { new ShiftStep[] { new ShiftStep('&', 1) }, new ShiftStep[] { new ShiftStep('=', 1) }, new ShiftStep[] { new ShiftStep('%', 5) }, new ShiftStep[] { new ShiftStep('=', 0) } },
            new ShiftStep[][] { new ShiftStep[] { new ShiftStep('&', 1) }, new ShiftStep[] { new ShiftStep('=', 0) }, new ShiftStep[] { new ShiftStep('=', 0) }, new ShiftStep[] { new ShiftStep('=', 2) } }
        };
        static readonly Dictionary<int,FunctionBlock> Functions = new() {
            [0x00] = new FunctionBlock(new byte[] { 0x04, 0x66 }, System.Array.Empty<int>()),
            [0x01] = new FunctionBlock(new byte[] { 0x05, 0x22 }, new int[] { 0x07 }),
            [0x02] = new FunctionBlock(new byte[] { 0x00, 0x87 }, System.Array.Empty<int>()),
            [0x03] = new FunctionBlock(new byte[] { 0x0F, 0x01 }, new int[] { 0x03 }),
            [0x04] = new FunctionBlock(new byte[] { 0x09, 0xA3 }, new int[] { 0x08 }),
            [0x05] = new FunctionBlock(new byte[] { 0x07, 0x66 }, System.Array.Empty<int>()),
            [0x06] = new FunctionBlock(new byte[] { 0x01, 0x81 }, System.Array.Empty<int>()),
            [0x07] = new FunctionBlock(new byte[] { 0x04, 0x0F }, new int[] { 0x01, 0x03 }),
            [0x08] = new FunctionBlock(new byte[] { 0x0B, 0x91 }, new int[] { 0x06, 0x01 }),
            [0x09] = new FunctionBlock(new byte[] { 0x02, 0x22 }, new int[] { 0x07, 0x0A }),
            [0x0A] = new FunctionBlock(new byte[] { 0x06, 0x02 }, System.Array.Empty<int>()),
            [0x0B] = new FunctionBlock(new byte[] { 0x06, 0xCC }, new int[] { 0x02, 0x0D }),
            [0x0C] = new FunctionBlock(new byte[] { 0x02, 0xA8 }, System.Array.Empty<int>()),
            [0x0D] = new FunctionBlock(new byte[] { 0x05, 0x01 }, new int[] { 0x03, 0x04 }),
            [0x0E] = new FunctionBlock(new byte[] { 0x0A, 0x10 }, new int[] { 0x0B }),
            [0x0F] = new FunctionBlock(new byte[] { 0x02, 0x02 }, new int[] { 0x01 }),
            [0x10] = new FunctionBlock(new byte[] { 0x01, 0xDD }, new int[] { 0x09, 0x05 }),
            [0x11] = new FunctionBlock(new byte[] { 0x00, 0xB4 }, new int[] { 0x0C }),
            [0x12] = new FunctionBlock(new byte[] { 0x03, 0x3A }, new int[] { 0x02, 0x0A }),
            [0x13] = new FunctionBlock(new byte[] { 0x01, 0xAA }, new int[] { 0x05 }),
            [0x14] = new FunctionBlock(new byte[] { 0x07, 0x03 }, new int[] { 0x06 }),
            [0x15] = new FunctionBlock(new byte[] { 0x03, 0x6D }, System.Array.Empty<int>()),
            [0x16] = new FunctionBlock(new byte[] { 0x08, 0x91 }, System.Array.Empty<int>()),
            [0x17] = new FunctionBlock(new byte[] { 0x04, 0x49 }, System.Array.Empty<int>()),
            [0x18] = new FunctionBlock(new byte[] { 0x0C, 0x12 }, new int[] { 0x0D }),
            [0x19] = new FunctionBlock(new byte[] { 0x0C, 0x61 }, System.Array.Empty<int>()),
            [0x1A] = new FunctionBlock(new byte[] { 0x02, 0x09 }, new int[] { 0x01 }),
            [0x1B] = new FunctionBlock(new byte[] { 0x01, 0xDD }, System.Array.Empty<int>()),
            [0x1C] = new FunctionBlock(new byte[] { 0x03, 0x93 }, System.Array.Empty<int>()),
            [0x1D] = new FunctionBlock(new byte[] { 0x0A, 0x04 }, System.Array.Empty<int>()),
            [0x1E] = new FunctionBlock(new byte[] { 0x07, 0x71 }, System.Array.Empty<int>()),
            [0x20] = new FunctionBlock(new byte[] { 0x06, 0xC2 }, System.Array.Empty<int>()),
            [0x21] = new FunctionBlock(new byte[] { 0x02, 0x12 }, new int[] { 0x08 }),
            [0x22] = new FunctionBlock(new byte[] { 0x08, 0x88 }, System.Array.Empty<int>()),
            [0x23] = new FunctionBlock(new byte[] { 0x0E, 0xEE }, System.Array.Empty<int>()),
            [0x24] = new FunctionBlock(new byte[] { 0x09, 0x99 }, System.Array.Empty<int>()),
            [0x25] = new FunctionBlock(new byte[] { 0x0D, 0x02 }, System.Array.Empty<int>()),
            [0x26] = new FunctionBlock(new byte[] { 0x01, 0x19 }, System.Array.Empty<int>()),
            [0x27] = new FunctionBlock(new byte[] { 0x05, 0x59 }, new int[] { 0x08 }),
            [0x28] = new FunctionBlock(new byte[] { 0x0B, 0x22 }, new int[] { 0x01 }),
            [0x29] = new FunctionBlock(new byte[] { 0x02, 0x89 }, System.Array.Empty<int>()),
            [0x2A] = new FunctionBlock(new byte[] { 0x0A, 0xCA }, System.Array.Empty<int>()),
            [0x2B] = new FunctionBlock(new byte[] { 0x07, 0x35 }, System.Array.Empty<int>()),
            [0x2C] = new FunctionBlock(new byte[] { 0x08, 0x40 }, System.Array.Empty<int>()),
            [0x2D] = new FunctionBlock(new byte[] { 0x06, 0x34 }, System.Array.Empty<int>()),
            [0x2E] = new FunctionBlock(new byte[] { 0x07, 0x82 }, new int[] { 0x08, 0x09 }),
            [0x2F] = new FunctionBlock(new byte[] { 0x05, 0x17 }, new int[] { 0x03, 0x0A }),
            [0x30] = new FunctionBlock(new byte[] { 0x03, 0x56 }, new int[] { 0x01 }),
            [0x31] = new FunctionBlock(new byte[] { 0x04, 0x62 }, System.Array.Empty<int>()),
            [0x32] = new FunctionBlock(new byte[] { 0x02, 0x38 }, System.Array.Empty<int>()),
            [0x33] = new FunctionBlock(new byte[] { 0x04, 0x57 }, new int[] { 0x08 }),
            [0x34] = new FunctionBlock(new byte[] { 0x0E, 0x13 }, System.Array.Empty<int>()),
            [0x35] = new FunctionBlock(new byte[] { 0x0D, 0x78 }, new int[] { 0x0A }),
            [0x36] = new FunctionBlock(new byte[] { 0x04, 0x24 }, new int[] { 0x03 }),
            [0x37] = new FunctionBlock(new byte[] { 0x0B, 0x60 }, System.Array.Empty<int>()),
            [0x38] = new FunctionBlock(new byte[] { 0x09, 0x26 }, System.Array.Empty<int>()),
            [0x39] = new FunctionBlock(new byte[] { 0x0A, 0xB5 }, System.Array.Empty<int>()),
            [0x3A] = new FunctionBlock(new byte[] { 0x08, 0x05 }, System.Array.Empty<int>()),
            [0x3B] = new FunctionBlock(new byte[] { 0x0C, 0x50 }, System.Array.Empty<int>()),
            [0x3C] = new FunctionBlock(new byte[] { 0x07, 0x77 }, System.Array.Empty<int>()),
            [0x3D] = new FunctionBlock(new byte[] { 0x0F, 0x29 }, System.Array.Empty<int>()),
            [0x3E] = new FunctionBlock(new byte[] { 0x02, 0xE7 }, System.Array.Empty<int>()),
            [0x3F] = new FunctionBlock(new byte[] { 0x05, 0x9D }, System.Array.Empty<int>()),
            [0x40] = new FunctionBlock(new byte[] { 0x0B, 0x83 }, System.Array.Empty<int>()),
            [0x41] = new FunctionBlock(new byte[] { 0x06, 0xEB }, System.Array.Empty<int>()),
            [0x42] = new FunctionBlock(new byte[] { 0x04, 0x70 }, new int[] { 0x08 }),
            [0x43] = new FunctionBlock(new byte[] { 0x0D, 0x2C }, new int[] { 0x0A }),
        };

        // ───────────────────────── cipher (validated) ─────────────────────────
        public static byte[] BindKeyword(byte[] dev)
        {
            var o = new byte[4];
            for (int i = 0; i < 4; i++) { int j = KeywordIndex[i]; o[i] = (byte)(KeywordCodes[j] ^ dev[j]); }
            return o;
        }
        static uint SeedFold(byte[] buf)
        { uint s = 0; foreach (var b in buf) s = unchecked(s * 0x100u + b); return s; }

        static uint ApplyShift(uint seed, ShiftStep[] steps)
        {
            uint acc = seed;
            foreach (var st in steps)
            {
                uint d = st.Data;
                switch (st.Sign)
                {
                    case '=': acc = d; break;
                    case '+': acc = unchecked(acc + d); break;
                    case '-': acc = unchecked(acc - d); break;
                    case '*': acc = unchecked(acc * d); break;
                    case '/': acc = d != 0 ? acc / d : 0; break;
                    case '%': acc = d != 0 ? acc % d : 0; break;
                    case '&': acc &= d; break;
                    case '|': acc |= d; break;
                    case '^': acc ^= d; break;
                    default: throw new Exception($"unknown operator {st.Sign}");
                }
            }
            return acc;
        }
        static uint[] BuildShiftTable(uint seed, ShiftStep[][] arr)
        { var t = new uint[arr.Length]; for (int i = 0; i < arr.Length; i++) t[i] = ApplyShift(seed, arr[i]); return t; }

        // functor_implementation (FUN_004e76c0): transform SUBJECT, seeded by seedSource.
        static byte[] Functor2(byte[] subject, byte[] seedSource, bool send)
        {
            uint seed = SeedFold(seedSource);
            var idx   = CommandIndex[seed % (uint)CommandIndex.Length];
            var codes = CommandCodes[seed % (uint)CommandCodes.Length];
            var sTab  = BuildShiftTable(seed, CommandShift[seed % (uint)CommandShift.Length]);
            int n = subject.Length; var o = new byte[n];
            for (int i = 0; i < n; i++)
            {
                int j = idx[i % idx.Length] % n;
                byte code = codes[j % codes.Length];
                uint shift = sTab[j % sTab.Length];
                byte ks = (byte)(((seed >> (int)(shift & 0x1f)) ^ code) & 0xFF);
                if (send) o[i] = (byte)(subject[j] ^ ks);
                else      o[j] = (byte)(subject[i] ^ ks);
            }
            return o;
        }

        // functor_encryption_003 (FUN_004e8410): the 20-byte envelope.
        static byte[] Envelope3(byte[] app)
        {
            byte cmdId = app[3];
            var env = new byte[20];
            env[0] = 0x00; env[1] = 0x12; env[2] = 0x01; env[3] = cmdId;
            Array.Copy(EnvFixed16, 0, env, 4, 16);
            if (Functions.TryGetValue(cmdId, out var blk))
            {
                var sp = blk.Special;
                for (int k = 0; k + 1 < sp.Length; k += 2)
                { int off = sp[k]; if (4 + off < env.Length) env[4 + off] = sp[k + 1]; }
                for (int i = 0; i < blk.Indexes.Length; i++)
                { int off = blk.Indexes[i]; int pi = 4 + i; if (pi < app.Length && 4 + off < env.Length) env[4 + off] = app[pi]; }
            }
            return env;
        }

        // Encipher a set_command (0x85) app frame -> 20-byte on-wire payload.
        public static byte[] Encipher85(byte[] app, byte[] bound)
            => Functor2(Envelope3(app), bound, send: true);

        // Decrypt one 20-byte response block -> plaintext bytes.
        public static byte[] DecryptBlock(byte[] block, byte[] bound)
            => Functor2(block, bound, send: false);

        // ───────────────────────── self-test (no hardware) ─────────────────────
        static (byte[] kw, byte[] app, byte[] want)[] Vectors = new[]
        {
            (Hex("DB3BD200"), Hex("85000010077C"), Hex("3A8A9570EAFFC2752999E0A22162CE06A32BFB00")),
            (Hex("DB3BD200"), Hex("85000015"),     Hex("3A8A957026FFC275299CE0A22162E4EFA32BFBE2")),
            (Hex("E49BF700"), Hex("85000010077C"), Hex("16B2ADF46E761DA42EA91A781C0C71F242CA826A")),
            (Hex("E49BF700"), Hex("8500000D0000"), Hex("16B2ADF4CFE01DA42EB41A781C0C5B8F42CA8288")),
        };
        public static int SelfTest()
        {
            int fail = 0;
            foreach (var (kw, app, want) in Vectors)
            {
                var got = Encipher85(app, BindKeyword(kw));
                bool ok = ByteEq(got, want);
                Console.WriteLine($"  kw={ToHex(kw)} app={ToHex(app)} -> {(ok ? "OK " : "FAIL")}  {ToHex(got)}");
                if (!ok) { Console.WriteLine($"      want: {ToHex(want)}"); fail++; }
            }
            Console.WriteLine(fail == 0 ? "SELFTEST: all cipher vectors byte-exact ✓"
                                        : $"SELFTEST: {fail} vector(s) FAILED ✗");
            return fail == 0 ? 0 : 2;
        }

        // ───────────────────────── helpers ─────────────────────────────────────
        static byte[] Hex(string s){ s=s.Replace(" ",""); var b=new byte[s.Length/2]; for(int i=0;i<b.Length;i++) b[i]=Convert.ToByte(s.Substring(i*2,2),16); return b; }
        static string ToHex(byte[] b)=>BitConverter.ToString(b).Replace("-"," ");
        static bool ByteEq(byte[] a,byte[] b){ if(a.Length!=b.Length) return false; for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false; return true; }
        static byte[] Concat(byte[] a,byte[] b){ var r=new byte[a.Length+b.Length]; Array.Copy(a,r,a.Length); Array.Copy(b,0,r,a.Length,b.Length); return r; }
        static byte[] Pad20(byte[] b){ var r=new byte[20]; Array.Copy(b,r,Math.Min(20,b.Length)); return r; }
        static string Ascii(List<byte> buf){ var sb=new StringBuilder(); foreach(var c in buf) sb.Append(c>=32&&c<127?(char)c:(c=='\n'||c=='\r'?(char)c:'.')); return sb.ToString(); }
        static string Field(string text,string key)
        {
            int i=text.IndexOf(key,StringComparison.Ordinal); if(i<0) return null; i+=key.Length;
            int j=i; while(j<text.Length && (char.IsDigit(text[j])||text[j]=='.')) j++;
            return j>i?text.Substring(i,j-i):null;
        }
    }
}
