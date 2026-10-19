using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
[assembly: System.Reflection.AssemblyTitle("Xbox Reconnect")]
[assembly: System.Reflection.AssemblyVersion("2.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.1.0.0")]

internal sealed class Adapter {
    public string Name, Id;
    public override string ToString() { return Name + "  /  " + (Id.IndexOf("PID_02E6", StringComparison.OrdinalIgnoreCase) >= 0 ? "Original adapter" : "Compact adapter"); }
}
internal sealed class ControllerSnapshot {
    internal uint Slot; internal bool Connected; internal uint Error;
    internal string Battery="Not reported", Link="Not reported", Input="Idle";
    internal string Describe() {
        return "Slot "+(Slot+1)+"  /  "+(Connected ? "CONNECTED  /  "+Link+"  /  Battery: "+Battery+"  /  "+Input : "Disconnected"+(Error!=1167?" (Windows error "+Error+")":""));
    }
}
internal static class Controllers {
    [StructLayout(LayoutKind.Sequential)] internal struct Gamepad { internal ushort Buttons; internal byte LeftTrigger,RightTrigger; internal short LX,LY,RX,RY; }
    [StructLayout(LayoutKind.Sequential)] internal struct State { internal uint Packet; internal Gamepad Pad; }
    [StructLayout(LayoutKind.Sequential)] internal struct Battery { internal byte Type,Level; }
    [StructLayout(LayoutKind.Sequential)] internal struct Vibration { internal ushort Left,Right; }
    [StructLayout(LayoutKind.Sequential)] internal struct Capabilities { internal byte Type,SubType; internal ushort Flags; internal Gamepad Pad; internal Vibration Vibration; }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index,out State state);
    [DllImport("xinput1_4.dll")] static extern uint XInputGetBatteryInformation(uint index,byte deviceType,out Battery battery);
    [DllImport("xinput1_4.dll")] static extern uint XInputGetCapabilities(uint index,uint flags,out Capabilities capabilities);
    internal static string BatteryText(byte type,byte level) {
        if(type==0) return "Not present"; if(type==1) return "Wired power";
        if(type!=2 && type!=3) return "Unknown";
        return level==0?"Empty":level==1?"Low":level==2?"Medium":level==3?"Full":"Unknown";
    }
    internal static ControllerSnapshot Read(uint slot) {
        State state; uint result=XInputGetState(slot,out state);
        var snapshot=new ControllerSnapshot { Slot=slot,Connected=result==0,Error=result };
        if(!snapshot.Connected) return snapshot;
        Battery battery; if(XInputGetBatteryInformation(slot,0,out battery)==0) snapshot.Battery=BatteryText(battery.Type,battery.Level);
        Capabilities caps; if(XInputGetCapabilities(slot,0,out caps)==0) snapshot.Link=(caps.Flags&2)!=0?"Wireless":"Wired / unspecified";
        if(state.Pad.Buttons!=0) snapshot.Input="Buttons: 0x"+state.Pad.Buttons.ToString("X4");
        else if(state.Pad.LeftTrigger>30 || state.Pad.RightTrigger>30 || Math.Abs((int)state.Pad.LX)>8000 || Math.Abs((int)state.Pad.LY)>8000 || Math.Abs((int)state.Pad.RX)>9000 || Math.Abs((int)state.Pad.RY)>9000) snapshot.Input="Input active";
        return snapshot;
    }
}
internal static class InputServices {
    internal static bool Matches(string name,string display) {
        return string.Equals(name,"GameInputSvc",StringComparison.OrdinalIgnoreCase)
            || string.Equals(name,"GameInputRedistService",StringComparison.OrdinalIgnoreCase)
            || string.Equals(name,"GameInputRedistSvc",StringComparison.OrdinalIgnoreCase)
            || string.Equals(display,"GameInput Service",StringComparison.OrdinalIgnoreCase)
            || string.Equals(display,"GameInput Redist Service",StringComparison.OrdinalIgnoreCase);
    }
    internal static List<string> Names() {
        var result=new List<string>();
        foreach(var service in ServiceController.GetServices()) {
            using(service) if(Matches(service.ServiceName,service.DisplayName)) result.Add(service.ServiceName);
        }
        return result;
    }
    internal static string Details(string name) {
        using(var service=new ServiceController(name)) {
            string result=name+" ("+service.DisplayName+"): "+service.Status;
            try {
                using(var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name)) {
                    if(key!=null) {
                        string path=Convert.ToString(key.GetValue("ImagePath"));
                        result+="\r\n  Start setting: "+Convert.ToString(key.GetValue("Start"))+"; executable: "+path;
                        Match match=Regex.Match(Environment.ExpandEnvironmentVariables(path),"^\\s*(?:\"(?<file>[^\"]+)\"|(?<file>.+?\\.exe))(?:\\s|$)",RegexOptions.IgnoreCase);
                        if(match.Success && System.IO.File.Exists(match.Groups["file"].Value)) result+="\r\n  File version: "+FileVersionInfo.GetVersionInfo(match.Groups["file"].Value).FileVersion;
                    }
                }
            } catch(Exception ex) { result+="\r\n  Version/configuration unavailable: "+ex.Message; }
            return result;
        }
    }
}
internal static class Devices {
    [StructLayout(LayoutKind.Sequential)] struct Info { public uint Size; public Guid Class; public uint DevInst; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref Info info);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref Info info, StringBuilder id, uint size, out uint required);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref Info info, uint prop, out uint type, byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    internal static bool Allowed(string name, string id) {
        return Regex.IsMatch(name ?? "", @"^Xbox Wireless Adapter for Windows(?: #\d+)?$", RegexOptions.IgnoreCase)
            && Regex.IsMatch(id ?? "", @"^USB\\VID_045E&PID_(02E6|02FE)\\[^""\r\n]+$", RegexOptions.IgnoreCase);
    }
    static string Property(IntPtr set, ref Info info, uint property) {
        byte[] buffer = new byte[4096]; uint type, required;
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out type, buffer, (uint)buffer.Length, out required)) return "";
        return Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(required, (uint)buffer.Length)).TrimEnd('\0');
    }
    internal static List<Adapter> Read() {
        var result = new List<Adapter>();
        Guid net = new Guid("4d36e972-e325-11ce-bfc1-08002be10318");
        IntPtr set = SetupDiGetClassDevs(ref net, null, IntPtr.Zero, 2);
        if (set == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try {
            for (uint i=0;;i++) {
                Info info = new Info(); info.Size=(uint)Marshal.SizeOf(typeof(Info));
                if (!SetupDiEnumDeviceInfo(set, i, ref info)) {
                    int error=Marshal.GetLastWin32Error(); if(error==259) break;
                    throw new System.ComponentModel.Win32Exception(error);
                }
                StringBuilder id = new StringBuilder(4096); uint required;
                if (!SetupDiGetDeviceInstanceId(set, ref info, id, (uint)id.Capacity, out required)) continue;
                string name=Property(set,ref info,12); if(name.Length==0) name=Property(set,ref info,0);
                if (Allowed(name,id.ToString())) result.Add(new Adapter { Name=name, Id=id.ToString() });
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
}
internal sealed class Card : Panel {
    internal Card() { BackColor=Color.FromArgb(26,33,30); Padding=new Padding(22); }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using(var pen=new Pen(Color.FromArgb(48,60,52))) e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1); }
}
internal static class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length==2 && args[0]=="--read-services") {
            var details=new StringBuilder();
            try {
                foreach(string name in ServiceSettings.Names()) {
                    var snapshot=ServiceSettings.Read(name);
                    details.AppendLine(name+" | "+snapshot.Status+" | "+snapshot.Mode+" | "+snapshot.Version);
                }
                System.IO.File.WriteAllText(args[1],details.ToString()); return 0;
            } catch(Exception ex) { System.IO.File.WriteAllText(args[1],ex.ToString()); return 2; }
        }
        if(args.Length>0 && args[0]=="--self-test") {
            bool ok=Devices.Allowed("Xbox Wireless Adapter for Windows",@"USB\VID_045E&PID_02E6\TEST")
                && Devices.Allowed("Xbox Wireless Adapter for Windows #2",@"USB\VID_045E&PID_02FE\TEST")
                && !Devices.Allowed("Wi-Fi",@"USB\VID_045E&PID_02E6\TEST")
                && !Devices.Allowed("Xbox Wireless Adapter for Windows",@"USB\VID_9999&PID_02E6\TEST")
                && !Devices.Allowed("Xbox Wireless Adapter for Windows",@"USB\VID_045E&PID_02E6&MI_00\TEST")
                && !Devices.Allowed("Xbox Wireless Adapter for Windows","USB\\VID_045E&PID_02E6\\bad\"id");
            ok=ok && Marshal.SizeOf(typeof(Controllers.Gamepad))==12
                && Marshal.SizeOf(typeof(Controllers.State))==16
                && Marshal.SizeOf(typeof(Controllers.Battery))==2
                && Marshal.SizeOf(typeof(Controllers.Capabilities))==20
                && Controllers.BatteryText(2,1)=="Low" && Controllers.BatteryText(3,3)=="Full"
                && Controllers.BatteryText(1,0)=="Wired power" && Controllers.BatteryText(255,0)=="Unknown"
                && new ControllerSnapshot { Slot=0,Connected=false,Error=1167 }.Describe()=="Slot 1  /  Disconnected"
                && InputServices.Matches("GameInputRedistService","GameInput Redist Service")
                && InputServices.Matches("GameInputSvc","GameInput Service")
                && InputServices.Matches("GameInputRedistSvc","")
                && InputServices.Matches("OtherInternalName","GameInput Redist Service")
                && !InputServices.Matches("GamingServices","Gaming Services")
                && !InputServices.Matches("OtherService","Other Service");
            for(uint i=0;i<4;i++) { ControllerSnapshot live=Controllers.Read(i); ok=ok && live.Slot==i; }
            return ok && SettingsTests.Run()?0:1;
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==1 && args[0]=="--ui-test") {
            using(var form=new ReconnectForm(true)) { form.Show(); Application.DoEvents(); return form.TestDropdowns()?0:1; }
        }
        if(args.Length==2 && args[0]=="--preview") {
            using(var form=new ReconnectForm(true)) { form.Show(); Application.DoEvents(); using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,form.ClientRectangle.Size==form.Size?form.ClientRectangle:new Rectangle(Point.Empty,form.Size)); bitmap.Save(args[1]); } }
            return 0;
        }
        Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message,"Xbox Reconnect",MessageBoxButtons.OK,MessageBoxIcon.Error); };
        Application.Run(new ReconnectForm(false)); return 0;
    }
}
