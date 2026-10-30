// Experimental G3100 adapter, 2026-10-29. This is an ALTERED implementation,
// derived from the G6000 protocol recovered by Jess Sullivan and Michael's
// Windows port (see LICENSE/upstream). Locally validated on one G3100 V3.010;
// this is not a compatibility guarantee for other units or firmware revisions.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CanonG3100;

internal sealed record Baseline(int Schema, string Serial, string Profile, DateTime TimestampUtc,
    string Model, double Counter, string Status, string StatusSha256, string EncryptedHex);

internal static class Program
{
    internal const string Profile="experimental-g6000-sr5-on-g3100-v1";
    internal static readonly byte[] Selector=Convert.FromHexString("85000010077C");
    internal static readonly byte[] Query=Convert.FromHexString("85000015");
    internal static readonly byte[] Clear=Convert.FromHexString("8500000D0000");
    private static readonly JsonSerializerOptions JsonOptions=new() { WriteIndented=true };

    private static int Main(string[] args)
    {
        try { return Run(args, NativeUsb.OpenExact, NativeUsb.Enumerate); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ABORTED: {ex.GetType().Name}: {ex.Message}");
            if(ex is System.ComponentModel.Win32Exception native)
                Console.Error.WriteLine($"WIN32_ERROR={native.NativeErrorCode}: {new System.ComponentModel.Win32Exception(native.NativeErrorCode).Message}");
            return 1;
        }
    }

