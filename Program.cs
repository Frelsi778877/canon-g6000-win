// CanonG6000Read — read the Canon G6000-series (G6040) ink-absorber / waste
// counter over the usbprint VENDOR service protocol. READ-ONLY. No reset, no key.
//
// Protocol + cipher ported from the open-source, right-to-repair project
// github.com/Jesssullivan/canon-megatank-reset (MIT), and validated BYTE-EXACT
// against this printer's own WicReset application.log capture (see --selftest).
//
// Requires the printer in SERVICE MODE (enumerates as USB 04a9:12fe) over USB.
// Build:  dotnet build -c Release     Run: CanonG6000Read [--selftest]
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CanonG6000Read
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

    static class Program
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
        static byte[] BindKeyword(byte[] dev)
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
        static byte[] Encipher85(byte[] app, byte[] bound)
            => Functor2(Envelope3(app), bound, send: true);

        // Decrypt one 20-byte response block -> plaintext bytes.
        static byte[] DecryptBlock(byte[] block, byte[] bound)
            => Functor2(block, bound, send: false);

        // ───────────────────────── self-test (no hardware) ─────────────────────
        static (byte[] kw, byte[] app, byte[] want)[] Vectors = new[]
        {
            (Hex("DB3BD200"), Hex("85000010077C"), Hex("3A8A9570EAFFC2752999E0A22162CE06A32BFB00")),
            (Hex("DB3BD200"), Hex("85000015"),     Hex("3A8A957026FFC275299CE0A22162E4EFA32BFBE2")),
            (Hex("E49BF700"), Hex("85000010077C"), Hex("16B2ADF46E761DA42EA91A781C0C71F242CA826A")),
            (Hex("E49BF700"), Hex("8500000D0000"), Hex("16B2ADF4CFE01DA42EB41A781C0C5B8F42CA8288")),
        };
        static int SelfTest()
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

        // ───────────────────────── Windows usbprint transport ──────────────────
        static readonly Guid GUID_DEVINTERFACE_USBPRINT =
            new Guid("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
        const uint IOCTL_SEND = 0x220038;  // VENDOR_SET_COMMAND
        const uint IOCTL_RECV = 0x22003c;  // VENDOR_GET_COMMAND
        const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr dev, ref Guid g, uint idx, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, ref int reqd, IntPtr devInfo);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int ret, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);
        static readonly IntPtr INVALID = new IntPtr(-1);

        static string FindDevicePath()
        {
            var g = GUID_DEVINTERFACE_USBPRINT;
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == INVALID) throw new Exception("SetupDiGetClassDevs failed");
            try
            {
                var did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did); i++)
                {
                    int reqd = 0;
                    SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, ref reqd, IntPtr.Zero);
                    if (reqd == 0) continue;
                    IntPtr detail = Marshal.AllocHGlobal(reqd);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize = 8 (x64) / 6 (x86)
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref did, detail, reqd, ref reqd, IntPtr.Zero))
                        {
                            string path = Marshal.PtrToStringUni(detail + 4);
                            if (path != null && path.IndexOf("vid_04a9", StringComparison.OrdinalIgnoreCase) >= 0
                                             && path.IndexOf("pid_12fe", StringComparison.OrdinalIgnoreCase) >= 0)
                                return path;
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return null;
        }

        static IntPtr Open(string path)
        {
            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                  IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID) throw new Exception($"CreateFile failed (err {Marshal.GetLastWin32Error()})");
            return h;
        }
        static void Send(IntPtr h, byte[] frame)
        {
            if (!DeviceIoControl(h, IOCTL_SEND, frame, frame.Length, null, 0, out _, IntPtr.Zero))
                throw new Exception($"SEND ioctl failed (err {Marshal.GetLastWin32Error()})");
        }
        static byte[] Recv(IntPtr h, byte[] header, int maxLen)
        {
            var outBuf = new byte[maxLen];
            if (!DeviceIoControl(h, IOCTL_RECV, header, header.Length, outBuf, outBuf.Length, out int n, IntPtr.Zero))
                throw new Exception($"RECV ioctl failed (err {Marshal.GetLastWin32Error()})");
            var r = new byte[n]; Array.Copy(outBuf, r, n); return r;
        }
        // Read one get_command ack, decrypt it, strip the 00 12 01 envelope header.
        static byte[] ReadAck(IntPtr h, byte[] bound)
        {
            byte[] blk = Array.Empty<byte>();
            for (int i = 0; i < 300 && blk.Length == 0; i++)
            { blk = Recv(h, Hex("860000"), 64); if (blk.Length == 0) System.Threading.Thread.Sleep(3); }
            if (blk.Length == 0) return blk;
            var dec = DecryptBlock(blk.Length == 20 ? blk : Pad20(blk), bound);
            return (dec.Length >= 3 && dec[0] == 0x00 && dec[1] == 0x12 && dec[2] == 0x01)
                ? dec[3..] : dec;
        }

        // ───────────────────────── read session ────────────────────────────────
        static int ReadCounter()
        {
            string path = FindDevicePath();
            if (path == null)
            {
                Console.Error.WriteLine("No service-mode printer found (USB 04a9:12fe).");
                Console.Error.WriteLine("Put the G6040 in service mode and connect USB, then retry.");
                return 1;
            }
            Console.WriteLine($"device: {path}");
            IntPtr h = Open(path);
            try
            {
                Send(h, Hex("81000003"));                      // set_session
                var kwRaw = Recv(h, Hex("820000"), 16);        // get_keyword
                var dev = new byte[4];
                Array.Copy(kwRaw, dev, Math.Min(4, kwRaw.Length)); // 3-byte kw padded to 4
                Console.WriteLine($"keyword: {ToHex(kwRaw)} -> seed {ToHex(dev)}");
                var bound = BindKeyword(dev);

                var selFrame = Concat(Hex("850000"), Encipher85(Hex("85000010077C"), bound));
                Send(h, selFrame);                              // selector
                Recv(h, Hex("860000"), 64);                     // selector ack (poll once)
                var qFrame = Concat(Hex("850000"), Encipher85(Hex("85000015"), bound));
                Send(h, qFrame);                                // status query

                // The status dump streams 20-byte blocks over repeated get_command
                // reads; an empty read means "not ready yet" (poll again), not EOF.
                var enc = new List<byte>();
                var plain = new List<byte>();
                int consumed = 0, empties = 0, blocks = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 15000)          // up to 15 s
                {
                    var got = Recv(h, Hex("860000"), 64);
                    if (got.Length == 0)
                    {
                        if (++empties > 400) break;             // ~several s of nothing => done
                        System.Threading.Thread.Sleep(3);
                        continue;
                    }
                    empties = 0; blocks++;
                    enc.AddRange(got);
                    while (enc.Count - consumed >= 20)
                    {
                        var chunk = enc.GetRange(consumed, 20).ToArray();
                        plain.AddRange(DecryptBlock(chunk, bound));
                        consumed += 20;
                    }
                    if (Ascii(plain).Contains("DL=")) break;    // counter reached
                }
                string text = Ascii(plain);
                // The dump's clean copy starts at the "G6000" model banner; trim
                // any leading partial/!aligned first block for a tidy printout.
                int g = text.LastIndexOf("G6000", StringComparison.Ordinal);
                string shown = (g > 0 ? text.Substring(g) : text).Replace("\r", "").Trim();
                Console.WriteLine($"({blocks} blocks, {enc.Count} bytes, {sw.ElapsedMilliseconds} ms)");
                Console.WriteLine("\n----- decoded service status -----");
                Console.WriteLine(shown);
                Console.WriteLine("----------------------------------");
                var d = Field(text, "D=");
                var dl = Field(text, "DL=");
                if (d != null) Console.WriteLine($"\nWASTE ABSORBER (D): {d}%   (DL raw: {dl})");
                else Console.WriteLine("\n(could not locate D= in the response — paste the block above)");
                return 0;
            }
            finally { CloseHandle(h); }
        }

        // ───────────────────────── reset (gated, dry-run default) ──────────────
        // Clears the 'common' absorber counter (the 5B00 target): selector 10 07 7C
        // then the write operand 0D 00 00. Protocol from the open-source MIT
        // right-to-repair project; validated byte-exact against this printer.
        static int ResetCounter(bool execute)
        {
            string path = FindDevicePath();
            if (path == null)
            {
                Console.Error.WriteLine("No service-mode printer found (USB 04a9:12fe).");
                return 1;
            }
            Console.WriteLine($"device: {path}");
            IntPtr h = Open(path);
            try
            {
                Send(h, Hex("81000003"));                       // set_session
                var kwRaw = Recv(h, Hex("820000"), 16);         // get_keyword
                var dev = new byte[4];
                Array.Copy(kwRaw, dev, Math.Min(4, kwRaw.Length));
                var bound = BindKeyword(dev);
                Console.WriteLine($"keyword: {ToHex(kwRaw)}");

                var selFrame   = Concat(Hex("850000"), Encipher85(Hex("85000010077C"), bound)); // select 'common'
                var clearFrame = Concat(Hex("850000"), Encipher85(Hex("8500000D0000"), bound)); // CLEAR (the write)
                Console.WriteLine($"selector frame : {ToHex(selFrame)}");
                Console.WriteLine($"clear frame    : {ToHex(clearFrame)}   <-- the EEPROM write (0D 00 00)");

                if (!execute)
                {
                    Console.WriteLine("\nDRY RUN — nothing was sent to the printer.");
                    Console.WriteLine("To actually reset, re-run:  CanonG6000Read.exe --reset --execute");
                    return 0;
                }

                Console.WriteLine("\n*** EXECUTING WASTE-COUNTER RESET (writes printer EEPROM) ***");
                Console.WriteLine("    Note: this clears the 5B00 software lock; it does NOT empty the");
                Console.WriteLine("    physical absorber pad. Service the pad for heavy use.\n");

                // Drain any stale status left buffered in the printer (e.g. from a
                // prior read that stopped early) so the command acks below are clean.
                int drained = 0;
                for (int i = 0; i < 128; i++)
                {
                    var junk = Recv(h, Hex("860000"), 64);
                    if (junk.Length == 0) break;
                    drained += junk.Length;
                }
                if (drained > 0) Console.WriteLine($"(drained {drained} stale bytes before reset)");

                // WicReset's validated reset: each set_command is followed by its
                // get_command ack. Selector acks '10 00 00 ...'; the clear acks
                // '0D 00 00 ...' — that 0D 00 00 echo is the success confirmation.
                Send(h, selFrame);                              // select common waste row
                var selAck = ReadAck(h, bound);
                Console.WriteLine($"selector ack : {ToHex(selAck)}");
                Send(h, clearFrame);                            // THE CLEAR WRITE (0D 00 00)
                var clrAck = ReadAck(h, bound);
                Console.WriteLine($"clear ack    : {ToHex(clrAck)}");

                bool ok = clrAck.Length >= 3 && clrAck[0] == 0x0D && clrAck[1] == 0x00 && clrAck[2] == 0x00;
                Console.WriteLine(ok
                    ? "\n>>> CLEAR CONFIRMED by printer (0D 00 00 echo). <<<"
                    : "\n>>> WARNING: no 0D 00 00 confirmation — the clear may NOT have been accepted. <<<");

                Console.WriteLine("\nNow power-cycle the printer with its POWER BUTTON (clean shutdown)");
                Console.WriteLine("to COMMIT the write to EEPROM and leave service mode (-> 04a9:1865).");
                Console.WriteLine("An abrupt USB unplug does NOT commit. Then re-enter service mode and read.");
                return ok ? 0 : 2;
            }
            finally { CloseHandle(h); }
        }

        static int Main(string[] args)
        {
            try
            {
                if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest();
                bool reset = Array.IndexOf(args, "--reset") >= 0;
                bool execute = Array.IndexOf(args, "--execute") >= 0;
                Console.WriteLine("CanonG6000Read. Running cipher self-test first:\n");
                int st = SelfTest();
                if (st != 0) { Console.Error.WriteLine("Cipher self-test failed; aborting."); return st; }
                Console.WriteLine();
                return reset ? ResetCounter(execute) : ReadCounter();
            }
            catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 1; }
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
