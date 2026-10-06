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
            Rectangle routine,updates,client,about;
            public SettingsPage(ShopWindow window) {
                owner=window;
                refreshSwitch=Toggle("Refresh shop on launch",owner.preferences.RefreshOnLaunch,value=>owner.preferences.RefreshOnLaunch=value);
                checkSwitch=Toggle("Check for updates automatically",owner.preferences.AutomaticUpdates,value=> { owner.preferences.AutomaticUpdates=value; installSwitch.Enabled=value; });
                installSwitch=Toggle("Install updates automatically",owner.preferences.AutomaticInstall,value=>owner.preferences.AutomaticInstall=value);
                installSwitch.Enabled=checkSwitch.Checked;
                check=new VaultButton {Text="Check for updates",Accent=true}; Controls.Add(check); check.Click+=async delegate { await owner.CheckUpdate(); };
                install=new VaultButton {Text="Install update"}; Controls.Add(install); install.Click+=async delegate { await owner.InstallUpdate(false); };
                uninstall=new VaultButton {Text="Uninstall app"}; Controls.Add(uninstall);
                string path=Path.Combine(Application.StartupPath,"unins000.exe"); uninstall.Enabled=File.Exists(path);
                uninstall.Click+=delegate { if(owner.busy || owner.updateBusy) return; try { Process.Start(new ProcessStartInfo(path) {UseShellExecute=true}); owner.Close(); } catch { owner.updateStatus="Couldn't open the uninstaller."; RefreshState(); } };
                message=new Label {BackColor=Style.Panel,ForeColor=Style.Muted,Font=new Font("Segoe UI",10),AutoEllipsis=true}; Controls.Add(message);
                Resize+=delegate { LayoutPage(); }; LayoutPage(); RefreshState();
            }
            Switch Toggle(string name,bool value,Action<bool> apply) {
                var control=new Switch {Checked=value,AccessibleName=name,AccessibleRole=AccessibleRole.CheckButton,TabStop=true};
                control.CheckedChanged+=delegate { apply(control.Checked); owner.SavePreferences(); Invalidate(); }; Controls.Add(control); return control;
            }
            void LayoutPage() {
                int usable=Width-40,gap=20,left=(usable-gap)*57/100,right=usable-gap-left;
                routine=new Rectangle(20,132,left,314); updates=new Rectangle(20+left+gap,132,right,314);
                client=new Rectangle(20,466,left,118); about=new Rectangle(20+left+gap,466,right,118);
                refreshSwitch.Bounds=new Rectangle(routine.Right-84,198,58,30); checkSwitch.Bounds=new Rectangle(routine.Right-84,280,58,30); installSwitch.Bounds=new Rectangle(routine.Right-84,362,58,30);
                message.Bounds=new Rectangle(updates.X+24,266,updates.Width-48,54);
                check.Bounds=new Rectangle(updates.X+24,336,updates.Width-48,40);
                install.Bounds=new Rectangle(updates.X+24,386,updates.Width-48,40);
                uninstall.Bounds=new Rectangle(about.Right-154,about.Y+65,130,32); Invalidate();
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
                Style.TextAt(g,"YOUR SPACE  /  SETTINGS",8,Style.Mint,new Rectangle(20,4,440,22),true);
                Style.TextAt(g,"A little more you.",30,Style.Text,new Rectangle(18,30,Width-40,54),true);
                Style.TextAt(g,"Set your routine. Keep Nightshift ready for the next rotation.",11,Style.Muted,new Rectangle(20,89,Width-40,26));
                foreach(var card in new[]{routine,updates,client,about}) Style.Box(g,card,Style.Panel,Style.Line,14);
                Style.TextAt(g,"YOUR ROUTINE",10,Style.Muted,new Rectangle(routine.X+24,routine.Y+19,routine.Width-48,26),true);
                Row(g,routine,194,"Refresh on launch","Your latest offers, as soon as you open Nightshift.");
                Row(g,routine,276,"Automatic update checks","Check at startup and every six hours.");
                Row(g,routine,358,"Automatic installation",checkSwitch.Checked?"Install new versions when the shop is idle.":"Turn on automatic checks to enable this.");
                using(var pen=new Pen(Style.Line)) { g.DrawLine(pen,routine.X+24,260,routine.Right-24,260); g.DrawLine(pen,routine.X+24,342,routine.Right-24,342); }
                Style.TextAt(g,"APP UPDATES",10,Style.Muted,new Rectangle(updates.X+24,updates.Y+19,updates.Width-48,26),true);
                Style.TextAt(g,"Nightshift",23,Style.Text,new Rectangle(updates.X+24,188,updates.Width-48,42),true);
                Style.Box(g,new RectangleF(updates.X+24,235,114,23),Color.FromArgb(32,43,30),Color.Transparent,11);
                Style.TextAt(g,"VERSION "+Updates.VersionText,8,Style.Mint,new Rectangle(updates.X+34,235,96,23),true);
                Style.TextAt(g,"RIOT CLIENT",9,Style.Mint,new Rectangle(client.X+24,client.Y+15,client.Width-48,24),true);
                Style.TextAt(g,"Quietly connected. Carefully closed.",12,Style.Text,new Rectangle(client.X+24,client.Y+43,client.Width-48,26),true);
                Style.TextAt(g,"Only a client started by Nightshift is closed afterward.",9,Style.Muted,new Rectangle(client.X+24,client.Y+76,client.Width-48,24));
                Style.TextAt(g,"ON THIS PC",9,Style.Muted,new Rectangle(about.X+24,about.Y+15,about.Width-48,24),true);
                Style.TextAt(g,"Preferences save automatically.",11,Style.Text,new Rectangle(about.X+24,about.Y+42,about.Width-48,24));
                Style.TextAt(g,uninstall.Enabled?"Installed app":"Portable app",8,Style.Muted,new Rectangle(about.X+24,about.Y+74,160,22));
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
        public Switch() { Appearance=Appearance.Button; FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Cursor=Cursors.Hand; DoubleBuffered=true; BackColor=Style.Panel; }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=!Enabled?Color.FromArgb(37,42,46):Checked?Style.Mint:Color.FromArgb(52,61,66);
            Style.Box(g,new RectangleF(1,2,Width-2,Height-4),fill,Focused?Style.Text:Color.Transparent,(Height-4)/2);
            using(var b=new SolidBrush(!Enabled?Style.Muted:Checked?Style.Bg:Style.Text)) g.FillEllipse(b,Checked?Width-Height+5:5,6,Height-12,Height-12);
        }
    }
}
