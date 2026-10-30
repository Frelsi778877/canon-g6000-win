// Local Windows transport for the experimental G3100 adapter; no driver changes.
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CanonG3100;

internal interface IUsb : IDisposable
{
    byte[] Control(uint code, byte[] input, int outputLength);
}

internal record UsbUnit(string Path, string Serial, bool Service);

internal sealed class NativeUsb : IUsb
{
    internal const uint GetId = 0x220034, Send = 0x220038, Receive = 0x22003c;
    private static readonly Guid InterfaceGuid = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
    private readonly SafeFileHandle handle;
    private bool poisoned;

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData
    {
        public int Size;
        public Guid ClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Overlapped
    {
        public IntPtr Internal, InternalHigh;
        public uint Offset, OffsetHigh;
        public IntPtr Event;
    }
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, int inputLength, IntPtr output, int outputLength, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool GetOverlappedResult(SafeFileHandle handle, IntPtr overlapped, out uint returned, bool wait);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);

    internal static IReadOnlyList<UsbUnit> Enumerate()
    {
        var guid = InterfaceGuid;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var units = new List<UsbUnit>();
        try
        {
            for (uint i=0; ; i++)
            {
                var data = new InterfaceData { Size=Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data))
                {
                    int error=Marshal.GetLastWin32Error();
                    if (error==259) break;
                    throw new Win32Exception(error, "Enumerating USB printer interfaces");
                }
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size<6 || size>65536) throw new IOException("Invalid SetupAPI detail size");
                IntPtr detail=Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size==8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    string path=Marshal.PtrToStringUni(detail+4) ?? "";
                    string[] parts=path.Split('#');
                    if (parts.Length<4 || !parts[1].Contains("vid_04a9", StringComparison.OrdinalIgnoreCase)) continue;
                    bool service=parts[1].Contains("pid_12fe", StringComparison.OrdinalIgnoreCase);
                    bool normal=parts[1].Contains("pid_1794", StringComparison.OrdinalIgnoreCase);
                    if (service || normal) units.Add(new UsbUnit(path, parts[2], service));
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return units;
    }

    internal static string SelectExact(IReadOnlyList<UsbUnit> units, string serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.IndexOfAny(['#','\\','/'])>=0)
            throw new ArgumentException("An exact service-mode unit serial is required");
        var matches=units.Where(x=>x.Service && string.Equals(x.Serial, serial, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length!=1) throw new IOException($"Expected exactly one matching service-mode unit; found {matches.Length}");
        return matches[0].Path;
    }
    internal static IUsb OpenExact(string serial) => new NativeUsb(SelectExact(Enumerate(), serial));

    private NativeUsb(string path)
    {
        handle=CreateFile(path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Opening the pinned USB printer");
    }

    public byte[] Control(uint code, byte[] input, int outputLength)
    {
        if (poisoned) throw new IOException("Transport timed out; refusing subsequent I/O");
        if (code!=GetId && code!=Send && code!=Receive) throw new ArgumentException("IOCTL outside allowlist");
        input ??= Array.Empty<byte>();
        if (input.Length>4096 || outputLength<0 || outputLength>4096) throw new ArgumentOutOfRangeException();
        Console.WriteLine($"IOCTL {code:X8} TX {Convert.ToHexString(input)} OUT_MAX {outputLength}");
        IntPtr inp=IntPtr.Zero, output=IntPtr.Zero, ovp=IntPtr.Zero;
        bool release=true;
        using var completed=new EventWaitHandle(false, EventResetMode.ManualReset);
        try
        {
            if (input.Length>0) { inp=Marshal.AllocHGlobal(input.Length); Marshal.Copy(input,0,inp,input.Length); }
            if (outputLength>0) output=Marshal.AllocHGlobal(outputLength);
            ovp=Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());
            Marshal.StructureToPtr(new Overlapped { Event=completed.SafeWaitHandle.DangerousGetHandle() },ovp,false);
            bool immediate=DeviceIoControl(handle,code,inp,input.Length,output,outputLength,out uint n,ovp);
            if (!immediate)
            {
                int error=Marshal.GetLastWin32Error();
                if (error!=997) throw new Win32Exception(error, $"IOCTL {code:X8}");
                if (!completed.WaitOne(3000))
                {
                    poisoned=true;
                    CancelIoEx(handle,ovp);
                    if (!completed.WaitOne(3000))
                    {
                        // Leave unmanaged buffers alive until process exit if the driver
                        // has not completed cancellation. They must never be reused.
                        release=false;
                        completed.SafeWaitHandle.SetHandleAsInvalid();
                    }
                    throw new TimeoutException($"IOCTL {code:X8} timed out; cancellation requested; no retry");
                }
                if (!GetOverlappedResult(handle,ovp,out n,false))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),$"IOCTL {code:X8} completion");
            }
            if (n>outputLength) throw new IOException("Driver reported a reply larger than its output buffer");
            var bytes=new byte[(int)n];
            if (n>0) Marshal.Copy(output,bytes,0,(int)n);
            Console.WriteLine($"IOCTL {code:X8} RX {Convert.ToHexString(bytes)}");
            return bytes;
        }
        finally
        {
            if (release)
            {
                if (inp!=IntPtr.Zero) Marshal.FreeHGlobal(inp);
                if (output!=IntPtr.Zero) Marshal.FreeHGlobal(output);
                if (ovp!=IntPtr.Zero) Marshal.FreeHGlobal(ovp);
            }
        }
    }
    public void Dispose() => handle.Dispose();
}
