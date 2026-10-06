using System;
using System.IO;
using System.Security.Cryptography;
namespace PersonalShop {
    static class UpdateTests {
        static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
        static object Release(string tag,string url,string digest,long size) { return Api.Json("{\"tag_name\":\""+tag+"\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\""+Updates.Installer+"\",\"browser_download_url\":\""+url+"\",\"digest\":\""+digest+"\",\"size\":"+size+"}]}"); }
        static void Reject(Action action,string message) { bool rejected=false; try { action(); } catch(FriendlyException) { rejected=true; } Assert(rejected,message); }
        public static void Run() {
            string original=Preferences.SettingsPath,dir=Path.Combine(Path.GetTempPath(),"ShopTests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try {
                Preferences.SettingsPath=Path.Combine(dir,"settings.json"); var p=Preferences.Load(); Assert(p.AutomaticUpdates && !p.AutomaticInstall && !p.RefreshOnLaunch && p.Region=="auto","Defaults");
                p.Region="eu"; p.AutomaticUpdates=false; p.AutomaticInstall=true; p.Save(); p=Preferences.Load(); Assert(p.Region=="eu" && !p.AutomaticUpdates && p.AutomaticInstall,"Round trip"); p.Region="invalid"; p.Save(); Assert(Preferences.Load().Region=="auto","Region validation");
                File.WriteAllText(Preferences.SettingsPath,"{bad"); Assert(Preferences.Load().AutomaticUpdates,"Malformed preferences");
                string tag="v99.0.0",url="https://github.com/"+Updates.Repository+"/releases/download/"+tag+"/"+Updates.Installer,hash=new string('a',64);
                var r=Updates.Parse(Release(tag,url,"sha256:"+hash,4)); Assert(r!=null && r.Hash==hash && r.Size==4,"Valid release");
                Assert(Updates.Parse(Release("v"+Updates.VersionText,"","",0))==null,"Same version"); Assert(Updates.Parse(Release("v0.0.1","","",0))==null,"Older version");
                Assert(Updates.Parse(Api.Json("{\"draft\":true}"))==null && Updates.Parse(Api.Json("{\"prerelease\":true}"))==null,"Draft/prerelease");
                Reject(()=>Updates.Parse(Release("latest",url,"sha256:"+hash,4)),"Invalid tag"); Reject(()=>Updates.Parse(Release(tag,url+"?redirect=bad","sha256:"+hash,4)),"Asset URL"); Reject(()=>Updates.Parse(Release(tag,url,"",4)),"Missing digest"); Reject(()=>Updates.Parse(Release(tag,url,"sha256:"+hash,200*1024*1024)),"Oversized release");
                string file=Path.Combine(dir,"fixture.exe"); File.WriteAllBytes(file,new byte[]{1,2,3,4}); using(var sha=SHA256.Create()) r.Hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant(); Updates.Verify(file,r);
                File.WriteAllBytes(file,new byte[]{4,3,2,1}); Reject(()=>Updates.Verify(file,r),"Hash mismatch"); File.WriteAllBytes(file,new byte[]{1}); Reject(()=>Updates.Verify(file,r),"Truncated download"); Assert(Updates.InstallerArguments.Contains("/VERYSILENT") && Updates.InstallerArguments.Contains("/NORESTART"),"Installer flags");
            } finally { Preferences.SettingsPath=original; Directory.Delete(dir,true); }
        }
    }
}
