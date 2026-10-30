namespace CanonG3100;

internal static class SelfChecks
{
    private sealed class Fake : IUsb
    {
        internal readonly List<(uint Code,byte[] Bytes)> Calls=new();
        internal readonly Queue<byte[]> Responses=new();
        public byte[] Control(uint code,byte[] input,int length)
        {
            Calls.Add((code,input.ToArray()));
            return code==NativeUsb.Send ? Array.Empty<byte>() : Responses.Dequeue();
        }
        public void Dispose() { }
    }
    private static void Require(bool yes,string label) { if(!yes) throw new Exception("SELFTEST FAILED: "+label); }
    private static void Reject(Action action,string label)
    {
        try { action(); } catch(Exception) { return; }
        throw new Exception("SELFTEST FAILED (accepted invalid input): "+label);
    }
    internal static int Run()
    {
        int opens=0,lists=0;
        Func<string,IUsb> open=_=>{ opens++; throw new Exception("Hardware must not be opened by this test"); };
        Func<IReadOnlyList<UsbUnit>> list=()=>{ lists++; throw new Exception("Hardware must not be enumerated by this test"); };
        Require(Program.Run(["--reset"],open,list)==0 && opens==0 && lists==0,"dry-run performs zero hardware I/O");
        Reject(()=>Program.Run(["--execute"],open,list),"execute without reset");
        Reject(()=>Program.Run(["--read","--experimental-g6000-profile","--baseline","missing.json"],open,list),"missing unit");
        Require(opens==0,"invalid CLI never opens device");
        Reject(()=>Program.ParseStatus("G3000 DL=12345"),"DL cannot masquerade as D");
        Require(Program.ParseStatus("G3000 D=100.0 DL=12345").Counter==100,"valid G3000 counter");
        Reject(()=>Program.ParseStatus("G6000 D=100.0"),"wrong model rejected");
        Reject(()=>Program.ParseStatus("G3000 D=9999.0"),"implausible counter rejected");
        Reject(()=>NativeUsb.SelectExact([new("path","ABC123",true)],"123"),"serial substring cannot match");
        Reject(()=>NativeUsb.SelectExact([new("normal","123",false)],"123"),"normal-mode path rejected");
        Reject(()=>NativeUsb.SelectExact([new("a","123",true),new("b","123",true)],"123"),"ambiguous device rejected");
        string dir=Path.Combine(Path.GetTempPath(),"canon-g3100-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string text="G3000 D=100.0 DL=12345";
            var baseline=new Baseline(1,"TEST-UNIT",Program.Profile,DateTime.UtcNow,"G3000",100,text,Program.Hash(text),"");
            Reject(()=>Program.ValidateBaseline(baseline,"OTHER"),"wrong-unit baseline");
            Reject(()=>Program.ValidateBaseline(baseline with { TimestampUtc=DateTime.UtcNow.AddHours(-1) },"TEST-UNIT"),"stale baseline");
            Reject(()=>Program.ValidateBaseline(baseline with { Status=text+"changed" },"TEST-UNIT"),"changed status");
            string budget=Path.Combine(dir,"used.json");
            using(var fake=new Fake())
            {
                fake.Responses.Enqueue([1,2]);
                Reject(()=>Program.StartSession(fake),"malformed keyword");
                Require(fake.Calls.Count(x=>x.Code==NativeUsb.Send)==1,"bad keyword sends session only, no selector");
            }
            using(var fake=new Fake())
            {
                fake.Responses.Enqueue([0xdb,0x3b,0xd2]);
                fake.Responses.Enqueue(Array.Empty<byte>());
                fake.Responses.Enqueue(new byte[20]);
                Reject(()=>Program.ExecuteReset(fake,baseline,budget),"malformed selector ACK");
                Require(fake.Calls.Count(x=>x.Code==NativeUsb.Send)==2 && !File.Exists(budget),"invalid ACK sends no clear and consumes no budget");
            }
            string once=Path.Combine(dir,"one-attempt.json");
            using(var fake=new Fake())
            {
                // Real G3100 read-session capture: this selector reply decrypts
                // to 001201100000... with session keyword 39B957.
                fake.Responses.Enqueue(Convert.FromHexString("39B957"));
                fake.Responses.Enqueue(Array.Empty<byte>());
                fake.Responses.Enqueue(Convert.FromHexString("CE6BBE1004166C37B0DA268BDEB566D859B5D59B"));
                fake.Responses.Enqueue(new byte[20]);
                Require(!Program.ExecuteReset(fake,baseline,once),"unconfirmed clear is not claimed accepted");
                Require(File.Exists(once) && fake.Calls.Count(x=>x.Code==NativeUsb.Send)==3,"clear sent exactly once and budget reserved despite bad clear ACK");
                int before=fake.Calls.Count;
                Reject(()=>Program.ExecuteReset(fake,baseline,once),"retry after uncertain clear");
                Require(fake.Calls.Count==before,"uncertain clear cannot be retried");
            }
            File.WriteAllText(budget,"reserved");
            string successBudget=Path.Combine(dir,"successful-attempt.json");
            using(var fake=new Fake())
            {
                // Actual G3100 reset transcript, including the positive clear echo.
                fake.Responses.Enqueue(Convert.FromHexString("EF3BB7"));
                fake.Responses.Enqueue(Array.Empty<byte>());
                fake.Responses.Enqueue(Convert.FromHexString("DA79263925FCF09AA308EED12082C7677B9A1AFF"));
                fake.Responses.Enqueue(Convert.FromHexString("8379959787E1F05B198AEED1EA31C716D726B77D"));
                Require(Program.ExecuteReset(fake,baseline,successBudget),"real captured G3100 clear ACK accepted");
                var sends=fake.Calls.Where(x=>x.Code==NativeUsb.Send).ToArray();
                Require(sends.Length==3 && Convert.ToHexString(sends[2].Bytes)=="8500002E460C9E44E1F0EF4C5F07D1DF4EC74015A7C644", "single clear is byte-exact to the actual G3100 capture");
            }
            using(var fake=new Fake())
            {
                Reject(()=>Program.ExecuteReset(fake,baseline,budget),"spent budget");
                Require(fake.Calls.Count==0,"spent budget refuses all I/O");
            }
            Reject(()=>Program.Run(["--reset","--execute","--unit","TEST-UNIT","--experimental-g6000-profile","--baseline",Path.Combine(dir,"missing.json")],open,list),"missing baseline");
            Require(opens==0,"missing baseline refuses all native opens");
        }
        finally
        {
            // Every path here is a fixed child of this freshly-created test directory.
            foreach(string file in Directory.GetFiles(dir)) File.Delete(file);
            Directory.Delete(dir);
        }
        Console.WriteLine("SAFETY SELFTESTS: passed (no hardware calls)");
        return 0;
    }
}
