using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PersonalShop {
    class FriendlyException : Exception { public FriendlyException(string s) : base(s) {} }
    class ApiException : Exception { public int Status; public ApiException(int s) { Status = s; } }
    class Offer { public string Id, Name, ImageUrl; public int? Price; public int Discount; public Image Art; }
    class ShopData {
        public List<Offer> Daily = new List<Offer>(), Night = new List<Offer>();
        public DateTime Expires, NightExpires; public string Shard; public string Warning = "", ClientNote = "";
    }
    static class Api {
        public static string Stage = "starting";
        public const string VP = "85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741";
        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        public static object Get(object o, string k) { object v; return Obj(o).TryGetValue(k, out v) ? v : null; }
        public static string Str(object o) { return o == null ? "" : Convert.ToString(o); }
        public static int Num(object o) { int n; return Int32.TryParse(Str(o), out n) ? n : 0; }
        public static IEnumerable<object> Items(object o) { var a = o as IEnumerable; if (a != null && !(o is string) && !(o is IDictionary)) foreach (object v in a) yield return v; }
        public static object Json(string s) { return new JavaScriptSerializer { MaxJsonLength = 20000000 }.DeserializeObject(s); }
        public static string ReadShared(string path) { using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var r = new StreamReader(f)) return r.ReadToEnd(); }
        public static byte[] Request(string url, Dictionary<string,string> headers, bool local, string method, int timeout = 15000) {
            var u = new Uri(url);
            if (local && (u.Host != "127.0.0.1" || u.Scheme != "https")) throw new FriendlyException("Invalid local client address.");
            var req = (HttpWebRequest)WebRequest.Create(u);
            req.Method = method; req.Timeout = timeout; req.ReadWriteTimeout = timeout; req.AllowAutoRedirect = false;
            req.UserAgent = "PersonalValorantShop/1.0";
            if (local) { req.Proxy = null; req.ServerCertificateValidationCallback = delegate { return true; }; }
            if (headers != null) foreach (var h in headers) req.Headers[h.Key] = h.Value;
            if (method == "POST") { req.ContentType = "application/json"; byte[] b = Encoding.UTF8.GetBytes("{}"); req.ContentLength = b.Length; using (var s = req.GetRequestStream()) s.Write(b,0,b.Length); }
            try { using (var res = req.GetResponse()) using (var s = res.GetResponseStream()) using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); } }
            catch (WebException ex) { var res = ex.Response as HttpWebResponse; if (res != null) { int status = (int)res.StatusCode; res.Close(); throw new ApiException(status); } throw new FriendlyException(local ? "Riot Client is not responding. Open Valorant, sign in, then refresh." : "Could not reach the shop service. Check your internet connection and try again."); }
        }
        static object Fetch(string url, Dictionary<string,string> h, bool local, string method) { return Json(Encoding.UTF8.GetString(Request(url,h,local,method))); }
        static int? Cost(object o) { object p = Get(o,VP); return p == null ? (int?)null : Num(p); }
        static Offer ParseOffer(object o, object cost) {
            string id = Str(Get(o,"OfferID"));
            foreach (object reward in Items(Get(o,"Rewards"))) { string r = Str(Get(reward,"ItemID")); if (r != "") { id = r; break; } }
            return new Offer { Id = id, Name = "Uncatalogued skin", Price = Cost(cost) };
        }
        public static ShopData Parse(object root) {
            var d = new ShopData(); object panel = Get(root,"SkinsPanelLayout");
            if (panel == null) throw new FriendlyException("Riot returned an unfamiliar shop format. The integration may need an update.");
            d.Expires = DateTime.UtcNow.AddSeconds(Num(Get(panel,"SingleItemOffersRemainingDurationInSeconds")));
            foreach (object o in Items(Get(panel,"SingleItemStoreOffers"))) d.Daily.Add(ParseOffer(o,Get(o,"Cost")));
            if (d.Daily.Count == 0) foreach (object id in Items(Get(panel,"SingleItemOffers"))) d.Daily.Add(new Offer { Id = Str(id), Name = "Uncatalogued skin" });
            if (d.Daily.Count == 0) throw new FriendlyException("No daily offers returned. Open Valorant's Store tab, then refresh.");
            object bonus = Get(root,"BonusStore"); d.NightExpires = DateTime.UtcNow.AddSeconds(Num(Get(bonus,"BonusStoreRemainingDurationInSeconds")));
            foreach (object o in Items(Get(bonus,"BonusStoreOffers"))) { var offer = ParseOffer(Get(o,"Offer"),Get(o,"DiscountCosts")); offer.Discount = Num(Get(o,"DiscountPercent")); d.Night.Add(offer); }
            return d;
        }
        public static string ShardFrom(string s) {
            var matches = Regex.Matches(s, @"https://(?:pd\.|glz-[a-z]+-\d\.)(na|eu|ap|kr|pbe)\.a\.pvp\.net", RegexOptions.IgnoreCase);
            if (matches.Count > 0) return matches[matches.Count-1].Groups[1].Value.ToLowerInvariant();
            var m = Regex.Match(s, @"-ares-deployment=(na|latam|br|eu|ap|kr|pbe)\b", RegexOptions.IgnoreCase);
            if (m.Success) { string r = m.Groups[1].Value.ToLowerInvariant(); return r == "br" || r == "latam" ? "na" : r; }
            return "";
        }
        public static ClientSession ReadClientSession(int timeout) {
            string app = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string lockPath = Path.Combine(app,@"Riot Games\Riot Client\Config\lockfile");
            if (!File.Exists(lockPath)) return null;
            string lockText;
            try { lockText = ReadShared(lockPath); } catch(UnauthorizedAccessException) { throw new FriendlyException("Windows denied access to Riot Client's session. Run this app under the same Windows account as Riot Client."); }
            string[] fields = lockText.Trim().Split(':'); int port;
            int pid;
            if (fields.Length != 5 || !Int32.TryParse(fields[1],out pid) || !Int32.TryParse(fields[2],out port) || port < 1 || port > 65535 || fields[4] != "https") return null;
            string localUrl = "https://127.0.0.1:" + port;
            var basic = new Dictionary<string,string> { {"Authorization","Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:"+fields[3]))} };
            object auth;
            try { auth = Json(Encoding.UTF8.GetString(Request(localUrl+"/entitlements/v1/token",basic,true,"GET",timeout))); }
            catch(ApiException) { return null; } catch(FriendlyException) { return null; }
            Guid player;
            if(Str(Get(auth,"accessToken"))=="" || Str(Get(auth,"token"))=="" || !Guid.TryParse(Str(Get(auth,"subject")),out player)) return null;
            return new ClientSession { Pid=pid, LocalUrl=localUrl, Headers=basic, Auth=auth };
        }
        public static ShopData Load(string region) { return Load(region,null,CancellationToken.None); }
        public static ShopData Load(string region, Action<string> progress, CancellationToken cancel) {
            using(var lease=new ClientLease(new WindowsClientHost(),progress,cancel)) {
                Stage="connecting to Riot Client";
                var session=lease.Connect(); cancel.ThrowIfCancellationRequested();
                var data=LoadStore(region,session,cancel);
                lease.Dispose(); // Close temporary client as soon as Riot's shop data arrives.
                data.ClientNote=lease.CleanupNote;
                if(lease.CleanupWarning!="") data.Warning=lease.CleanupWarning;
                cancel.ThrowIfCancellationRequested();
                if(progress!=null) progress("Loading skin names and artwork...");
                LoadCatalog(data); return data;
            }
        }
        static ShopData LoadStore(string region, ClientSession session, CancellationToken cancel) {
            string app=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localUrl=session.LocalUrl; var basic=session.Headers; object auth=session.Auth;
            string access = Str(Get(auth,"accessToken")), entitlement = Str(Get(auth,"token")), subject = Str(Get(auth,"subject"));
            string shard = region, version = "", sessionText = "";
            Stage = "detecting region and version";
            try { sessionText = new JavaScriptSerializer().Serialize(Fetch(localUrl+"/product-session/v1/external-sessions",basic,true,"GET")); } catch { }
            string log = ""; string logPath = Path.Combine(app,@"VALORANT\Saved\Logs\ShooterGame.log");
            try { if (File.Exists(logPath)) log = ReadShared(logPath); } catch { }
            if (shard == "auto") { shard = ShardFrom(sessionText); if (shard == "") shard = ShardFrom(log); }
            if (shard == "") throw new FriendlyException("Could not detect your region. Select your account's region above, then refresh.");
            if (!Regex.IsMatch(shard,@"^(na|eu|ap|kr|pbe)$")) throw new FriendlyException("Select a valid region.");
            var versions = Regex.Matches(log,@"release-[\w.]+-shipping-\d+-\d+"); if (versions.Count > 0) version = versions[versions.Count-1].Value;
            if (version == "") try { version = Str(Get(Get(Fetch("https://valorant-api.com/v1/version",null,false,"GET"),"data"),"riotClientVersion")); } catch { }
            if (version == "") throw new FriendlyException("Could not detect the game version. Open Valorant and try again.");
            string platform = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"platformType\":\"PC\",\"platformOS\":\"Windows\",\"platformOSVersion\":\"10.0.19042.1.256.64bit\",\"platformChipset\":\"Unknown\"}"));
            var headers = new Dictionary<string,string> { {"Authorization","Bearer "+access}, {"X-Riot-Entitlements-JWT",entitlement}, {"X-Riot-ClientVersion",version}, {"X-Riot-ClientPlatform",platform} };
            object store;
            string remote = "https://pd."+shard+".a.pvp.net/store/";
            cancel.ThrowIfCancellationRequested();
            Stage = "loading shop";
            try { store = Fetch(remote+"v2/storefront/"+subject,headers,false,"GET"); }
            catch (ApiException e) { if (e.Status == 404 || e.Status == 405) { try { store = Fetch(remote+"v3/storefront/"+subject,headers,false,"POST"); } catch (ApiException x) { throw ShopError(x.Status); } } else throw ShopError(e.Status); }
            Stage = "parsing shop";
            var data = Parse(store); data.Shard = shard;
            return data;
        }
        static void LoadCatalog(ShopData data) {
            // Catalog service receives no Riot headers, account IDs, or tokens.
            try {
                Stage = "loading public skin catalog";
                object catalog = Fetch("https://valorant-api.com/v1/weapons/skins",null,false,"GET");
                var map = new Dictionary<string,object>();
                foreach (object skin in Items(Get(catalog,"data"))) foreach (object level in Items(Get(skin,"levels"))) map[Str(Get(level,"uuid"))] = new object[] {skin,level};
                var all = new List<Offer>(data.Daily); all.AddRange(data.Night);
                foreach (var o in all) {
                    object value; if (map.TryGetValue(o.Id,out value)) { object[] v = (object[])value; o.Name = Str(Get(v[0],"displayName")); o.ImageUrl = Str(Get(v[1],"displayIcon")); if (o.ImageUrl == "") o.ImageUrl = Str(Get(v[0],"displayIcon")); }
                }
                Parallel.ForEach(all,new ParallelOptions { MaxDegreeOfParallelism = 4 },o => {
                    try { Uri u; if (Uri.TryCreate(o.ImageUrl,UriKind.Absolute,out u) && u.Scheme == "https" && u.Host == "media.valorant-api.com") { byte[] b = Request(o.ImageUrl,null,false,"GET"); using (var m = new MemoryStream(b)) using (var im = Image.FromStream(m)) o.Art = new Bitmap(im); } } catch { }
                });
            } catch { data.Warning = (data.Warning=="" ? "" : data.Warning+" ") + "Skin catalog unavailable; offers are shown by item ID."; }
        }
        static FriendlyException ShopError(int status) {
            if (status == 401 || status == 403) return new FriendlyException("Riot declined shop access ("+status+"). Open Valorant and refresh. If it persists, this unofficial integration may be blocked.");
            if (status == 429) return new FriendlyException("Too many requests. Wait a minute before refreshing.");
            return new FriendlyException("Shop service returned HTTP "+status+". Try again later; Riot may have changed the endpoint.");
        }
    }
    static class Program {
        [STAThread] static int Main(string[] args) {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>0 && args[0]=="--install-latest") return Updates.Bootstrap(Array.IndexOf(args,"--silent-update")>=0);
            if(args.Length>1 && args[0]=="--settings-test") {
                string saved=Preferences.SettingsPath,dir=Path.Combine(Path.GetTempPath(),"Nightshift-UI-"+Guid.NewGuid().ToString("N"));
                try { Preferences.SettingsPath=Path.Combine(dir,"settings.json"); using(var form=new ShopWindow(true)) { form.Show(); form.VerifySettingsNavigation(); } File.WriteAllText(args[1],"PASS: embedded navigation without owned windows, reused settings page, shop offers and tab retained, switches save preferences and enforce automatic check dependency."); return 0; }
                catch(Exception ex) { File.WriteAllText(args[1],"FAIL: "+ex.Message); return 1; }
                finally { Preferences.SettingsPath=saved; if(Directory.Exists(dir)) Directory.Delete(dir,true); }
            }
            if(args.Length>1 && args[0]=="--settings-preview") { using(var form=new ShopWindow(true)) { form.Show(); form.OpenSettings(args[1]); if(form.OwnedForms.Length!=0) throw new Exception("Settings opened a popup"); form.Size=new Size(1050,760); form.OpenSettings(args[1]+".compact.png"); form.CloseSettings(); form.SwitchTab(1); Application.DoEvents(); using(var image=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height)); image.Save(args[1]+".shop.png"); } } return 0; }
            if(args.Length>1 && args[0]=="--check-update") { try { var update=Updates.Fetch(); string result=update==null?"Up to date":("Available: "+update.Version.ToString(3)); if(update!=null && Array.IndexOf(args,"--download")>=0) { string file=Updates.Download(update); result+="; installer SHA-256 and size verified"; Directory.Delete(Path.GetDirectoryName(file),true); } File.WriteAllText(args[1],result); return 0; } catch(Exception ex) { File.WriteAllText(args[1],ex.Message); return 1; } }
            if(args.Length>0 && args[0]=="--update-test") { try { UpdateTests.Run(); File.WriteAllText(args[1],"PASS: settings defaults and persistence, malformed settings, release version filtering, exact asset URL, SHA-256 and size verification, silent update flags."); return 0; } catch(Exception ex) { File.WriteAllText(args[1],"FAIL: "+ex.Message); return 1; } }
            if(args.Length>0 && args[0]=="--self-test") { try { SelfTest(); LifecycleTests.Run(); File.WriteAllText(args.Length>1?args[1]:"tests.txt","PASS: shop parsing; lifecycle: existing client preserved, cold launch and sign-in wait, children-first cleanup, shop failure, timeout, cancellation, unrelated client, reused PID, running game, denied cleanup, launch failure."); return 0; } catch(Exception ex) { File.WriteAllText(args.Length>1?args[1]:"tests.txt","FAIL: "+ex.Message); return 1; } }
            if(args.Length>0 && args[0]=="--probe") { try { var d=Api.Load("auto"); File.WriteAllText(args[1],"Connected: "+d.Daily.Count+" daily offers; "+d.Night.Count+" Night Market offers; region "+d.Shard+". Catalog resolved: "+d.Daily.FindAll(o=>o.Name!="Uncatalogued skin").Count+"; images: "+d.Daily.FindAll(o=>o.Art!=null).Count+". "+d.ClientNote+" "+d.Warning); return 0; } catch(Exception ex) { File.WriteAllText(args[1],ex is FriendlyException?ex.Message:"Connection failed at "+Api.Stage+" ("+ex.GetType().Name+")."); return 2; } }
            if(args.Length>1 && args[0]=="--preview") { using(var form=new ShopWindow(true)) { form.Show(); form.ShowDemo(); Application.DoEvents(); using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]); } } return 0; }
            if(args.Length>1 && args[0]=="--live-preview") { var d=Api.Load("auto"); using(var form=new ShopWindow(false)) { form.Show(); form.ShowLive(d); Application.DoEvents(); using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]); } form.SwitchTab(1); Application.DoEvents(); using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]+".night.png"); } form.SwitchTab(0); form.Size=new Size(1050,760); Application.DoEvents(); using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[1]+".compact.png"); } } return 0; }
            bool created; using(var mutex=new Mutex(true,@"Local\ValorantShopChecker.Foundation",out created)) { if(!created) return 0; Application.Run(new ShopWindow(false)); } return 0;
        }
        static void Check(bool b) { if(!b) throw new Exception("Test failed"); }
        static void SelfTest() {
            Check(Api.VP == "85ad13f7-3d1b-5128-9eb2-7cd8ee0b5741");
            var fixture=Api.Json("{\"SkinsPanelLayout\":{\"SingleItemStoreOffers\":[{\"OfferID\":\"o\",\"Rewards\":[{\"ItemID\":\"skin\"}],\"Cost\":{\""+Api.VP+"\":1775}}],\"SingleItemOffersRemainingDurationInSeconds\":100},\"BonusStore\":{\"BonusStoreOffers\":[{\"Offer\":{\"OfferID\":\"night\"},\"DiscountCosts\":{\""+Api.VP+"\":1000},\"DiscountPercent\":30}]}}");
            var d=Api.Parse(fixture); Check(d.Daily.Count==1 && d.Daily[0].Id=="skin" && d.Daily[0].Price==1775); Check(d.Night[0].Price==1000 && d.Night[0].Discount==30); Check((d.Expires-DateTime.UtcNow).TotalSeconds>95);
            d=Api.Parse(Api.Json("{\"SkinsPanelLayout\":{\"SingleItemOffers\":[\"x\"]}}")); Check(d.Daily[0].Id=="x" && d.Daily[0].Price==null && d.Night.Count==0);
            Check(Api.ShardFrom("--foo -ares-deployment=br")=="na"); Check(Api.ShardFrom("https://glz-eu-1.eu.a.pvp.net")=="eu"); Check(Api.ShardFrom("https://pd.ap.a.pvp.net https://pd.kr.a.pvp.net")=="kr"); Check(Api.ShardFrom("unknown")=="");
            bool invalid=false; try { Api.Parse(Api.Json("{}")); } catch(FriendlyException) { invalid=true; } Check(invalid);
        }
    }
}

