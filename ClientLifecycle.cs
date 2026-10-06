using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace PersonalShop {
    class ClientProcess {
        public int Id, ParentId; public string Name; public DateTime Started;
        public bool Same(ClientProcess other) { return other != null && Id == other.Id && Started == other.Started; }
        public bool IsClient { get { return Name.StartsWith("RiotClient",StringComparison.OrdinalIgnoreCase); } }
        public bool IsGame { get { return !IsClient; } }
    }
    class ClientSession {
        public int Pid; public string LocalUrl; public Dictionary<string,string> Headers; public object Auth;
    }
    interface IClientHost {
        List<ClientProcess> Snapshot(); ClientProcess Start(); void ActivateExisting(); ClientSession ReadSession();
        void Hide(ClientProcess p); bool Close(ClientProcess p); void Pause(int ms); DateTime Now { get; }
    }
    class WindowsClientHost : IClientHost {
        delegate bool WindowCallback(IntPtr window,IntPtr state);
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,IntPtr state);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
        // Initialize Valorant's client services without pressing Play or starting the game.
        const string ClientArguments="--launch-product=valorant --launch-patchline=live --disable-auto-launch";
        public DateTime Now { get { return DateTime.UtcNow; } }
        public void Pause(int ms) { Thread.Sleep(ms); }
        public List<ClientProcess> Snapshot() {
            var list = new List<ClientProcess>();
            const string query = "SELECT ProcessId, ParentProcessId, Name, CreationDate FROM Win32_Process WHERE Name LIKE 'RiotClient%.exe' OR Name = 'VALORANT.exe' OR Name = 'VALORANT-Win64-Shipping.exe' OR Name = 'League of Legends.exe' OR Name LIKE 'LeagueClient%.exe' OR Name = 'LoR.exe'";
            try {
                using(var search = new ManagementObjectSearcher(query)) using(var results = search.Get()) foreach(ManagementObject p in results) {
                    using(p) { string created = Convert.ToString(p["CreationDate"]); if(created == "") throw new InvalidOperationException();
                        list.Add(new ClientProcess { Id = Convert.ToInt32(p["ProcessId"]), ParentId = Convert.ToInt32(p["ParentProcessId"]), Name = Convert.ToString(p["Name"]), Started = ManagementDateTimeConverter.ToDateTime(created).ToUniversalTime() }); }
                }
                return list;
            } catch { throw new FriendlyException("Windows could not check Riot Client's process ownership. Run this app under the same Windows account as Riot Client."); }
        }
        static string Locate() {
            var paths = new List<string>();
            string metadata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"Riot Games\RiotClientInstalls.json");
            try { object installs = Api.Json(Api.ReadShared(metadata)); paths.Add(Api.Str(Api.Get(installs,"rc_live"))); paths.Add(Api.Str(Api.Get(installs,"rc_default"))); } catch { }
            paths.Add(@"C:\Riot Games\Riot Client\RiotClientServices.exe");
            foreach(string p in paths) {
                try { if(Path.IsPathRooted(p) && String.Equals(Path.GetFileName(p),"RiotClientServices.exe",StringComparison.OrdinalIgnoreCase) && File.Exists(p)) return Path.GetFullPath(p); } catch { }
            }
            throw new FriendlyException("Could not locate Riot Client. Open it manually, sign in, then refresh.");
        }
        public ClientProcess Start() {
            string exe = Locate();
            try {
                DateTime requested=DateTime.UtcNow;
                using(var process = Process.Start(new ProcessStartInfo { FileName = exe, Arguments=ClientArguments, WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden })) {
                    if(process == null) throw new InvalidOperationException();
                    DateTime started=requested;
                    try { started=process.StartTime.ToUniversalTime(); } catch(InvalidOperationException) { }
                    return new ClientProcess { Id = process.Id, Name = "RiotClientServices.exe", Started = started };
                }
            } catch { throw new FriendlyException("Could not start Riot Client. Open it manually and refresh."); }
        }
        public void ActivateExisting() {
            string exe=Locate();
            try { using(var p=Process.Start(new ProcessStartInfo { FileName=exe, Arguments=ClientArguments, WorkingDirectory=Path.GetDirectoryName(exe), UseShellExecute=true, WindowStyle=ProcessWindowStyle.Hidden })) {} }
            catch { throw new FriendlyException("Could not initialize Riot Client. Open it manually and refresh."); }
        }
        public ClientSession ReadSession() { return Api.ReadClientSession(3000); }
        public void Hide(ClientProcess expected) {
            try {
                using(var p=Process.GetProcessById(expected.Id)) {
                    if(Math.Abs((p.StartTime.ToUniversalTime()-expected.Started).TotalMilliseconds)>=1 || !String.Equals(p.ProcessName+".exe",expected.Name,StringComparison.OrdinalIgnoreCase)) return;
                    EnumWindows(delegate(IntPtr window,IntPtr state) { uint pid; GetWindowThreadProcessId(window,out pid); if(pid==(uint)expected.Id) ShowWindow(window,0); return true; },IntPtr.Zero);
                }
            } catch { } // A client can exit between the identity check and window enumeration.
        }
        public bool Close(ClientProcess expected) {
            try {
                using(var p = Process.GetProcessById(expected.Id)) {
                    // WMI creation dates and Process.StartTime can differ by less than a millisecond.
                    if(Math.Abs((p.StartTime.ToUniversalTime()-expected.Started).TotalMilliseconds) >= 1 || !String.Equals(p.ProcessName+".exe",expected.Name,StringComparison.OrdinalIgnoreCase)) return false;
                    if(p.HasExited) return true;
                    if(p.MainWindowHandle != IntPtr.Zero) { p.CloseMainWindow(); if(p.WaitForExit(1500)) return true; }
                    p.Kill(); return p.WaitForExit(3000);
                }
            } catch(ArgumentException) { return true; } catch(InvalidOperationException) { return true; } catch { return false; }
        }
    }
    class ClientLease : IDisposable {
        readonly IClientHost host; readonly Action<string> progress; readonly CancellationToken cancel;
        readonly Dictionary<int,ClientProcess> owned = new Dictionary<int,ClientProcess>();
        ClientProcess root; bool disposed, observedClient; DateTime launchedAt; public bool StartedClient { get { return root != null; } }
        public string CleanupWarning = "", CleanupNote = "";
        public ClientLease(IClientHost h, Action<string> report, CancellationToken token) { host=h; progress=report ?? delegate {}; cancel=token; }
        public ClientSession Connect() {
            var before = host.Snapshot();
            if(!ActiveClientOrGame(before)) {
                cancel.ThrowIfCancellationRequested(); progress("Starting Riot Client temporarily in the background...");
                // Recheck immediately before launch so a newly opened client stays open.
                if(!ActiveClientOrGame(host.Snapshot())) { launchedAt=host.Now; root=host.Start(); owned[root.Id]=root; }
            }
            if(root == null) progress("Using your running Riot Client; it will stay open.");
            DateTime deadline=host.Now.AddSeconds(60); bool activated=false;
            while(host.Now < deadline) {
                cancel.ThrowIfCancellationRequested(); var snapshot=host.Snapshot(); Observe(snapshot); HideOwned(snapshot);
                try {
                    var session=host.ReadSession();
                    if(session != null) {
                        // A stale lockfile or a fast-exiting launcher is not proof that the client is ready.
                        snapshot=host.Snapshot(); Observe(snapshot);
                        ClientProcess sessionProcess=snapshot.Find(p=>p.Id==session.Pid), expectedSession;
                        if(root!=null && (sessionProcess==null || !owned.TryGetValue(session.Pid,out expectedSession) || !SameIdentity(expectedSession,sessionProcess))) {
                            progress("Waiting for the started Riot Client session's process to appear..."); host.Pause(250); continue;
                        }
                        // Only associate the login with the client started by this refresh.
                        progress("Loading the current shop from Riot..."); return session;
                    }
                } catch(UnauthorizedAccessException) { throw new FriendlyException("Windows denied access to Riot Client's session. Run this app under the same Windows account as Riot Client."); }
                if(root==null && !activated && !host.Snapshot().Exists(p=>p.IsGame)) {
                    cancel.ThrowIfCancellationRequested(); activated=true;
                    progress("Initializing your existing Riot Client, with game launch disabled..."); host.ActivateExisting();
                }
                progress(root == null ? "Waiting for Riot Client to finish signing in..." : "Waiting for Riot Client's remembered sign-in...");
                host.Pause(250);
            }
            throw new FriendlyException("Riot Client did not sign in within 60 seconds. Open it, sign in with Stay signed in enabled, then refresh.");
        }
        static bool ActiveClientOrGame(List<ClientProcess> snapshot) { return snapshot.Exists(p=>p.IsGame || !String.Equals(p.Name,"RiotClientCrashHandler.exe",StringComparison.OrdinalIgnoreCase)); }
        void Observe(List<ClientProcess> snapshot) {
            if(root == null) return;
            foreach(var p in snapshot) { ClientProcess expected; if(p.IsClient && owned.TryGetValue(p.Id,out expected) && SameIdentity(expected,p)) observedClient=true; }
            bool changed;
            do {
                changed=false;
                foreach(var p in snapshot) {
                    if(!p.IsClient || owned.ContainsKey(p.Id)) continue;
                    ClientProcess parent;
                    if(owned.TryGetValue(p.ParentId,out parent) && p.Started >= parent.Started) {
                        // Parent must still have the same identity if its PID is present.
                        var currentParent=snapshot.Find(x=>x.Id==parent.Id);
                        if(currentParent != null && !SameIdentity(parent,currentParent)) continue;
                        owned[p.Id]=p; observedClient=true; changed=true;
                    }
                }
            } while(changed);
        }
        static bool SameIdentity(ClientProcess a, ClientProcess b) { return a.Id==b.Id && Math.Abs((a.Started-b.Started).TotalMilliseconds)<1; }
        void HideOwned(List<ClientProcess> snapshot) {
            if(root==null || snapshot.Exists(p=>p.IsGame)) return;
            foreach(var p in snapshot) { ClientProcess expected; if(owned.TryGetValue(p.Id,out expected) && SameIdentity(expected,p)) host.Hide(p); }
        }
        public void Dispose() {
            if(disposed) return; disposed=true;
            if(root==null) { CleanupNote="Your existing Riot Client was left open."; return; }
            try {
                var current=host.Snapshot(); Observe(current);
                if(current.Exists(p=>p.IsGame)) { CleanupWarning="Riot Client was left open because a Riot game is running."; return; }
                progress("Closing the Riot Client started for this refresh...");
                // Children first, then their service. Never close an unrelated PID or a reused PID.
                DateTime deadline=host.Now.AddSeconds(15), quietSince=DateTime.MinValue;
                // Keep watching after the launcher exits. Empty snapshots are common during startup/handoff.
                while(host.Now<deadline) {
                    current=host.Snapshot(); Observe(current);
                    var close=new List<ClientProcess>();
                    foreach(var p in current) { ClientProcess expected; if(owned.TryGetValue(p.Id,out expected) && SameIdentity(expected,p)) close.Add(p); }
                    if(close.Count==0) {
                        if(quietSince==DateTime.MinValue) quietSince=host.Now;
                        bool launchGracePassed=(host.Now-launchedAt).TotalSeconds>=5;
                        if(launchGracePassed && (host.Now-quietSince).TotalSeconds>=3) break;
                        progress(observedClient ? "Verifying that Riot Client has finished exiting..." : "Waiting for the launched Riot process before finishing cleanup...");
                        host.Pause(250); continue;
                    }
                    quietSince=DateTime.MinValue;
                    close.Sort((a,b)=>Depth(b).CompareTo(Depth(a)));
                    foreach(var p in close) {
                        var latest=host.Snapshot();
                        if(latest.Exists(x=>x.IsGame)) { CleanupWarning="Riot Client was left open because a Riot game started."; return; }
                        var still=latest.Find(x=>x.Id==p.Id);
                        if(still != null && SameIdentity(still,p)) host.Close(p);
                    }
                    host.Pause(250);
                }
                var remaining=host.Snapshot();
                if(remaining.Exists(p=>owned.ContainsKey(p.Id) && SameIdentity(owned[p.Id],p))) CleanupWarning="Windows could not close the temporary Riot Client within 15 seconds. You can exit it manually.";
                else if(!observedClient) CleanupWarning="The launcher exited before a Riot Client process could be confirmed. Cleanup waited for delayed startup, but closure could not be verified.";
                else if(quietSince==DateTime.MinValue || (host.Now-quietSince).TotalSeconds<3) CleanupWarning="Riot Client stopped, but the shutdown confirmation timed out. Check the tray for any remaining client.";
                else CleanupNote="The temporary Riot Client was closed and its exit was verified.";
            } catch { CleanupWarning="The temporary Riot Client could not be safely closed. You can exit it manually."; }
        }
        int Depth(ClientProcess p) { int d=0; ClientProcess parent; var seen=new HashSet<int>(); while(seen.Add(p.Id) && owned.TryGetValue(p.ParentId,out parent)) { d++; p=parent; } return d; }
    }
}
