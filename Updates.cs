using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalShop {
    class ReleaseInfo { public Version Version; public string Url, Hash; public long Size; }
    static class Updates {
        public const string Repository="aidenculpepper/ValorantShopChecker";
        public const string Installer="ValorantShopCheckerSetup.exe";
        public static Version Current { get { return typeof(Updates).Assembly.GetName().Version; } }
        public static string VersionText { get { return Current.ToString(3); } }
        public static ReleaseInfo Parse(object root) {
            if(Api.Get(root,"draft") is bool && (bool)Api.Get(root,"draft") || Api.Get(root,"prerelease") is bool && (bool)Api.Get(root,"prerelease")) return null;
            string tag=Api.Str(Api.Get(root,"tag_name")); Version version;
            if(!Regex.IsMatch(tag,@"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag.Substring(1),out version)) throw new FriendlyException("The release version is invalid.");
            if(version<=new Version(Current.ToString(3))) return null;
            foreach(object asset in Api.Items(Api.Get(root,"assets"))) {
                if(Api.Str(Api.Get(asset,"name"))!=Installer) continue;
                string url=Api.Str(Api.Get(asset,"browser_download_url")),digest=Api.Str(Api.Get(asset,"digest")); long size;
                string expected="https://github.com/"+Repository+"/releases/download/"+tag+"/"+Installer;
                if(url!=expected || !Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$") || !Int64.TryParse(Api.Str(Api.Get(asset,"size")),out size) || size<1 || size>100*1024*1024) throw new FriendlyException("This release has no verified installer. Try again later.");
                return new ReleaseInfo {Version=version,Url=url,Hash=digest.Substring(7).ToLowerInvariant(),Size=size};
            }
            throw new FriendlyException("The latest release has no Windows installer.");
        }
        static HttpWebRequest Request(string url) {
            var r=(HttpWebRequest)WebRequest.Create(url); r.UserAgent="ValorantShopChecker/"+VersionText; r.Accept="application/vnd.github+json"; r.Timeout=30000; r.ReadWriteTimeout=30000; return r;
        }
        public static ReleaseInfo Fetch() {
            try {
                using(var response=Request("https://api.github.com/repos/"+Repository+"/releases/latest").GetResponse()) using(var stream=response.GetResponseStream()) using(var memory=new MemoryStream()) {
                    byte[] block=new byte[8192]; int n; while((n=stream.Read(block,0,block.Length))>0) { if(memory.Length+n>1024*1024) throw new FriendlyException("Release information is too large."); memory.Write(block,0,n); }
                    return Parse(Api.Json(Encoding.UTF8.GetString(memory.ToArray())));
                }
            } catch(WebException ex) {
                var r=ex.Response as HttpWebResponse; if(r!=null) { var code=r.StatusCode; r.Close(); if(code==HttpStatusCode.NotFound) return null; if(code==HttpStatusCode.Forbidden || (int)code==429) throw new FriendlyException("GitHub's update check limit was reached. Try again later."); }
                throw new FriendlyException("Couldn't check for updates. Check your connection and try again.");
            }
        }
        public static void Verify(string file,ReleaseInfo release) {
            if(new FileInfo(file).Length!=release.Size) throw new FriendlyException("The installer download is incomplete.");
            using(var hash=SHA256.Create()) using(var stream=File.OpenRead(file)) {
                string actual=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
                if(actual!=release.Hash) throw new FriendlyException("The installer failed its security check. Nothing was installed.");
            }
        }
        public static string Download(ReleaseInfo release) {
            string dir=Path.Combine(Path.GetTempPath(),"ValorantShopChecker-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); string file=Path.Combine(dir,Installer);
            try {
                using(var response=Request(release.Url).GetResponse()) using(var stream=response.GetResponseStream()) using(var output=File.Create(file)) {
                    byte[] block=new byte[65536]; int n; long total=0; while((n=stream.Read(block,0,block.Length))>0) { total+=n; if(total>release.Size) throw new FriendlyException("The installer size is unexpected."); output.Write(block,0,n); }
                }
                Verify(file,release); return file;
            } catch { Directory.Delete(dir,true); throw; }
        }
        public static string InstallerArguments { get { return "/SHOPUPDATE=1 /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"; } }
        public static void Launch(string path) { Process.Start(new ProcessStartInfo(path,InstallerArguments) {UseShellExecute=true}); }
        public static int Bootstrap(bool silent) {
            try { var release=Fetch(); if(release==null) return 0; Launch(Download(release)); return 2; }
            catch { return 1; }
        }
    }
}
