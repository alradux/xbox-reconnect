using System;
internal static class SettingsTests {
    internal static bool Run() {
        var stopped=new RedistSnapshot { Startup=4,Status=System.ServiceProcess.ServiceControllerStatus.Stopped };
        var running=new RedistSnapshot { Startup=2,Status=System.ServiceProcess.ServiceControllerStatus.Running };
        if(!ReconnectForm.VerifiedAction("Stop",stopped) || !ReconnectForm.VerifiedAction("Start",running)) return false;
        if(ReconnectForm.VerifiedAction("Start",stopped) || ReconnectForm.VerifiedAction("Stop",running) || ReconnectForm.VerifiedAction("Start",null)) return false;
        running.Startup=4; if(ReconnectForm.VerifiedAction("Start",running)) return false;
        running.Startup=2; running.Delayed=true; if(ReconnectForm.VerifiedAction("Start",running)) return false;
        if(ReconnectForm.CanExit(false,true) || ReconnectForm.CanExit(true,false) || !ReconnectForm.CanExit(false,false)) return false;
        for(int i=0;i<4;i++) if(ServiceSettings.ModeIndex(ServiceSettings.ModeStart(i),i==1)!=i) return false;
        if(ServiceSettings.IsRedist("GameInputSvc","GameInput Service") || ServiceSettings.IsRedist("GamingServices","Gaming Services")) return false;
        if(!ServiceSettings.IsRedist("GameInputRedistService","GameInput Redist Service")) return false;
        bool rejected=false; try { ServiceSettings.ModeStart(-1); } catch(ArgumentOutOfRangeException) { rejected=true; } if(!rejected) return false;
        rejected=false; try { ServiceSettings.ModeStart(4); } catch(ArgumentOutOfRangeException) { rejected=true; } if(!rejected) return false;
        uint actual=2; bool delayed=true; int calls=0; bool sawFailure=false;
        try {
            ServiceSettings.ApplyWithRollback(delegate(uint mode,bool delay) {
                actual=mode; delayed=delay; calls++;
                if(calls==1) throw new Exception("Simulated partial write failure");
            },2,true,4,false);
        } catch(InvalidOperationException) { sawFailure=true; }
        if(!sawFailure || calls!=2 || actual!=2 || !delayed) return false;
        string failure="";
        try { ServiceSettings.ApplyWithRollback(delegate(uint mode,bool delay) { throw new Exception("Access denied"); },3,false,2,true); }
        catch(InvalidOperationException ex) { failure=ex.Message; }
        if(!failure.Contains("Restoring the previous setting also failed")) return false;
        calls=0;
        ServiceSettings.ApplyWithRollback(delegate(uint mode,bool delay) { actual=mode; delayed=delay; calls++; },4,false,2,true);
        return calls==1 && actual==2 && delayed;
    }
}
