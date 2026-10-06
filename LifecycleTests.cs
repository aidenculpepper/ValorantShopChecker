using System;
using System.Collections.Generic;
using System.Threading;

namespace PersonalShop {
    // Fake process/session host: lifecycle tests never launch or close a real program.
    class FakeClientHost : IClientHost {
        public List<ClientProcess> Processes = new List<ClientProcess>();
        public List<int> Closed = new List<int>(), Hidden = new List<int>(); public int Starts, Reads, ReadyAfter, Activations, StartedSessionPid=100;
        public bool FailStart, FailClose, DelayedStart; public DateTime CloseStarted; public Action<FakeClientHost> DuringPause; public Action<FakeClientHost,ClientProcess> DuringClose;
        public DateTime Clock = new DateTime(2026,10,5,0,0,0,DateTimeKind.Utc);
        public DateTime Now { get { return Clock; } }
        public List<ClientProcess> Snapshot() { return new List<ClientProcess>(Processes); }
        public ClientProcess Make(int id,int parent,string name) { return new ClientProcess {Id=id,ParentId=parent,Name=name,Started=Clock}; }
        public ClientProcess Start() { Starts++; if(FailStart) throw new FriendlyException("Launch failed."); var p=Make(100,1,"RiotClientServices.exe"); if(!DelayedStart) { Processes.Add(p); Processes.Add(Make(101,100,"RiotClientUx.exe")); } return p; }
        public void ActivateExisting() { Activations++; }
        public ClientSession ReadSession() { Reads++; return Reads > ReadyAfter ? new ClientSession {Pid=Starts>0 ? StartedSessionPid : 10} : null; }
        public void Hide(ClientProcess p) { Hidden.Add(p.Id); }
        public bool Close(ClientProcess p) { if(CloseStarted==DateTime.MinValue) CloseStarted=Clock; Closed.Add(p.Id); if(FailClose) return false; if(DuringClose!=null) DuringClose(this,p); Processes.RemoveAll(x=>x.Same(p)); return true; }
        public void Pause(int ms) { Clock=Clock.AddMilliseconds(ms); if(DuringPause!=null) DuringPause(this); }
    }
    static class LifecycleTests {
        static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
        public static void Run() {
            var existing=new FakeClientHost(); existing.Processes.Add(existing.Make(10,1,"RiotClientServices.exe"));
            using(var lease=new ClientLease(existing,null,CancellationToken.None)) { lease.Connect(); Check(!lease.StartedClient,"Existing client must not be owned"); }
            Check(existing.Starts==0 && existing.Closed.Count==0 && existing.Hidden.Count==0 && existing.Processes.Count==1,"Existing client changed");

            var background=new FakeClientHost { ReadyAfter=2 }; background.Processes.Add(background.Make(10,1,"RiotClientServices.exe"));
            using(var lease=new ClientLease(background,null,CancellationToken.None)) lease.Connect();
            Check(background.Activations==1 && background.Starts==0 && background.Closed.Count==0,"Background client activation closed an existing client");

            var orphan=new FakeClientHost(); orphan.Processes.Add(orphan.Make(9,1,"RiotClientCrashHandler.exe"));
            using(var lease=new ClientLease(orphan,null,CancellationToken.None)) lease.Connect();
            Check(orphan.Starts==1 && orphan.Processes.Count==1 && orphan.Processes[0].Id==9,"Orphan crash handler blocked launch or was closed");

            var cold=new FakeClientHost { ReadyAfter=2 };
            using(var lease=new ClientLease(cold,null,CancellationToken.None)) { lease.Connect(); Check(lease.StartedClient && cold.Reads==3,"Did not wait for sign-in"); }
            Check(cold.Starts==1 && cold.Closed.Count==2 && cold.Closed[0]==101 && cold.Closed[1]==100 && cold.Processes.Count==0,"Owned client did not close children first");
            Check(cold.Hidden.Contains(100) && cold.Hidden.Contains(101),"Temporary client was not hidden");

            var respawn=new FakeClientHost();
            respawn.DuringClose=(h,p)=> { if(p.Id==101) h.Processes.Add(h.Make(102,100,"RiotClientUx.exe")); };
            using(var lease=new ClientLease(respawn,null,CancellationToken.None)) { lease.Connect(); lease.Dispose(); Check(lease.CleanupNote=="The temporary Riot Client was closed and its exit was verified.","Successful closure was not reported"); }
            Check(respawn.Processes.Count==0 && respawn.Closed.Contains(102),"Respawned helper escaped cleanup");

            var delayed=new FakeClientHost { DelayedStart=true, StartedSessionPid=101 }; DateTime delayedStart=delayed.Clock;
            delayed.DuringPause=h=> { if((h.Clock-delayedStart).TotalSeconds>=1) { h.Processes.Add(h.Make(101,100,"RiotClientServices.exe")); h.DuringPause=null; } };
            using(var lease=new ClientLease(delayed,null,CancellationToken.None)) { lease.Connect(); Check((delayed.Clock-delayedStart).TotalSeconds>=1,"Startup completed before process appeared"); }
            Check(delayed.Closed.Contains(101) && delayed.Processes.Count==0,"Delayed service escaped cleanup");

            var lateHelper=new FakeClientHost(); bool helperAdded=false;
            lateHelper.DuringPause=h=> { if(!helperAdded && h.CloseStarted!=DateTime.MinValue && (h.Clock-h.CloseStarted).TotalSeconds>=1) { helperAdded=true; h.Processes.Add(h.Make(102,100,"RiotClientUx.exe")); } };
            using(var lease=new ClientLease(lateHelper,null,CancellationToken.None)) lease.Connect();
            Check(helperAdded && lateHelper.Closed.Contains(102) && lateHelper.Processes.Count==0,"Delayed shutdown helper escaped cleanup");

            var failed=new FakeClientHost();
            try { using(var lease=new ClientLease(failed,null,CancellationToken.None)) { lease.Connect(); throw new InvalidOperationException("Simulated shop failure"); } } catch(InvalidOperationException) {}
            Check(failed.Processes.Count==0,"Shop failure leaked temporary client");

            var timeout=new FakeClientHost { ReadyAfter=Int32.MaxValue }; bool timedOut=false;
            try { using(var lease=new ClientLease(timeout,null,CancellationToken.None)) lease.Connect(); } catch(FriendlyException) { timedOut=true; }
            Check(timedOut && timeout.Processes.Count==0,"Sign-in timeout did not clean up");

            var cancelled=new FakeClientHost { ReadyAfter=Int32.MaxValue }; bool wasCancelled=false;
            using(var token=new CancellationTokenSource()) {
                cancelled.DuringPause=h=>token.Cancel();
                try { using(var lease=new ClientLease(cancelled,null,token.Token)) lease.Connect(); } catch(OperationCanceledException) { wasCancelled=true; }
            }
            Check(wasCancelled && cancelled.Processes.Count==0,"Cancellation did not clean up");

            var unrelated=new FakeClientHost { ReadyAfter=1 };
            unrelated.DuringPause=h=> { h.Processes.Add(h.Make(200,2,"RiotClientServices.exe")); h.DuringPause=null; };
            using(var lease=new ClientLease(unrelated,null,CancellationToken.None)) lease.Connect();
            Check(unrelated.Processes.Count==1 && unrelated.Processes[0].Id==200 && !unrelated.Closed.Contains(200) && !unrelated.Hidden.Contains(200),"Unrelated newly opened client was changed");

            var reused=new FakeClientHost();
            using(var lease=new ClientLease(reused,null,CancellationToken.None)) {
                lease.Connect(); reused.Processes.RemoveAll(p=>p.Id==100); reused.Clock=reused.Clock.AddSeconds(1); reused.Processes.Add(reused.Make(100,2,"RiotClientServices.exe"));
            }
            Check(!reused.Closed.Contains(100) && reused.Processes.Exists(p=>p.Id==100),"Reused PID was closed");

            var game=new FakeClientHost();
            using(var lease=new ClientLease(game,null,CancellationToken.None)) { lease.Connect(); game.Processes.Add(game.Make(300,100,"VALORANT.exe")); lease.Dispose(); Check(lease.CleanupWarning!="","Game protection had no notice"); }
            Check(game.Closed.Count==0,"Running game lost its client");

            var denied=new FakeClientHost { FailClose=true };
            using(var lease=new ClientLease(denied,null,CancellationToken.None)) { lease.Connect(); lease.Dispose(); Check(lease.CleanupWarning!="","Close failure was hidden"); }

            var launchFailure=new FakeClientHost { FailStart=true }; bool launchFailed=false;
            try { using(var lease=new ClientLease(launchFailure,null,CancellationToken.None)) lease.Connect(); } catch(FriendlyException) { launchFailed=true; }
            Check(launchFailed && launchFailure.Closed.Count==0,"Launch failure changed processes");
        }
    }
}