    internal static int Run(string[] args, Func<string,IUsb> open, Func<IReadOnlyList<UsbUnit>> list)
    {
        var commands=new HashSet<string> {"--selftest","--list","--describe","--read","--reset"};
        var options=new Dictionary<string,string>();
        string command=null;
        for(int i=0;i<args.Length;i++)
        {
            string arg=args[i];
            if(commands.Contains(arg))
            {
                if(command!=null) throw new ArgumentException("Choose exactly one operation");
                command=arg;
            }
            else if(arg is "--execute" or "--experimental-g6000-profile")
            {
                if(!options.TryAdd(arg,"true")) throw new ArgumentException("Duplicate option: "+arg);
            }
            else if(arg is "--unit" or "--baseline")
            {
                if(++i>=args.Length || args[i].StartsWith("--") || !options.TryAdd(arg,args[i]))
                    throw new ArgumentException("Missing or repeated option value: "+arg);
            }
            else throw new ArgumentException("Unknown option: "+arg);
        }
        if(command==null)
        {
            if(args.Length!=0) throw new ArgumentException("An operation must accompany the options");
            Console.WriteLine("CanonG3100 EXPERIMENTAL: --selftest | --list | --describe --unit SERIAL | --read --unit SERIAL --experimental-g6000-profile --baseline FILE | --reset [--execute] --unit SERIAL --experimental-g6000-profile --baseline FILE");
            return 0;
        }
        bool execute=options.ContainsKey("--execute");
        if(execute && command!="--reset") throw new ArgumentException("--execute is valid only with --reset");
        if(command is "--selftest" or "--list")
        {
            if(options.Count!=0) throw new ArgumentException("This operation accepts no options");
            if(command=="--selftest") return Sr5Codec.SelfTest()==0 && SelfChecks.Run()==0 ? 0 : 2;
            foreach(var unit in list()) Console.WriteLine(JsonSerializer.Serialize(unit));
            return 0;
        }
        if(command=="--reset" && !execute)
        {
            Console.WriteLine("OFFLINE DRY RUN: no enumeration, handle, session or command is sent.");
            Console.WriteLine($"Profile {Profile}; selector {Convert.ToHexString(Selector)}; clear {Convert.ToHexString(Clear)}.");
            Console.WriteLine("These are application frames, not USB payloads. Live per-session keyword and successful G3100 read/selector ACK are required before an actual write.");
            return 0;
        }
        if(!options.TryGetValue("--unit",out string serial) || string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("Specify the exact --unit serial from service-mode --list");
        if(serial.IndexOfAny(['#','\\','/'])>=0) throw new ArgumentException("Invalid unit serial");
        if(command=="--describe")
        {
            if(options.Count!=1) throw new ArgumentException("--describe accepts only --unit");
            using var usb=open(serial);
            byte[] id=usb.Control(NativeUsb.GetId,Array.Empty<byte>(),4096);
            Console.WriteLine("IEEE1284 raw: "+Convert.ToHexString(id));
            Console.WriteLine("IEEE1284 text: "+Printable(id));
            return 0;
        }
        if(!options.ContainsKey("--experimental-g6000-profile"))
            throw new ArgumentException("This profile is unvalidated on G3100; explicit --experimental-g6000-profile required");
        if(!options.TryGetValue("--baseline",out string file)) throw new ArgumentException("--baseline FILE required");
        file=Path.GetFullPath(file);
        if(command=="--read")
        {
            if(File.Exists(file)) throw new IOException("Baseline exists; preserve it and select a new filename");
            using var usb=open(serial);
            Baseline baseline=ReadStatus(usb,serial);
            using var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            JsonSerializer.Serialize(output,baseline,JsonOptions);
            output.Flush(true);
            Console.WriteLine($"VALIDATED STATUS baseline saved: {file}; D={baseline.Counter.ToString(CultureInfo.InvariantCulture)}%. This is NOT a full EEPROM backup.");
            return 0;
        }
        if(command=="--reset")
        {
            Baseline baseline=LoadBaseline(file,serial);
            string budget=BudgetPath(file,serial);
            if(File.Exists(budget)) throw new IOException("The unit's one-write budget is already consumed. No automatic retry.");
            Directory.CreateDirectory(Path.GetDirectoryName(budget));
            string lockPath=budget+".lock";
            using var locked=new FileStream(lockPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            try
            {
                using var usb=open(serial);
                bool accepted=ExecuteReset(usb,baseline,budget);
                Console.WriteLine(accepted
                    ? "CLEAR ACCEPTED by an exact decoded echo. Persistence is NOT established. Power off with the printer POWER BUTTON, power on normally, then check 5B00 and independently reread D."
                    : "CLEAR OUTCOME UNCERTAIN. Budget remains consumed; do not retry. Use a clean POWER BUTTON shutdown and verify the actual state.");
                return accepted ? 0 : 3;
            }
            finally
            {
                locked.Dispose();
                File.Delete(lockPath);
            }
        }
        throw new ArgumentException("Invalid operation");
    }

    internal static string Printable(byte[] bytes)
    {
        var text=new StringBuilder();
        foreach(byte b in bytes) text.Append(b>=32 && b<127 || b==10 || b==13 ? (char)b : '.');
        return text.ToString();
    }
    internal static byte[] Keyword(byte[] raw)
    {
        if(raw.Length!=3 && !(raw.Length==4 && raw[3]==0))
            throw new IOException("Unexpected keyword length/format; no selector or reset sent");
        byte[] padded=new byte[4];
        Array.Copy(raw,padded,raw.Length);
        return Sr5Codec.BindKeyword(padded);
    }
    internal static byte[] StartSession(IUsb usb)
    {
        usb.Control(NativeUsb.Send,Convert.FromHexString("81000003"),0);
        return Keyword(usb.Control(NativeUsb.Receive,Convert.FromHexString("820000"),16));
    }
    internal static byte[] Wire(byte[] application,byte[] bound)
        => [0x85,0,0,..Sr5Codec.Encipher85(application,bound)];

    private static byte[] ReadAck(IUsb usb,byte[] bound,byte expected)
    {
        var bytes=new List<byte>();
        for(int i=0;i<12;i++)
        {
            byte[] part=usb.Control(NativeUsb.Receive,Convert.FromHexString("860000"),64);
            bytes.AddRange(part);
            if(bytes.Count>20) throw new IOException("Unexpected ACK size; not a single encrypted block");
            if(bytes.Count==20)
            {
                byte[] decoded=Sr5Codec.DecryptBlock(bytes.ToArray(),bound);
                Console.WriteLine("ACK decoded: "+Convert.ToHexString(decoded));
                if(decoded[0]!=0 || decoded[1]!=0x12 || decoded[2]!=1 || decoded[3]!=expected || decoded[4]!=0 || decoded[5]!=0)
                    throw new IOException("Decoded ACK does not match the selected protocol/command");
                return decoded;
            }
            Thread.Sleep(25);
        }
        throw new IOException("No complete selector/command ACK; no further command sent");
    }

    internal static (string Model,double Counter) ParseStatus(string text)
    {
        var model=Regex.Match(text,@"\bG(?:3000|3100)(?:\s+series)?\b",RegexOptions.CultureInvariant);
        if(!model.Success) throw new IOException("Status does not identify this G3000/G3100 model family");
        var d=Regex.Match(text,@"(?<![A-Za-z0-9_])D=(\d+(?:\.\d+)?)",RegexOptions.CultureInvariant);
        if(!d.Success || !double.TryParse(d.Groups[1].Value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double value)
            || !double.IsFinite(value) || value<0 || value>200)
            throw new IOException("No unambiguous plausible D= counter in service status");
        return (model.Value,value);
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static Baseline ReadStatus(IUsb usb,string serial)
    {
        byte[] bound=StartSession(usb);
        usb.Control(NativeUsb.Send,Wire(Selector,bound),0);
        ReadAck(usb,bound,0x10);
        usb.Control(NativeUsb.Send,Wire(Query,bound),0);
        var encrypted=new List<byte>();
        var decoded=new List<byte>();
        int consumed=0,empty=0;
        var timer=System.Diagnostics.Stopwatch.StartNew();
        bool found=false;
        for(int i=0;i<64 && timer.ElapsedMilliseconds<8000;i++)
        {
            byte[] part=usb.Control(NativeUsb.Receive,Convert.FromHexString("860000"),64);
            if(part.Length==0)
            {
                if(++empty>=12) break;
                Thread.Sleep(30); continue;
            }
            empty=0;
            encrypted.AddRange(part);
            if(encrypted.Count>16384) throw new IOException("Status exceeds bounded capture size");
            while(encrypted.Count-consumed>=20)
            {
                byte[] block=Sr5Codec.DecryptBlock(encrypted.GetRange(consumed,20).ToArray(),bound);
                // Service text is streamed in blocks; preserve data verbatim apart
                // from recognizable framing so text cannot be invented by padding.
                if(block[0]==0 && block[1]==0x12 && block[2]==1) decoded.AddRange(block.Skip(3));
                else decoded.AddRange(block);
                consumed+=20;
            }
            string current=Printable(decoded.ToArray());
            try { ParseStatus(current); found=current.Contains("DL=",StringComparison.Ordinal); }
            catch(IOException) { }
            if(found) break;
        }
        string status=Printable(decoded.ToArray());
        Console.WriteLine("----- DECODED SERVICE STATUS -----\n"+status+"\n----------------------------------");
        Console.WriteLine($"Captured {encrypted.Count} encrypted bytes; incomplete tail={encrypted.Count-consumed}");
        var parsed=ParseStatus(status);
        if(encrypted.Count!=consumed) throw new IOException("Incomplete status block; baseline not saved");
        return new Baseline(1,serial,Profile,DateTime.UtcNow,parsed.Model,parsed.Counter,status,Hash(status),Convert.ToHexString(encrypted.ToArray()));
    }

    internal static Baseline LoadBaseline(string file,string serial)
    {
        var baseline=JsonSerializer.Deserialize<Baseline>(File.ReadAllText(file)) ?? throw new IOException("Missing baseline");
        ValidateBaseline(baseline,serial);
        return baseline;
    }
    internal static void ValidateBaseline(Baseline baseline,string serial)
    {
        if(baseline.Schema!=1 || baseline.Profile!=Profile || !string.Equals(baseline.Serial,serial,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Baseline schema/profile/unit does not match");
        var age=DateTime.UtcNow-baseline.TimestampUtc.ToUniversalTime();
        if(age>TimeSpan.FromMinutes(30) || age<TimeSpan.FromSeconds(-30)) throw new IOException("Baseline is stale or from the future");
        if(baseline.Status==null || baseline.StatusSha256!=Hash(baseline.Status)) throw new IOException("Baseline status hash mismatch");
        var parsed=ParseStatus(baseline.Status);
        if(parsed.Model!=baseline.Model || parsed.Counter!=baseline.Counter || baseline.Counter<=0)
            throw new IOException("Baseline model/counter fields disagree or counter is already zero");
    }
    internal static string BudgetPath(string baseline,string serial)
        => Path.Combine(Path.GetDirectoryName(baseline),"state",Hash(serial.ToUpperInvariant())+".reset-used.json");

    internal static bool ExecuteReset(IUsb usb,Baseline baseline,string budget)
    {
        ValidateBaseline(baseline,baseline.Serial);
        if(File.Exists(budget)) throw new IOException("Write budget already consumed");
        byte[] bound=StartSession(usb);
        // A previous status query can leave unread blocks. Discard only the
        // existing stream before selecting the reset row (upstream reset path).
        bool drained=false;
        for(int i=0;i<64;i++)
        {
            byte[] stale=usb.Control(NativeUsb.Receive,Convert.FromHexString("860000"),64);
            if(stale.Length==0) { drained=true; break; }
        }
        if(!drained) throw new IOException("Status stream did not drain within its bound; reset not sent");
        usb.Control(NativeUsb.Send,Wire(Selector,bound),0);
        ReadAck(usb,bound,0x10);
        // Reserve permanently BEFORE clear. Uncertainty never authorizes a retry.
        using(var reservation=new FileStream(budget,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            JsonSerializer.Serialize(reservation,new { baseline.Serial,Profile,ReservedUtc=DateTime.UtcNow,StatusSha256=baseline.StatusSha256,ApplicationFrame=Convert.ToHexString(Clear) },JsonOptions);
            reservation.Flush(true);
        }
        usb.Control(NativeUsb.Send,Wire(Clear,bound),0);
        try { ReadAck(usb,bound,0x0d); return true; }
        catch(IOException ex) { Console.Error.WriteLine("Clear sent once; ACK uncertain: "+ex.Message); return false; }
    }
}
