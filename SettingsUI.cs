using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PersonalShop {
    partial class ShopWindow {
        void SavePreferences() { try { preferences.Save(); } catch { MessageBox.Show(this,"Windows couldn't save your settings. Your choices will apply for this session.","Settings",MessageBoxButtons.OK,MessageBoxIcon.Warning); } }
        async Task CheckUpdate() {
            if(updateBusy || closing) return; updateBusy=true; available=null; updateStatus="Checking for updates...";
            try { available=await Task.Run(()=>Updates.Fetch()); updateStatus=available==null?"You're up to date":"Version "+available.Version.ToString(3)+" is available"; }
            catch(Exception ex) { updateStatus=ex is FriendlyException?ex.Message:"Couldn't check for updates. Try again later."; }
            finally { updateBusy=false; }
            RefreshSettingsState(); autoPending=available!=null && preferences.AutomaticUpdates && preferences.AutomaticInstall;
            if(!IsDisposed && !closing && autoPending && !busy && !settingsOpen) { autoPending=false; await InstallUpdate(true); }
        }
        async Task InstallUpdate(bool automatic) {
            if(available==null || updateBusy || busy || closing) return; updateBusy=true; string file=null;
            try {
                updateStatus="Downloading verified installer..."; var release=available; file=await Task.Run(()=>Updates.Download(release));
                if(IsDisposed || closing || automatic && (!preferences.AutomaticUpdates || !preferences.AutomaticInstall)) { Directory.Delete(Path.GetDirectoryName(file),true); return; }
                Updates.Launch(file); updateStatus="Installing update..."; Close();
            } catch(Exception ex) { updateStatus=ex is FriendlyException?ex.Message:"Couldn't start the installer. Try again later."; if(file!=null) try { Directory.Delete(Path.GetDirectoryName(file),true); } catch {} }
            finally { updateBusy=false; RefreshSettingsState(); }
        }
        SettingsPage settingsPage; VaultButton shopNavigation;
        public void OpenSettings(string capturePath=null) {
            settingsOpen=true;
            if(settingsPage==null) { settingsPage=new SettingsPage(this); Controls.Add(settingsPage); }
            settingsPage.Visible=true; settings.Selected=true; shopNavigation.Selected=false;
            foreach(Control control in new Control[]{content,refresh,demo,regions,dailyTab,nightTab,status}) control.Visible=false;
            Arrange(); RefreshSettingsState(); settingsPage.BringToFront(); Invalidate();
            if(capturePath!=null) { Application.DoEvents(); using(var image=new Bitmap(Width,Height)) { DrawToBitmap(image,new Rectangle(0,0,Width,Height)); image.Save(capturePath); } }
        }
        public void CloseSettings() {
            settingsOpen=false; if(settingsPage!=null) settingsPage.Visible=false;
            settings.Selected=false; shopNavigation.Selected=true;
            foreach(Control control in new Control[]{content,refresh,demo,regions,dailyTab,nightTab,status}) control.Visible=true;
            autoPending=available!=null && preferences.AutomaticUpdates && preferences.AutomaticInstall;
            Arrange(); Invalidate();
        }
        void RefreshSettingsState() { if(settingsPage!=null && !settingsPage.IsDisposed) settingsPage.RefreshState(); }
        public void VerifySettingsNavigation() {
            ShowDemo(); SwitchTab(1); var original=data; OpenSettings(); var first=settingsPage;
            if(OwnedForms.Length!=0 || content.Visible || !settingsPage.Visible) throw new Exception("Settings must replace the shop without a popup");
            settingsPage.VerifySwitches(); CloseSettings();
            if(!content.Visible || settingsPage.Visible || data!=original || selectedTab!=1 || content.Controls.Count!=6) throw new Exception("Returning to shop lost offers or selection");
            OpenSettings(); if(settingsPage!=first) throw new Exception("Settings page was not reused"); CloseSettings();
        }
        class SettingsPage : PaintedPanel {
            readonly ShopWindow owner; readonly Switch refreshSwitch,checkSwitch,installSwitch;
            readonly VaultButton check,install,uninstall; readonly Label message;
            Rectangle routine;
            public SettingsPage(ShopWindow window) {
                owner=window;
                refreshSwitch=Toggle("Refresh shop on launch",owner.preferences.RefreshOnLaunch,value=>owner.preferences.RefreshOnLaunch=value);
                checkSwitch=Toggle("Check for updates automatically",owner.preferences.AutomaticUpdates,value=> { owner.preferences.AutomaticUpdates=value; installSwitch.Enabled=value; });
                installSwitch=Toggle("Install updates automatically",owner.preferences.AutomaticInstall,value=>owner.preferences.AutomaticInstall=value);
                installSwitch.Enabled=checkSwitch.Checked;
                check=new VaultButton {Text="Check for updates",Accent=true,BackColor=Style.Bg}; Controls.Add(check); check.Click+=async delegate { await owner.CheckUpdate(); };
                install=new VaultButton {Text="Install update",BackColor=Style.Bg}; Controls.Add(install); install.Click+=async delegate { await owner.InstallUpdate(false); };
                uninstall=new VaultButton {Text="Uninstall app",BackColor=Style.Bg}; Controls.Add(uninstall);
                string path=Path.Combine(Application.StartupPath,"unins000.exe"); uninstall.Enabled=File.Exists(path);
                uninstall.Click+=delegate { if(owner.busy || owner.updateBusy) return; try { Process.Start(new ProcessStartInfo(path) {UseShellExecute=true}); owner.Close(); } catch { owner.updateStatus="Couldn't open the uninstaller."; RefreshState(); } };
                message=new Label {BackColor=Style.Bg,ForeColor=Style.Muted,Font=new Font("Segoe UI",10),AutoEllipsis=true}; Controls.Add(message);
                Resize+=delegate { LayoutPage(); }; LayoutPage(); RefreshState();
            }
            Switch Toggle(string name,bool value,Action<bool> apply) {
                var control=new Switch {Checked=value,AccessibleName=name,AccessibleRole=AccessibleRole.CheckButton,TabStop=true};
                control.CheckedChanged+=delegate { apply(control.Checked); owner.SavePreferences(); Invalidate(); }; Controls.Add(control); return control;
            }
            void LayoutPage() {
                routine=new Rectangle(20,90,Math.Min(760,Width-40),270);
                refreshSwitch.Bounds=new Rectangle(routine.Right-84,118,58,30); checkSwitch.Bounds=new Rectangle(routine.Right-84,200,58,30); installSwitch.Bounds=new Rectangle(routine.Right-84,282,58,30);
                check.Bounds=new Rectangle(20,384,220,40);
                install.Bounds=new Rectangle(256,384,220,40);
                uninstall.Bounds=new Rectangle(20,440,220,36);
                message.Bounds=new Rectangle(256,440,routine.Width-236,40); Invalidate();
            }
            public void RefreshState() {
                message.Text=owner.updateStatus; message.ForeColor=owner.available!=null?Style.Orange:owner.updateStatus=="You're up to date"?Style.Mint:Style.Muted;
                check.Enabled=!owner.updateBusy; check.Text=owner.updateBusy?"Working...":"Check for updates";
                install.Visible=owner.available!=null; install.Enabled=owner.available!=null && !owner.updateBusy && !owner.busy;
                uninstall.Enabled=File.Exists(Path.Combine(Application.StartupPath,"unins000.exe")) && !owner.busy && !owner.updateBusy;
                Invalidate();
            }
            public void VerifySwitches() {
                checkSwitch.Checked=false; if(installSwitch.Enabled || Preferences.Load().AutomaticUpdates) throw new Exception("Automatic check switch failed");
                checkSwitch.Checked=true; if(!installSwitch.Enabled || !Preferences.Load().AutomaticUpdates) throw new Exception("Dependent install switch failed");
                refreshSwitch.Checked=true; if(!Preferences.Load().RefreshOnLaunch) throw new Exception("Launch switch was not saved");
                installSwitch.Checked=true; if(!Preferences.Load().AutomaticInstall) throw new Exception("Install switch was not saved");
            }
            protected override void OnPaint(PaintEventArgs e) {
                base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
                Style.TextAt(g,"App settings",28,Style.Text,new Rectangle(18,7,Width-40,58),true);
                Style.Box(g,routine,Style.Panel,Style.Line,14);
                Row(g,routine,114,"Refresh on launch","Your latest offers, as soon as you open Nightshift.");
                Row(g,routine,196,"Automatic update checks","Check at startup and every six hours.");
                Row(g,routine,278,"Automatic installation",checkSwitch.Checked?"Install new versions when the shop is idle.":"Turn on automatic checks to enable this.");
                using(var pen=new Pen(Style.Line)) { g.DrawLine(pen,routine.X+24,180,routine.Right-24,180); g.DrawLine(pen,routine.X+24,262,routine.Right-24,262); }
                string version="VERSION "+Updates.VersionText; int versionWidth;
                using(var font=new Font("Segoe UI",8,FontStyle.Bold)) versionWidth=TextRenderer.MeasureText(g,version,font,new Size(Int32.MaxValue,23),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
                Style.Box(g,new RectangleF(20,500,versionWidth+20,23),Color.FromArgb(32,43,30),Color.Transparent,11);
                Style.TextAt(g,version,8,Style.Mint,new Rectangle(30,500,versionWidth,23),true);
            }
            void Row(Graphics g,Rectangle card,int y,string title,string description) {
                int width=card.Width-128;
                Style.TextAt(g,title,12,Style.Text,new Rectangle(card.X+24,y,width,25),true);
                Style.TextAt(g,description,9,Style.Muted,new Rectangle(card.X+24,y+29,width,32),false,TextFormatFlags.Left|TextFormatFlags.WordBreak);
            }
        }
    }
    class NavigationButton : VaultButton {
        public bool Gear;
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; float cx=Width/2f,cy=Height/2f;
            using(var pen=new Pen(Selected?Style.Mint:Style.Muted,1.6f)) {
                if(Gear) { g.DrawEllipse(pen,cx-7,cy-7,14,14); g.DrawEllipse(pen,cx-2.5f,cy-2.5f,5,5); for(int i=0;i<8;i++) { double a=i*Math.PI/4; g.DrawLine(pen,cx+(float)Math.Cos(a)*7,cy+(float)Math.Sin(a)*7,cx+(float)Math.Cos(a)*10,cy+(float)Math.Sin(a)*10); } }
                else { g.DrawRectangle(pen,cx-10,cy-9,20,18); g.DrawLine(pen,cx-10,cy-3,cx+10,cy-3); g.DrawLine(pen,cx-3,cy-9,cx-3,cy+9); }
            }
        }
    }
    class Switch : CheckBox {
        public Switch() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); UseVisualStyleBackColor=false; Appearance=Appearance.Button; FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Cursor=Cursors.Hand; DoubleBuffered=true; BackColor=Style.Panel; }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics; g.Clear(BackColor); g.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=!Enabled?Color.FromArgb(37,42,46):Checked?Style.Mint:Color.FromArgb(52,61,66);
            Style.Box(g,new RectangleF(1,2,Width-2,Height-4),fill,Focused?Style.Text:Color.Transparent,(Height-4)/2);
            using(var b=new SolidBrush(!Enabled?Style.Muted:Checked?Style.Bg:Style.Text)) g.FillEllipse(b,Checked?Width-Height+5:5,6,Height-12,Height-12);
        }
    }
}
