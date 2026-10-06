using System;
using System.Diagnostics;
using System.Drawing;
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
            autoPending=available!=null && preferences.AutomaticUpdates && preferences.AutomaticInstall;
            if(!IsDisposed && !closing && autoPending && !busy && !settingsOpen) { autoPending=false; await InstallUpdate(true); }
        }
        async Task InstallUpdate(bool automatic) {
            if(available==null || updateBusy || busy || closing) return; updateBusy=true; string file=null;
            try {
                updateStatus="Downloading verified installer..."; var release=available; file=await Task.Run(()=>Updates.Download(release));
                if(IsDisposed || closing || automatic && (!preferences.AutomaticUpdates || !preferences.AutomaticInstall)) { Directory.Delete(Path.GetDirectoryName(file),true); return; }
                Updates.Launch(file); updateStatus="Installing update..."; Close();
            } catch(Exception ex) { updateStatus=ex is FriendlyException?ex.Message:"Couldn't start the installer. Try again later."; if(file!=null) try { Directory.Delete(Path.GetDirectoryName(file),true); } catch {} }
            finally { updateBusy=false; }
        }
        public void OpenSettings(string capturePath=null) {
            settingsOpen=true;
            using(var dialog=new Form { Text="Nightshift · Settings",BackColor=Style.Bg,ForeColor=Style.Text,Font=new Font("Segoe UI",10),ClientSize=new Size(540,530),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,Icon=Icon }) {
                if(capturePath!=null) dialog.Shown+=delegate { dialog.BeginInvoke((Action)delegate { using(var image=new Bitmap(dialog.Width,dialog.Height)) { dialog.DrawToBitmap(image,new Rectangle(0,0,dialog.Width,dialog.Height)); image.Save(capturePath); } dialog.Close(); }); };
                var title=new Label {Text="MAKE IT YOURS.",Font=new Font("Segoe UI",21,FontStyle.Bold),ForeColor=Style.Mint,Bounds=new Rectangle(24,20,490,45)}; dialog.Controls.Add(title);
                dialog.Controls.Add(new Label {Text="Your preferences stay on this Windows account.",ForeColor=Style.Muted,Bounds=new Rectangle(26,72,490,26)});
                AddToggle(dialog,"Refresh my shop when the app opens",112,preferences.RefreshOnLaunch,value=>preferences.RefreshOnLaunch=value);
                var checks=AddToggle(dialog,"Check for updates automatically",158,preferences.AutomaticUpdates,value=>preferences.AutomaticUpdates=value);
                var installs=AddToggle(dialog,"Install updates automatically",204,preferences.AutomaticInstall,value=>preferences.AutomaticInstall=value);
                installs.Enabled=checks.Checked; checks.CheckedChanged+=delegate { installs.Enabled=checks.Checked; };
                dialog.Controls.Add(new Label {Text="Riot Client opens in the background when needed.\nA client that was already running stays open.",ForeColor=Style.Muted,Bounds=new Rectangle(26,254,490,48)});
                var message=new Label {Text=updateStatus,ForeColor=Style.Muted,Bounds=new Rectangle(26,313,490,48)}; dialog.Controls.Add(message);
                var check=new VaultButton {Text="Check for updates",Accent=true,Bounds=new Rectangle(26,366,232,40)};
                var install=new VaultButton {Text="Install update",Bounds=new Rectangle(276,366,238,40),Visible=available!=null,Enabled=available!=null && !updateBusy && !busy}; dialog.Controls.Add(check); dialog.Controls.Add(install);
                var uninstall=new VaultButton {Text="Uninstall",Bounds=new Rectangle(26,423,232,38)}; dialog.Controls.Add(uninstall);
                string uninstaller=Path.Combine(Application.StartupPath,"unins000.exe"); uninstall.Enabled=File.Exists(uninstaller);
                uninstall.Click+=delegate { if(busy || updateBusy) { message.Text="Wait for the current operation to finish."; return; } try { Process.Start(new ProcessStartInfo(uninstaller) {UseShellExecute=true}); dialog.Close(); Close(); } catch { message.Text="Couldn't open the uninstaller."; } };
                dialog.Controls.Add(new Label {Text="Valorant Shop Checker · v"+Updates.VersionText,ForeColor=Style.Muted,Bounds=new Rectangle(26,478,488,24)});
                check.Click+=async delegate { await CheckUpdate(); };
                install.Click+=async delegate { await InstallUpdate(false); if(IsDisposed && !dialog.IsDisposed) dialog.Close(); };
                using(var timer=new System.Windows.Forms.Timer {Interval=100}) {
                    timer.Tick+=delegate { if(dialog.IsDisposed) return; message.Text=updateStatus; message.ForeColor=available==null && updateStatus=="You're up to date"?Style.Mint:available!=null?Style.Orange:Style.Muted; check.Enabled=!updateBusy; install.Enabled=available!=null && !updateBusy && !busy; install.Visible=available!=null; };
                    timer.Start(); dialog.ShowDialog(this);
                }
            }
            settingsOpen=false;
        }
        CheckBox AddToggle(Form dialog,string text,int y,bool value,Action<bool> apply) {
            var toggle=new CheckBox {Text=text,Checked=value,ForeColor=Style.Text,BackColor=Style.Panel,Bounds=new Rectangle(26,y,488,36),Padding=new Padding(12,0,0,0)};
            toggle.CheckedChanged+=delegate { apply(toggle.Checked); SavePreferences(); }; dialog.Controls.Add(toggle); return toggle;
        }
    }
}
