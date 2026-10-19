using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;

internal sealed class RedistSnapshot {
    internal string Name, Version, Path; internal uint Startup; internal bool Delayed;
    internal ServiceControllerStatus Status; internal bool CanStop;
    internal string Mode { get { return ServiceSettings.ModeName(Startup,Delayed); } }
}
internal static class ServiceSettings {
    [StructLayout(LayoutKind.Sequential)] struct Config {
        internal uint Type,Start,Error;
        internal IntPtr BinaryPath,LoadGroup; internal uint Tag;
        internal IntPtr Dependencies,Account,DisplayName;
    }
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenSCManager(string machine,string database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenService(IntPtr scm,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryServiceConfig(IntPtr service,IntPtr buffer,uint size,out uint required);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryServiceConfig2(IntPtr service,uint level,IntPtr buffer,uint size,out uint required);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ChangeServiceConfig(IntPtr service,uint type,uint start,uint error,string path,string group,IntPtr tag,string dependencies,string account,string password,string display);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ChangeServiceConfig2(IntPtr service,uint level,ref int value);
    internal static string ModeName(uint start,bool delayed) {
        return start==2?(delayed?"Automatic (Delayed Start)":"Automatic"):start==3?"Manual":start==4?"Disabled":"Unsupported ("+start+")";
    }
    internal static uint ModeStart(int index) {
        if(index<0 || index>3) throw new ArgumentOutOfRangeException("index");
        return index<2?2u:index==2?3u:4u;
    }
    internal static int ModeIndex(uint start,bool delayed) {
        return start==2?(delayed?1:0):start==3?2:start==4?3:-1;
    }
    internal static bool IsRedist(string name,string display) {
        return string.Equals(name,"GameInputRedistService",StringComparison.OrdinalIgnoreCase)
            || string.Equals(name,"GameInputRedistSvc",StringComparison.OrdinalIgnoreCase)
            || string.Equals(display,"GameInput Redist Service",StringComparison.OrdinalIgnoreCase);
    }
    internal static List<string> Names() {
        var names=new List<string>();
        foreach(var service in ServiceController.GetServices()) using(service) {
            if(IsRedist(service.ServiceName,service.DisplayName)) names.Add(service.ServiceName);
        }
        names.Sort(StringComparer.OrdinalIgnoreCase); return names;
    }
    internal static void Validate(string name) {
        using(var service=new ServiceController(name)) {
            if(!IsRedist(service.ServiceName,service.DisplayName)) throw new InvalidOperationException("Only GameInput Redist Service can be changed by this app.");
        }
    }
    static IntPtr Open(string name,uint access,out IntPtr scm) {
        scm=OpenSCManager(null,null,1);
        if(scm==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr service=OpenService(scm,name,access);
        if(service==IntPtr.Zero) { int error=Marshal.GetLastWin32Error(); CloseServiceHandle(scm); scm=IntPtr.Zero; throw new Win32Exception(error); }
        return service;
    }
    internal static RedistSnapshot Read(string name) {
        Validate(name); IntPtr scm,service=Open(name,1,out scm);
        try {
            uint required; QueryServiceConfig(service,IntPtr.Zero,0,out required);
            if(required==0) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr buffer=Marshal.AllocHGlobal((int)required);
            try {
                if(!QueryServiceConfig(service,buffer,required,out required)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var config=(Config)Marshal.PtrToStructure(buffer,typeof(Config));
                var result=new RedistSnapshot { Name=name,Startup=config.Start,Path=Marshal.PtrToStringUni(config.BinaryPath),Version="Not reported" };
                IntPtr delayed=Marshal.AllocHGlobal(4);
                try {
                    if(!QueryServiceConfig2(service,3,delayed,4,out required)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    result.Delayed=Marshal.ReadInt32(delayed)!=0;
                } finally { Marshal.FreeHGlobal(delayed); }
                using(var controller=new ServiceController(name)) { result.Status=controller.Status; result.CanStop=controller.CanStop; }
                var match=System.Text.RegularExpressions.Regex.Match(Environment.ExpandEnvironmentVariables(result.Path??""),"^\\s*(?:\"(?<file>[^\"]+)\"|(?<file>.+?\\.exe))(?:\\s|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if(match.Success && System.IO.File.Exists(match.Groups["file"].Value)) result.Version=System.Diagnostics.FileVersionInfo.GetVersionInfo(match.Groups["file"].Value).FileVersion;
                return result;
            } finally { Marshal.FreeHGlobal(buffer); }
        } finally { CloseServiceHandle(service); CloseServiceHandle(scm); }
    }
    static void SetRaw(IntPtr service,uint startup,bool delayed) {
        if(!ChangeServiceConfig(service,uint.MaxValue,startup,uint.MaxValue,null,null,IntPtr.Zero,null,null,null,null)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if(startup==2) { int flag=delayed?1:0; if(!ChangeServiceConfig2(service,3,ref flag)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }
    internal static void Apply(string name,int mode) {
        uint startup=ModeStart(mode); RedistSnapshot before=Read(name); IntPtr scm,service=Open(name,3,out scm);
        try {
            ApplyWithRollback(delegate(uint value,bool delayed) { SetRaw(service,value,delayed); },before.Startup,before.Delayed,startup,mode==1);
        } finally { CloseServiceHandle(service); CloseServiceHandle(scm); }
        RedistSnapshot after=Read(name);
        if(after.Startup!=startup || (startup==2 && after.Delayed!=(mode==1))) throw new InvalidOperationException("The setting changed again or could not be verified. Refresh to see the current setting.");
    }
    internal static void ApplyWithRollback(Action<uint,bool> write,uint oldMode,bool oldDelay,uint newMode,bool newDelay) {
        try { write(newMode,newDelay); }
        catch(Exception original) {
            try { write(oldMode,oldDelay); }
            catch(Exception rollback) { throw new InvalidOperationException("Could not finish applying settings: "+original.Message+". Restoring the previous setting also failed: "+rollback.Message+". Refresh to see the actual setting."); }
            throw new InvalidOperationException("Startup setting was not applied; previous setting restored. "+original.Message);
        }
    }
    internal static void Control(string name,string action) {
        RedistSnapshot before=Read(name);
        if((action=="Start" || action=="Restart") && before.Startup==4) throw new InvalidOperationException("The service is disabled. Apply Automatic or Manual first, then start it.");
        using(var service=new ServiceController(name)) {
            if(action=="Start") {
                if(service.Status==ServiceControllerStatus.Running) return;
                if(service.Status!=ServiceControllerStatus.Stopped) throw new InvalidOperationException("The service is changing state. Wait, then try again.");
                service.Start(); service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(25)); return;
            }
            if(action!="Stop" && action!="Restart") throw new ArgumentException("Unknown service action.");
            if(service.Status==ServiceControllerStatus.Stopped) {
                if(action=="Stop") return;
                throw new InvalidOperationException("The service is stopped. Use Start.");
            }
            if(service.Status!=ServiceControllerStatus.Running || !service.CanStop) throw new InvalidOperationException("The service cannot currently be stopped. Wait, then refresh.");
            foreach(var dependent in service.DependentServices) using(dependent) {
                if(dependent.Status!=ServiceControllerStatus.Stopped) throw new InvalidOperationException("An active dependent service prevents stopping. Close games first. No dependent services were stopped.");
            }
            // Use ControlService directly: refuse active dependents instead of recursively stopping them.
            IntPtr scm,handle=Open(name,0x20,out scm);
            try { NativeStatus status; if(!ControlService(handle,1,out status)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
            finally { CloseServiceHandle(handle); CloseServiceHandle(scm); }
            service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(25));
            if(action=="Restart") { service.Start(); service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(25)); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeStatus { internal uint Type,State,Accepted,Win32Exit,ServiceExit,Checkpoint,WaitHint; }
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool ControlService(IntPtr service,uint control,out NativeStatus status);
}
