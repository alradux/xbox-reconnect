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

    float layoutScale=1f,fontMultiplier=1f;
    TableLayoutPanel content;
    readonly List<Label> wrappingLabels=new List<Label>();
    internal ReconnectForm(bool isPreview,float mockDpiScale=0f) {
        SuspendLayout(); preview=isPreview; Text="Xbox Reconnect 2.2";
        AutoScaleMode=AutoScaleMode.None;
        StartPosition=FormStartPosition.CenterScreen; MaximizeBox=true; FormBorderStyle=FormBorderStyle.Sizable;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BackColor=Color.FromArgb(16,22,19); ForeColor=Color.FromArgb(239,246,241);
        float nativeScale; using(var graphics=CreateGraphics()) nativeScale=graphics.DpiX/96f;
        layoutScale=mockDpiScale>0?mockDpiScale:nativeScale;
        fontMultiplier=mockDpiScale>0?mockDpiScale/nativeScale:1f;
        Font=new Font("Segoe UI",10*fontMultiplier);
        ClientSize=new Size(Px(940),Px(860));
        var viewport=new Panel { Dock=DockStyle.Fill,AutoScroll=true }; Controls.Add(viewport);
        content=Stack(); content.Dock=DockStyle.None; content.Anchor=AnchorStyles.Top|AnchorStyles.Left; content.Padding=new Padding(Px(24));
        content.MinimumSize=new Size(Px(700),0); viewport.Controls.Add(content);
        var header=Stack(); header.Margin=new Padding(0,0,0,Px(16));
        header.Controls.Add(AutoLabel("CONTROLLER CONNECTION HELPER",9,Green,true));
        header.Controls.Add(AutoLabel("Xbox Reconnect",25,ForeColor,true)); content.Controls.Add(header);

        var serviceBody=Stack(); var serviceCard=AutoCard(serviceBody); content.Controls.Add(serviceCard);
        var serviceHeading=new TableLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=2,Margin=Padding.Empty };
        serviceHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,70)); serviceHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,30));
        serviceHeading.Controls.Add(AutoLabel("GAMEINPUT REDIST SERVICE",11,Green,true),0,0);
        serviceStatus=AutoLabel("Checking...",11,Muted,true); serviceStatus.Anchor=AnchorStyles.Right; serviceHeading.Controls.Add(serviceStatus,1,0); serviceBody.Controls.Add(serviceHeading);
        serviceDetails=AutoLabel("Reading installed version and startup setting...",9,Muted,false); serviceBody.Controls.Add(serviceDetails);
        var serviceButtons=ButtonRow(); start=ActionButton("Start",180,true); stop=ActionButton("Stop",180,false);
        serviceButtons.Controls.Add(start); serviceButtons.Controls.Add(stop); serviceBody.Controls.Add(serviceButtons);
        serviceBody.Controls.Add(AutoLabel("If the controller will not reconnect, click Stop.",11,ForeColor,true));
        serviceBody.Controls.Add(AutoLabel("GameInput Redist can trigger this fault. Stop disables and stops the service so it cannot restart itself.\nWhen finished, click Start to set it back to Automatic and run it again for games that need it.",9,Muted,false));
        exitHint=AutoLabel("After using Stop, you must click Start before closing this app.",9,Green,true); serviceBody.Controls.Add(exitHint);
        start.Click+=async delegate { await ServiceAction("Start"); }; stop.Click+=async delegate { await ServiceAction("Stop"); };

        var controllerBody=Stack(); content.Controls.Add(AutoCard(controllerBody));
        controllerTitle=AutoLabel("CONTROLLER STATUS   /   CHECKING...",10,Green,true); controllerBody.Controls.Add(controllerTitle);
        for(int i=0;i<4;i++) { rows[i]=AutoLabel("Slot "+(i+1)+"  /  Checking...",9,Muted,false); controllerBody.Controls.Add(rows[i]); }
        controllerBody.Controls.Add(AutoLabel("Windows XInput status; model / serial unavailable. Virtual controllers can also appear here.",9,Muted,false));

        var adapterBody=Stack(); content.Controls.Add(AutoCard(adapterBody));
        adapterBody.Controls.Add(AutoLabel("WIRELESS ADAPTER",9,Muted,true));
        var adapterButtons=ButtonRow(); adapterSelect=ActionButton("Checking adapter...",470,false); adapterSelect.TextAlign=ContentAlignment.MiddleLeft;
        restartAdapter=ActionButton("Restart adapter",190,false); adapterButtons.Controls.Add(adapterSelect); adapterButtons.Controls.Add(restartAdapter); adapterBody.Controls.Add(adapterButtons);
        adapterStatus=AutoLabel("Only the selected Xbox adapter is affected.",9,Muted,false); adapterBody.Controls.Add(adapterStatus);
        adapterSelect.Click+=delegate {
            if(adapters.Count==0) return;
            while(adapterMenu.Items.Count>0) { var old=adapterMenu.Items[0]; adapterMenu.Items.RemoveAt(0); old.Dispose(); }
            foreach(Adapter candidate in adapters) {
                Adapter choice=candidate; var item=new ToolStripMenuItem(choice.ToString());
                item.Click+=delegate { selectedAdapter=choice; adapterSelect.Text=choice.ToString()+"   \u25be"; SetEnabled(); }; adapterMenu.Items.Add(item);
            }
            adapterMenu.Show(adapterSelect,new Point(0,adapterSelect.Height));
        };
        restartAdapter.Click+=async delegate { Adapter target=selectedAdapter; if(target!=null) await Run("RESTARTING ADAPTER",delegate { RestartAdapter(target); }); };

        var activity=Stack(); content.Controls.Add(activity);
        var activityHeading=ButtonRow(); activityTitle=AutoLabel("ACTIVITY   /   READY",9,Muted,true); activityHeading.Controls.Add(activityTitle);
        var save=ActionButton("Save report",145,false); activityHeading.Controls.Add(save); save.Click+=delegate { SaveReport(); }; activity.Controls.Add(activityHeading);
        log=new TextBox { Dock=DockStyle.Top,Height=Px(150),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=BackColor,ForeColor=Muted,Font=new Font("Consolas",9*fontMultiplier),Margin=new Padding(0,Px(8),0,Px(12)) };
        activity.Controls.Add(log);
        content.Controls.Add(AutoLabel("Service changes happen only when you click Start or Stop. Live status updates automatically.",9,Muted,false));
        viewport.SizeChanged+=delegate { UpdateWrapping(); };
        content.SizeChanged+=delegate { UpdateWrapping(); };
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {
            if(!CanExit(busy,needsRestore)) {
                e.Cancel=true;
                if(busy) { Write("Please wait for the current operation to finish."); return; }
                MessageBox.Show(this,"Click Start before closing to re-enable GameInput Redist and make it available to games again.","Restore GameInput before closing",MessageBoxButtons.OK,MessageBoxIcon.Information); start.Focus();
            }
        };
        if(preview) {
            serviceName="GameInputRedistService"; service=new RedistSnapshot { Name=serviceName,Version="4.0.300.0",Startup=4,Status=ServiceControllerStatus.Stopped }; DisplayService();
            selectedAdapter=new Adapter { Name="Xbox Wireless Adapter for Windows #2",Id=@"USB\VID_045E&PID_02FE\PREVIEW" }; adapters.Add(selectedAdapter); adapterSelect.Text=selectedAdapter.ToString()+"   \u25be";
            controllerTitle.Text="CONTROLLER STATUS   /   DISCONNECTED"; for(int i=0;i<4;i++) rows[i].Text="Slot "+(i+1)+"  /  Disconnected";
            Write("If the reconnect issue happens, click Stop. Click Start before closing."); SetEnabled();
        } else {
            SetEnabled(); timer=new Timer { Interval=500 };
            timer.Tick+=async delegate { await PollControllers(); pollCount++; if(pollCount%4==0 && !busy) await RefreshService(); };
            Shown+=async delegate { FitToWorkingArea(); UpdateWrapping(); timer.Start(); await RefreshService(); await RefreshAdapters(); };
            FormClosed+=delegate { timer.Stop(); timer.Dispose(); };
        }
        ResumeLayout(true); UpdateWrapping();
    }
    int Px(int value) { return (int)Math.Ceiling(value*layoutScale); }
    TableLayoutPanel Stack() {
        var panel=new TableLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Dock=DockStyle.Top,Margin=Padding.Empty,Padding=Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); return panel;
    }
    Card AutoCard(TableLayoutPanel body) {
        var card=new Card { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,Padding=new Padding(Px(20)),Margin=new Padding(0,0,0,Px(12)) };
        card.Controls.Add(body); return card;
    }
    FlowLayoutPanel ButtonRow() {
        return new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,WrapContents=true,Margin=new Padding(0,Px(5),0,Px(8)),Padding=Padding.Empty };
    }
    Label AutoLabel(string text,float size,Color color,bool bold) {
        var label=new Label { AutoSize=true,Text=text,ForeColor=color,Font=new Font("Segoe UI",size*fontMultiplier,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(0,0,0,Px(7)),UseMnemonic=false };
        wrappingLabels.Add(label); return label;
    }
    Button ActionButton(string text,int minWidth,bool primary) {
        var button=Button(text,0,0,Px(minWidth),primary);
        button.AutoSize=true; button.AutoSizeMode=AutoSizeMode.GrowAndShrink;
        button.MinimumSize=new Size(Px(minWidth),Px(36)); button.Padding=new Padding(Px(12),Px(5),Px(12),Px(5));
        button.Margin=new Padding(0,0,Px(12),0); return button;
    }
    bool updatingWrap;
    void UpdateWrapping() {
        if(updatingWrap || content==null) return; updatingWrap=true;
        try {
            // A docked child can overhang its AutoScroll parent without creating
            // a horizontal scrollbar. Give the content an explicit scrollable width.
            int contentWidth=Math.Max(Px(700),content.Parent.ClientSize.Width);
            content.MaximumSize=new Size(contentWidth,0); content.MinimumSize=new Size(contentWidth,0); content.Width=contentWidth;
            foreach(Label label in wrappingLabels) {
                if(label.Parent==null) continue;
                int width=label.Parent.ClientSize.Width-label.Parent.Padding.Horizontal-label.Margin.Horizontal;
                if(label.Parent is TableLayoutPanel && ((TableLayoutPanel)label.Parent).ColumnCount==2) width=(int)(width*(label==serviceStatus ? 0.30 : 0.70));
                var limit=new Size(Math.Max(Px(120),width),0);
                if(label.MaximumSize!=limit) label.MaximumSize=limit;
            }
        } finally { updatingWrap=false; }
    }

    void FitToWorkingArea() {
        Rectangle area=Screen.FromControl(this).WorkingArea;
        Size fitted=new Size(Math.Min(Width,Math.Max(320,area.Width-32)),Math.Min(Height,Math.Max(240,area.Height-32)));
        Size=fitted;
        Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
    }
    static void CollectControls(Control parent,List<Control> result) {
        foreach(Control child in parent.Controls) { result.Add(child); CollectControls(child,result); }
    }
    internal string CheckTextLayout() {
        var controls=new List<Control>(); CollectControls(this,controls);
        foreach(Control control in controls) {
            var label=control as Label; if(label==null || label.Text.Length==0) continue;
            Size available=new Size(Math.Max(1,label.ClientSize.Width-label.Padding.Horizontal),int.MaxValue);
            Size measured=TextRenderer.MeasureText(label.Text,label.Font,available,TextFormatFlags.WordBreak);
            if(measured.Height>label.ClientSize.Height-label.Padding.Vertical+1)
                return "Text clipped: "+label.Text+"; needs "+measured+"; has "+label.ClientSize;
            if(label.Left<0 || label.Top<0 || label.Right>label.Parent.ClientSize.Width+1 || label.Bottom>label.Parent.ClientSize.Height+1)
                return "Label outside parent: "+label.Text+"; label "+label.Bounds+"; parent "+label.Parent.ClientSize;
        }
        if(log.Bottom>log.Parent.ClientSize.Height+1) return "Activity log extends below its panel.";
        return null;
    }
    internal void SetDpiTestContent() {
        if(!preview) throw new InvalidOperationException("Layout samples require mock service state.");
        service=new RedistSnapshot { Name=serviceName,Version="4.0.300.0",Startup=2,Delayed=true,Status=ServiceControllerStatus.Running,CanStop=true };
        DisplayService(); controllerTitle.Text="CONTROLLER STATUS   /   DISCONNECTED   /   LAST SEEN 23:59:59";
        for(int i=0;i<4;i++) rows[i].Text="Slot "+(i+1)+"  /  CONNECTED  /  Wireless  /  Battery: Full  /  Buttons: 0xFFFF";
        adapterStatus.Text="1 supported adapter(s). Restarting briefly disconnects controllers and headsets on that adapter.";
        UpdateWrapping(); PerformLayout(); Application.DoEvents();
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
        var result=new Button { Text=text,Left=x,Top=y,Width=w,Height=Px(34),FlatStyle=FlatStyle.Flat,BackColor=primary?Green:Color.FromArgb(39,51,43),ForeColor=primary?Color.FromArgb(18,35,18):ForeColor,Font=new Font("Segoe UI",10*fontMultiplier,FontStyle.Bold),Cursor=Cursors.Hand,UseVisualStyleBackColor=false };
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
                System.IO.File.WriteAllText(dialog.FileName,"Xbox Reconnect 2.2\r\n"+DateTime.Now.ToString("O")+"\r\nWindows: "+Environment.OSVersion+"\r\nRestore required before exit: "+needsRestore+"\r\nAdapter: "+(selectedAdapter==null?"None":selectedAdapter.Name+"\r\n"+selectedAdapter.Id)+"\r\n\r\nGameInput Redist Service:\r\n"+details+"\r\n\r\n"+latestControllers+"\r\nActivity:\r\n"+log.Text); Write("Report saved.");
            } catch(Exception ex) { if(!IsDisposed) Write("Could not save report: "+ex.Message); }
        }
    }
}
