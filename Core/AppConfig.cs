using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace YTDEditor.Core
{
    public class AppConfig
    {
        public string CodeWalkerDir { get; set; } = "";
        public string DdsToolPath { get; set; } = "";
        public string DdsToolType { get; set; } = "auto";
        public bool OverwriteOriginal { get; set; } = false;
        public bool CreateBackupOnOverwrite { get; set; } = true;
        public string OutputSubfolderName { get; set; } = "output";
        public bool BlockLevelDxt5Patch { get; set; } = true;
        public int DefaultDilate { get; set; } = 0;
        public int DefaultFeather { get; set; } = 0;
        public int MaskColorR { get; set; } = 255;
        public int MaskColorG { get; set; } = 50;
        public int MaskColorB { get; set; } = 90;
        public float MaskOpacity { get; set; } = 0.48f;
        public string LastOpenedFolder { get; set; } = "";
        public bool ApplyToNormalSpecDefault { get; set; } = false;
        public int NormGridResolution { get; set; } = 1024;
        public string Language { get; set; } = "tr"; // "tr", "en", "de"

        [ScriptIgnore]
        public string BaseDir { get; set; }

        [ScriptIgnore]
        public string ConfigPath { get; set; }

        [ScriptIgnore]
        public string TemplatesDir { get; set; }

        public static AppConfig LoadOrCreate()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(baseDir, "config.json");
            string templatesDir = Path.Combine(baseDir, "templates");
            Directory.CreateDirectory(templatesDir);

            AppConfig cfg = new AppConfig();
            if (File.Exists(configPath))
            {
                try
                {
                    var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    string json = File.ReadAllText(configPath);
                    var loaded = ser.Deserialize<AppConfig>(json);
                    if (loaded != null)
                        cfg = loaded;
                }
                catch
                {
                }
            }

            cfg.BaseDir = baseDir;
            cfg.ConfigPath = configPath;
            cfg.TemplatesDir = templatesDir;
            cfg.AutoDetectTools();
            return cfg;
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string json = ser.Serialize(this);
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
            }
        }

        public void AutoDetectTools()
        {
            bool changed = false;

            if (string.IsNullOrWhiteSpace(CodeWalkerDir) || !File.Exists(GetCodeWalkerDllPath()))
            {
                string[] candidates = new[]
                {
                    BaseDir,
                    Path.Combine(BaseDir, "CodeWalker"),
                    Path.Combine(BaseDir, "tools", "CodeWalker"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CodeWalker"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CodeWalker"),
                    @"C:\CodeWalker",
                    @"C:\Program Files\CodeWalker"
                };

                foreach (string c in candidates)
                {
                    string dll = Path.Combine(c, "CodeWalker.Core.dll");
                    if (File.Exists(dll))
                    {
                        CodeWalkerDir = c;
                        changed = true;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(DdsToolPath) || !File.Exists(DdsToolPath))
            {
                string[] toolCandidates = new[]
                {
                    Path.Combine(BaseDir, "texconv.exe"),
                    Path.Combine(BaseDir, "tools", "texconv.exe"),
                    Path.Combine(BaseDir, "CompressonatorCLI.exe"),
                    Path.Combine(BaseDir, "tools", "CompressonatorCLI.exe"),
                    @"C:\Program Files\Compressonator\bin\CLI\CompressonatorCLI.exe"
                };

                foreach (string tc in toolCandidates)
                {
                    if (File.Exists(tc))
                    {
                        DdsToolPath = tc;
                        changed = true;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(DdsToolPath))
                {
                    string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                    foreach (string dir in pathEnv.Split(Path.PathSeparator))
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(dir)) continue;
                            string t1 = Path.Combine(dir.Trim(), "texconv.exe");
                            if (File.Exists(t1)) { DdsToolPath = t1; changed = true; break; }
                            string t2 = Path.Combine(dir.Trim(), "CompressonatorCLI.exe");
                            if (File.Exists(t2)) { DdsToolPath = t2; changed = true; break; }
                        }
                        catch { }
                    }
                }
            }

            if (changed)
                Save();
        }

        public string GetCodeWalkerDllPath()
        {
            if (string.IsNullOrWhiteSpace(CodeWalkerDir))
                return "";
            if (File.Exists(CodeWalkerDir) && CodeWalkerDir.EndsWith("CodeWalker.Core.dll", StringComparison.OrdinalIgnoreCase))
                return CodeWalkerDir;
            string p = Path.Combine(CodeWalkerDir, "CodeWalker.Core.dll");
            return File.Exists(p) ? p : "";
        }

        public string GetResolvedDdsToolType()
        {
            if (string.IsNullOrWhiteSpace(DdsToolPath) || !File.Exists(DdsToolPath))
                return "none";
            if (!string.Equals(DdsToolType, "auto", StringComparison.OrdinalIgnoreCase))
                return DdsToolType.ToLowerInvariant();
            string name = Path.GetFileName(DdsToolPath).ToLowerInvariant();
            return name.Contains("compressonator") ? "compressonator" : "texconv";
        }
    }
}
