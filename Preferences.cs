using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace PersonalShop {
    public class Preferences {
        public bool AutomaticUpdates = true;
        public bool AutomaticInstall = false;
        public bool RefreshOnLaunch = false;
        public string Region = "auto";
        public static string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ValorantShopChecker","settings.json");
        public static Preferences Load() {
            try { var p=new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(SettingsPath)); if(p!=null) { p.Validate(); return p; } } catch {}
            return new Preferences();
        }
        public void Validate() { if(!System.Text.RegularExpressions.Regex.IsMatch(Region??"",@"^(auto|na|eu|ap|kr|pbe)$")) Region="auto"; }
        public void Save() {
            Validate(); Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            string temp=SettingsPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
                if(File.Exists(SettingsPath)) File.Replace(temp,SettingsPath,null); else File.Move(temp,SettingsPath);
            } finally { if(File.Exists(temp)) File.Delete(temp); }
        }
    }
}
