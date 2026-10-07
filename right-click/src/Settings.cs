using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MinerURightClick
{
    public sealed class ToolSettings
    {
        public string Token { get; set; }
        public bool ExportMarkdown { get; set; }
        public bool ExportWord { get; set; }
        public ToolSettings() { Token = ""; ExportMarkdown = true; ExportWord = false; }
    }

    public static class SettingsStore
    {
        public static string DataDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinerURightClick"); }
        }
        public static string FilePath { get { return Path.Combine(DataDirectory, "settings.json"); } }
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MinerURightClick.Settings.v1");

        public static ToolSettings Load() { return LoadFrom(FilePath); }

        public static ToolSettings LoadFrom(string filePath)
        {
            ToolSettings settings = new ToolSettings();
            if (!File.Exists(filePath)) return settings;
            Dictionary<string, object> data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(filePath, Encoding.UTF8));
            object value;
            if (data.TryGetValue("exportMarkdown", out value)) settings.ExportMarkdown = Convert.ToBoolean(value);
            if (data.TryGetValue("exportWord", out value)) settings.ExportWord = Convert.ToBoolean(value);
            if (data.TryGetValue("protectedToken", out value) && !String.IsNullOrEmpty(Convert.ToString(value)))
            {
                settings.Token = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Convert.ToString(value)), Entropy, DataProtectionScope.CurrentUser));
            }
            return settings;
        }

        public static void Save(ToolSettings settings) { SaveTo(FilePath, settings); }

        public static void SaveTo(string filePath, ToolSettings settings)
        {
            if (!settings.ExportMarkdown && !settings.ExportWord) throw new InvalidOperationException("请至少选择一种导出格式。");
            string token = NormalizeToken(settings.Token);
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["version"] = 1;
            data["exportMarkdown"] = settings.ExportMarkdown;
            data["exportWord"] = settings.ExportWord;
            data["protectedToken"] = String.IsNullOrEmpty(token) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)));
            string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(data), new UTF8Encoding(false));
                if (File.Exists(filePath)) File.Replace(temporary, filePath, null);
                else File.Move(temporary, filePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            settings.Token = token;
        }

        public static string NormalizeToken(string token)
        {
            string clean = (token ?? "").Trim();
            return clean.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? clean.Substring(7).Trim() : clean;
        }

        public static void ImportToken(string oldSettingsPath)
        {
            ToolSettings settings = Load();
            if (!String.IsNullOrWhiteSpace(settings.Token)) return;
            Dictionary<string, object> old = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(oldSettingsPath, Encoding.UTF8));
            object token;
            if (!old.TryGetValue("token", out token) || String.IsNullOrWhiteSpace(Convert.ToString(token))) throw new InvalidOperationException("没有找到可复制的 Token。");
            settings.Token = Convert.ToString(token);
            Save(settings);
        }
    }
}
