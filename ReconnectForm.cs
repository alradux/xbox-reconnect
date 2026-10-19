using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.ServiceProcess;

internal sealed class ReconnectForm : Form {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); int dark=1; DwmSetWindowAttribute(Handle,20,ref dark,4); }
    static readonly Color Green=Color.FromArgb(156,239,112), Muted=Color.FromArgb(166,181,171);
    Button start,stop,adapterSelect,restartAdapter;
    readonly ContextMenuStrip adapterMenu=new ContextMenuStrip();
    Label serviceStatus,serviceDetails,exitHint,controllerTitle,adapterStatus,activityTitle;
    Label[] rows=new Label[4]; TextBox log;
    RedistSnapshot service; string serviceName; bool busy,readingService,readingControllers,preview,needsRestore;
    int epoch,pollCount; Timer timer;
    Adapter selectedAdapter; List<Adapter> adapters=new List<Adapter>();
    bool[] known=new bool[4],connected=new bool[4]; DateTime[] connectedSince=new DateTime[4]; DateTime? lastSeen;
    string latestControllers="Not checked yet",lastServiceState;

    internal ReconnectForm(bool isPreview) {
        preview=isPreview; Text="Xbox Reconnect 2.1"; ClientSize=new Size(940,860);
        StartPosition=FormStartPosition.CenterScreen; MaximizeBox=false; FormBorderStyle=FormBorderStyle.FixedSingle;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor=Color.FromArgb(16,22,19); ForeColor=Color.FromArgb(239,246,241);
        Font=new Font("Segoe UI",10); AutoScaleMode=AutoScaleMode.Dpi;
        // Keep every control reachable on small displays through scrolling.
        var viewport=new Panel { Dock=DockStyle.Fill,AutoScroll=true }; Controls.Add(viewport);
        var root=new TableLayoutPanel { Width=940,Height=860,ColumnCount=1,RowCount=6,Padding=new Padding(24),Dock=DockStyle.Top };
        root.MinimumSize=new Size(900,860); viewport.Controls.Add(root);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,86)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,244));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,164)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,116));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        var header=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
        header.Controls.Add(Label("CONTROLLER CONNECTION HELPER",0,0,850,20,9,Green,true));
        header.Controls.Add(Label("Xbox Reconnect",0,25,850,43,25,ForeColor,true));
        root.Controls.Add(header,0,0);

        var card=new Card { Dock=DockStyle.Fill,Margin=new Padding(0,0,0,12) };
        card.Controls.Add(Label("GAMEINPUT REDIST SERVICE",20,14,560,24,11,Green,true));
        serviceStatus=Label("Checking...",660,14,205,26,11,Muted,true); serviceStatus.TextAlign=ContentAlignment.MiddleRight; card.Controls.Add(serviceStatus);
        serviceDetails=Label("Reading installed version and startup setting...",20,44,840,24,9,Muted,false); card.Controls.Add(serviceDetails);
        start=Button("Start",20,79,180,true); stop=Button("Stop",212,79,180,false);
        card.Controls.Add(start); card.Controls.Add(stop);
        card.Controls.Add(Label("If the controller will not reconnect, click Stop.",20,127,845,24,11,ForeColor,true));
        card.Controls.Add(Label("GameInput Redist can trigger this fault. Stop disables and stops the service so it cannot restart itself.\nWhen finished, click Start to set it back to Automatic and run it again for games that need it.",20,157,845,42,9,Muted,false));
        exitHint=Label("After using Stop, you must click Start before closing this app.",20,204,845,23,9,Green,true); card.Controls.Add(exitHint);
        root.Controls.Add(card,0,1);
        start.Click+=async delegate { await ServiceAction("Start"); };
        stop.Click+=async delegate { await ServiceAction("Stop"); };

        var controllerCard=new Card { Dock=DockStyle.Fill,Margin=new Padding(0,0,0,12) };
        controllerTitle=Label("CONTROLLER STATUS   /   CHECKING...",20,13,850,23,10,Green,true); controllerCard.Controls.Add(controllerTitle);
        for(int i=0;i<4;i++) { rows[i]=Label("Slot "+(i+1)+"  /  Checking...",20,42+i*21,850,22,9,Muted,false); controllerCard.Controls.Add(rows[i]); }
        controllerCard.Controls.Add(Label("Windows XInput status; model / serial unavailable. Virtual controllers can also appear here.",20,128,850,20,9,Muted,false));
        root.Controls.Add(controllerCard,0,2);
        var adapterCard=new Card { Dock=DockStyle.Fill,Margin=new Padding(0,0,0,12) };
        adapterCard.Controls.Add(Label("WIRELESS ADAPTER",20,11,580,21,9,Muted,true));
        adapterSelect=Button("Checking adapter...",20,38,610,false); adapterSelect.TextAlign=ContentAlignment.MiddleLeft;
        restartAdapter=Button("Restart adapter",642,38,223,false); adapterCard.Controls.Add(adapterSelect); adapterCard.Controls.Add(restartAdapter);
        adapterStatus=Label("Only the selected Xbox adapter is affected.",20,78,850,20,9,Muted,false); adapterCard.Controls.Add(adapterStatus);
        root.Controls.Add(adapterCard,0,3);
        adapterSelect.Click+=delegate {
            if(adapters.Count==0) return;
            while(adapterMenu.Items.Count>0) { var old=adapterMenu.Items[0]; adapterMenu.Items.RemoveAt(0); old.Dispose(); }
            foreach(Adapter candidate in adapters) {
                Adapter choice=candidate; var item=new ToolStripMenuItem(choice.ToString());
                item.Click+=delegate { selectedAdapter=choice; adapterSelect.Text=choice.ToString()+"   \u25be"; SetEnabled(); }; adapterMenu.Items.Add(item);
            }
            adapterMenu.Show(adapterSelect,new Point(0,adapterSelect.Height));
        };
        restartAdapter.Click+=async delegate {
            Adapter target=selectedAdapter; if(target==null) return;
            await Run("RESTARTING ADAPTER",delegate { RestartAdapter(target); });
        };
        var activity=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
        activityTitle=Label("ACTIVITY   /   READY",0,0,650,24,9,Muted,true); activity.Controls.Add(activityTitle);
        var save=Button("Save report",747,0,145,false); save.Height=28; activity.Controls.Add(save); save.Click+=delegate { SaveReport(); };
        log=new TextBox { Left=0,Top=36,Width=892,Height=100,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=BackColor,ForeColor=Muted,Font=new Font("Consolas",9) };
        activity.Controls.Add(log); activity.Resize+=delegate { log.Width=activity.ClientSize.Width; log.Height=Math.Max(40,activity.ClientSize.Height-36); };
        root.Controls.Add(activity,0,4);
        root.Controls.Add(Label("Service changes happen only when you click Start or Stop. Live status updates automatically.",0,7,890,21,9,Muted,false),0,5);
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {
            if(!CanExit(busy,needsRestore)) {
                e.Cancel=true;
                if(busy) { Write("Please wait for the current operation to finish."); return; }
                MessageBox.Show(this,"Click Start before closing to re-enable GameInput Redist and make it available to games again.","Restore GameInput before closing",MessageBoxButtons.OK,MessageBoxIcon.Information);
                start.Focus();
            }
        };
        if(preview) {
            serviceName="GameInputRedistService"; service=new RedistSnapshot { Name=serviceName,Version="4.0.300.0",Startup=4,Status=ServiceControllerStatus.Stopped };
            DisplayService();
            selectedAdapter=new Adapter { Name="Xbox Wireless Adapter for Windows #2",Id=@"USB\VID_045E&PID_02FE\PREVIEW" }; adapters.Add(selectedAdapter); adapterSelect.Text=selectedAdapter.ToString()+"   \u25be";
            controllerTitle.Text="CONTROLLER STATUS   /   DISCONNECTED";
            for(int i=0;i<4;i++) rows[i].Text="Slot "+(i+1)+"  /  Disconnected";
            Write("GameInput Redist Service: Stopped / Disabled."); Write("If the reconnect issue happens, click Stop. Click Start before closing.");
            SetEnabled();
        } else {
            SetEnabled(); timer=new Timer { Interval=500 };
            timer.Tick+=async delegate {
                await PollControllers(); pollCount++;
                if(pollCount%4==0 && !busy) await RefreshService();
            };
            Shown+=async delegate {
                timer.Start(); await RefreshService(); await RefreshAdapters();
                int maxHeight=Screen.FromControl(this).WorkingArea.Height-40; if(Height>maxHeight) Height=maxHeight;
            };
            FormClosed+=delegate { timer.Stop(); timer.Dispose(); };
        }
    }
    protected override void Dispose(bool disposing) {
        // Dropdown close is part of click dispatch. Keep menus alive until the form is disposed.
        if(disposing) adapterMenu.Dispose();
        base.Dispose(disposing);
    }
    internal bool TestDropdowns() {
        if(!preview) throw new InvalidOperationException("UI tests require preview mode.");
        if(!start.Enabled || stop.Enabled) return false;
        needsRestore=false; service=new RedistSnapshot { Name=serviceName,Startup=2,Status=ServiceControllerStatus.Running,CanStop=true };
        DisplayService(); if(start.Enabled || !stop.Enabled) return false;
        needsRestore=true; service.Startup=4; service.Status=ServiceControllerStatus.Stopped; DisplayService();
        if(!start.Enabled || stop.Enabled || CanExit(false,needsRestore)) return false;
        needsRestore=false; service.Startup=2; service.Status=ServiceControllerStatus.Running; DisplayService();
        if(start.Enabled || !stop.Enabled || !CanExit(false,needsRestore)) return false;
        for(int i=0;i<3;i++) {
            adapterSelect.PerformClick(); var item=(ToolStripMenuItem)adapterMenu.Items[0];
            adapterMenu.Close(ToolStripDropDownCloseReason.ItemClicked); item.PerformClick(); Application.DoEvents();
            if(adapterMenu.IsDisposed || selectedAdapter==null) return false;
        }
        return !CanExit(true,false) && !CanExit(true,true) && !CanExit(false,true) && CanExit(false,false);
    }
    internal static bool CanExit(bool operationRunning,bool restoreRequired) { return !operationRunning && !restoreRequired; }
    Label Label(string text,int x,int y,int w,int h,float size,Color color,bool bold) { return new Label { Text=text,Left=x,Top=y,Width=w,Height=h,ForeColor=color,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular) }; }
    Button Button(string text,int x,int y,int w,bool primary) {
        var result=new Button { Text=text,Left=x,Top=y,Width=w,Height=34,FlatStyle=FlatStyle.Flat,BackColor=primary?Green:Color.FromArgb(39,51,43),ForeColor=primary?Color.FromArgb(18,35,18):ForeColor,Font=new Font("Segoe UI",10,FontStyle.Bold),Cursor=Cursors.Hand,UseVisualStyleBackColor=false };
        result.FlatAppearance.BorderSize=0; return result;
    }
    void Write(string text) { if(IsDisposed) return; if(InvokeRequired) { Invoke(new Action<string>(Write),text); return; } log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine+Environment.NewLine); }
    void SetEnabled() {
        bool ready=!busy && service!=null;
        bool stable=ready && (service.Status==ServiceControllerStatus.Stopped || service.Status==ServiceControllerStatus.Running);
        start.Enabled=stable && (needsRestore || service.Status!=ServiceControllerStatus.Running || service.Startup!=2 || service.Delayed);
        stop.Enabled=stable && (service.Status==ServiceControllerStatus.Stopped || service.CanStop) && (service.Startup!=4 || service.Status!=ServiceControllerStatus.Stopped);
        exitHint.Text=needsRestore?"Click Start before closing to restore GameInput for other games.":"After using Stop, you must click Start before closing this app.";
        adapterSelect.Enabled=!busy && adapters.Count>0; restartAdapter.Enabled=!busy && selectedAdapter!=null;
    }
    void DisplayService() {
        if(service==null) return;
        if(service.Startup==4 || service.Status==ServiceControllerStatus.Stopped) needsRestore=true;
        serviceStatus.Text=service.Status.ToString(); serviceStatus.ForeColor=service.Status==ServiceControllerStatus.Running?Green:Muted;
        serviceDetails.Text="Version "+service.Version+"   /   Startup: "+service.Mode;
        string state=service.Name+": "+service.Status+" / "+service.Mode;
        if(state!=lastServiceState) { Write(state); lastServiceState=state; }
        SetEnabled();
    }
    async Task RefreshService() {
        if(readingService || busy || IsDisposed) return;
        readingService=true; int token=epoch; SetEnabled();
        try {
            string target=serviceName;
            RedistSnapshot current=await Task.Run(delegate {
                if(target==null) { var names=ServiceSettings.Names(); if(names.Count==0) return null; target=names.Contains("GameInputRedistService")?"GameInputRedistService":names[0]; }
                return ServiceSettings.Read(target);
            });
            if(IsDisposed || token!=epoch) return;
            service=current; serviceName=current==null?null:current.Name;
            if(current==null) { serviceStatus.Text="Not installed"; serviceDetails.Text="No GameInput Redist Service was found."; }
            else DisplayService();
        } catch(Exception ex) {
            if(!IsDisposed && token==epoch) { service=null; serviceStatus.Text="Unavailable"; serviceDetails.Text=ex.Message; }
        } finally { readingService=false; if(!IsDisposed) SetEnabled(); }
    }
    async Task<bool> Run(string title,Action action) {
        if(busy) return false; busy=true; epoch++; SetEnabled(); activityTitle.Text="ACTIVITY   /   "+title; UseWaitCursor=true;
        bool success=false;
        try { await Task.Run(action); success=true; activityTitle.Text="ACTIVITY   /   COMPLETE"; }
        catch(Exception ex) { Write("Could not complete: "+ex.Message); activityTitle.Text="ACTIVITY   /   NEEDS ATTENTION"; }
        try {
            if(serviceName!=null) {
                try { string target=serviceName; service=await Task.Run(delegate { return ServiceSettings.Read(target); }); DisplayService(); }
                catch(Exception ex) { service=null; serviceStatus.Text="Unavailable"; Write("Could not refresh service: "+ex.Message); }
            }
        } finally { busy=false; UseWaitCursor=false; SetEnabled(); }
        return success;
    }
    async Task ServiceAction(string action) {
        string target=serviceName; if(target==null) return;
        if(action=="Stop") needsRestore=true;
        bool success=await Run(action.ToUpperInvariant()+" SERVICE",delegate {
            ServiceSettings.Apply(target,action=="Stop"?3:0);
            ServiceSettings.Control(target,action);
            RedistSnapshot verified=ServiceSettings.Read(target);
            if(!VerifiedAction(action,verified)) throw new InvalidOperationException("Windows did not keep the requested service state. Try again and check the activity log.");
            Write(action=="Stop"?"GameInput Redist is disabled and stopped. Reconnect the controller; click Start before closing.":"GameInput Redist is Automatic and running again. You can now close the app.");
        });
        if(action=="Start" && success && VerifiedAction(action,service)) needsRestore=false;
        SetEnabled();
    }
    internal static bool VerifiedAction(string action,RedistSnapshot snapshot) {
        if(snapshot==null) return false;
        return action=="Stop"?snapshot.Startup==4 && snapshot.Status==ServiceControllerStatus.Stopped:
            action=="Start" && snapshot.Startup==2 && !snapshot.Delayed && snapshot.Status==ServiceControllerStatus.Running;
    }
    async Task RefreshAdapters() {
        try {
            var found=await Task.Run(delegate { return Devices.Read(); }); if(IsDisposed) return;
            adapters=found; selectedAdapter=found.Count>0?found[0]:null;
            adapterSelect.Text=selectedAdapter==null?"No supported Xbox adapter detected":selectedAdapter.ToString()+"   \u25be";
            adapterStatus.Text=found.Count+" supported adapter(s). Restarting briefly disconnects controllers and headsets on that adapter.";
        } catch(Exception ex) { if(!IsDisposed) { adapterStatus.Text="Adapter detection unavailable: "+ex.Message; Write(adapterStatus.Text); } }
        if(!IsDisposed) SetEnabled();
    }
    async Task PollControllers() {
        if(readingControllers || IsDisposed) return; readingControllers=true;
        try {
            var snapshots=await Task.Run(delegate { var result=new List<ControllerSnapshot>(); for(uint slot=0;slot<4;slot++) result.Add(Controllers.Read(slot)); return result; });
            if(IsDisposed) return;
            int count=0; DateTime now=DateTime.Now; var details=new StringBuilder();
            foreach(var snapshot in snapshots) {
                int i=(int)snapshot.Slot; if(snapshot.Connected) { count++; lastSeen=now; }
                rows[i].Text=snapshot.Describe(); rows[i].ForeColor=snapshot.Connected?Green:Muted; details.AppendLine(snapshot.Describe());
                if(!known[i] || connected[i]!=snapshot.Connected) {
                    if(snapshot.Connected) { connectedSince[i]=now; Write("Controller slot "+(i+1)+" connected. "+snapshot.Link+"; battery: "+snapshot.Battery+"."); }
                    else if(known[i]) Write("Controller slot "+(i+1)+" disconnected after "+(now-connectedSince[i]).TotalSeconds.ToString("0.0")+" seconds.");
                    known[i]=true; connected[i]=snapshot.Connected;
                }
            }
            latestControllers=details.ToString(); controllerTitle.Text=count>0?"CONTROLLER STATUS   /   "+count+" CONNECTED":"CONTROLLER STATUS   /   DISCONNECTED"+(lastSeen.HasValue?"   /   LAST SEEN "+lastSeen.Value.ToString("HH:mm:ss"):"");
        } catch(Exception ex) { if(!IsDisposed) { controllerTitle.Text="CONTROLLER STATUS   /   UNAVAILABLE"; for(int i=0;i<4;i++) rows[i].Text="Slot "+(i+1)+"  /  Unavailable"; latestControllers=ex.Message; } }
        finally { readingControllers=false; }
    }
    void RestartAdapter(Adapter target) {
        bool present=false; foreach(var adapter in Devices.Read()) if(adapter.Id==target.Id) present=true;
        if(!present) throw new InvalidOperationException("The selected Xbox adapter is no longer connected.");
        var startInfo=new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\pnputil.exe"),"/restart-device \""+target.Id+"\"") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        using(var process=System.Diagnostics.Process.Start(startInfo)) {
            var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync(); process.WaitForExit();
            Write(output.GetAwaiter().GetResult().Trim()); string errorText=error.GetAwaiter().GetResult().Trim(); if(errorText.Length>0) Write(errorText);
            if(process.ExitCode==3010) { Write("Windows requires a PC restart to complete the device restart. This app will not reboot it."); return; }
            if(process.ExitCode!=0) throw new InvalidOperationException("Windows returned code "+process.ExitCode+". Try unplugging and reconnecting the dongle.");
        }
    }
    async void SaveReport() {
        using(var dialog=new SaveFileDialog { Filter="Text report (*.txt)|*.txt",FileName="Xbox-Reconnect-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt" }) {
            if(dialog.ShowDialog(this)!=DialogResult.OK) return;
            try {
                string target=serviceName;
                string details=await Task.Run(delegate { if(target==null) return "No service detected"; var current=ServiceSettings.Read(target); return current.Name+"\r\nStatus: "+current.Status+"\r\nStartup: "+current.Mode+"\r\nVersion: "+current.Version+"\r\nPath: "+current.Path; });
                if(IsDisposed) return;
                System.IO.File.WriteAllText(dialog.FileName,"Xbox Reconnect 2.1\r\n"+DateTime.Now.ToString("O")+"\r\nWindows: "+Environment.OSVersion+"\r\nRestore required before exit: "+needsRestore+"\r\nAdapter: "+(selectedAdapter==null?"None":selectedAdapter.Name+"\r\n"+selectedAdapter.Id)+"\r\n\r\nGameInput Redist Service:\r\n"+details+"\r\n\r\n"+latestControllers+"\r\nActivity:\r\n"+log.Text); Write("Report saved.");
            } catch(Exception ex) { if(!IsDisposed) Write("Could not save report: "+ex.Message); }
        }
    }
}
