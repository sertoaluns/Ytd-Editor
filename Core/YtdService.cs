using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace YTDEditor.Core
{
    public class YtdTextureEntry
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int MipLevels { get; set; }
        public string FormatStr { get; set; }
        public uint FormatUint { get; set; }
        public ushort Unknown32h { get; set; }
        public bool IsSrgb { get; set; }
        public byte[] RawDdsBytes { get; set; }
        public byte[] RgbaCache { get; set; }

        public bool IsDxt5
        {
            get
            {
                string f = (FormatStr ?? "").ToUpperInvariant();
                return f.Contains("DXT5") || f.Contains("BC3");
            }
        }
    }

    public class TextureUpdatePlan
    {
        public string OriginalName { get; set; }
        public string NewName { get; set; }
        public byte[] ReplacementDdsBytes { get; set; }
        public bool PreserveFormatFlags { get; set; } = true;
    }

    public class YtdService
    {
        private readonly AppConfig _config;
        private readonly DdsCodec _ddsCodec;

        private Assembly _cwAssembly;
        private string _loadedDllPath;
        private Type _ytdFileType;
        private Type _textureType;
        private Type _ddsIoType;
        private Type _jenkHashType;
        private MethodInfo _getDdsFileMethod;
        private MethodInfo _getTextureMethod;
        private MethodInfo _genHashMethod;

        public YtdService(AppConfig config, DdsCodec ddsCodec)
        {
            _config = config;
            _ddsCodec = ddsCodec;

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string dllPath = _config.GetCodeWalkerDllPath();
            if (!string.IsNullOrEmpty(dllPath))
            {
                string dir = Path.GetDirectoryName(dllPath);
                string asmName = new AssemblyName(args.Name).Name + ".dll";
                string candidate = Path.Combine(dir, asmName);
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }
            return null;
        }

        public void EnsureCodeWalkerLoaded()
        {
            string dllPath = _config.GetCodeWalkerDllPath();
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                throw new FileNotFoundException(
                    "CodeWalker.Core.dll bulunamadı!\n" +
                    "Lütfen Üst Menü -> Ayarlar bölümünden CodeWalker klasörünü (CodeWalker.Core.dll içeren klasör) seçin.");
            }

            if (_cwAssembly != null && string.Equals(_loadedDllPath, dllPath, StringComparison.OrdinalIgnoreCase))
                return;

            _cwAssembly = Assembly.LoadFrom(dllPath);
            _loadedDllPath = dllPath;

            _ytdFileType = _cwAssembly.GetType("CodeWalker.GameFiles.YtdFile", true);
            _textureType = _cwAssembly.GetType("CodeWalker.GameFiles.Texture", true);
            _ddsIoType = _cwAssembly.GetType("CodeWalker.Utils.DDSIO", true);
            _jenkHashType = _cwAssembly.GetType("CodeWalker.GameFiles.JenkHash", true);

            _getDdsFileMethod = _ddsIoType.GetMethod("GetDDSFile", new[] { _textureType });
            _getTextureMethod = _ddsIoType.GetMethod("GetTexture", new[] { typeof(byte[]) });
            _genHashMethod = _jenkHashType.GetMethod("GenHash", new[] { typeof(string) });
        }

        public List<YtdTextureEntry> LoadYtd(string ytdPath, bool decodeRgba = true)
        {
            if (!File.Exists(ytdPath))
                throw new FileNotFoundException("YTD dosyası bulunamadı: " + ytdPath);

            EnsureCodeWalkerLoaded();

            byte[] fileBytes = File.ReadAllBytes(ytdPath);
            object ytdObj = Activator.CreateInstance(_ytdFileType);

            MethodInfo loadMethod = _ytdFileType.GetMethod("Load", new[] { typeof(byte[]) });
            if (loadMethod != null)
            {
                loadMethod.Invoke(ytdObj, new object[] { fileBytes });
            }
            else
            {
                // Overload Load(byte[], RpfFileEntry) if older CodeWalker build
                foreach (var m in _ytdFileType.GetMethods())
                {
                    if (m.Name == "Load" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(byte[]))
                    {
                        m.Invoke(ytdObj, new object[] { fileBytes, null });
                        break;
                    }
                }
            }

            var result = new List<YtdTextureEntry>();
            object texDict = _ytdFileType.GetProperty("TextureDict")?.GetValue(ytdObj);
            if (texDict == null) return result;

            object texturesBlock = texDict.GetType().GetProperty("Textures")?.GetValue(texDict);
            if (texturesBlock == null) return result;

            var dataItemsField = texturesBlock.GetType().GetField("data_items");
            var dataItemsProp = texturesBlock.GetType().GetProperty("data_items");
            Array dataItems = (dataItemsField != null ? dataItemsField.GetValue(texturesBlock) : dataItemsProp?.GetValue(texturesBlock)) as Array;
            if (dataItems == null) return result;

            for (int i = 0; i < dataItems.Length; i++)
            {
                object tex = dataItems.GetValue(i);
                if (tex == null) continue;

                byte[] ddsBytes = (byte[])_getDdsFileMethod.Invoke(null, new[] { tex });
                string name = Convert.ToString(_textureType.GetProperty("Name")?.GetValue(tex)) ?? ("tex_" + i);
                int width = Convert.ToInt32(_textureType.GetProperty("Width")?.GetValue(tex) ?? 0);
                int height = Convert.ToInt32(_textureType.GetProperty("Height")?.GetValue(tex) ?? 0);
                int levels = Convert.ToInt32(_textureType.GetProperty("Levels")?.GetValue(tex) ?? 1);
                object fmtObj = _textureType.GetProperty("Format")?.GetValue(tex);
                string fmtStr = fmtObj != null ? fmtObj.ToString() : "DXT5";
                uint fmtUint = 0;
                try { fmtUint = Convert.ToUInt32(fmtObj); } catch { }
                ushort unk32h = 0;
                try { unk32h = Convert.ToUInt16(_textureType.GetProperty("Unknown_32h")?.GetValue(tex) ?? 0); } catch { }

                bool isSrgb = false;
                try
                {
                    var ddsMeta = DdsCodec.ParseDdsHeader(ddsBytes);
                    isSrgb = ddsMeta.IsSrgb;
                    if (width == 0) width = ddsMeta.Width;
                    if (height == 0) height = ddsMeta.Height;
                }
                catch { }

                byte[] rgba = null;
                if (decodeRgba)
                {
                    rgba = _ddsCodec.DecodeDdsToRgba(ddsBytes, out _);
                }

                result.Add(new YtdTextureEntry
                {
                    Index = i,
                    Name = name,
                    Width = width,
                    Height = height,
                    MipLevels = levels,
                    FormatStr = fmtStr,
                    FormatUint = fmtUint,
                    Unknown32h = unk32h,
                    IsSrgb = isSrgb,
                    RawDdsBytes = ddsBytes,
                    RgbaCache = rgba
                });
            }

            return result;
        }

        public void SaveYtd(
            string sourceYtdPath,
            string outputYtdPath,
            List<TextureUpdatePlan> updates,
            bool createBackupIfOverwrite = true)
        {
            EnsureCodeWalkerLoaded();

            string outDir = Path.GetDirectoryName(Path.GetFullPath(outputYtdPath));
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            if (createBackupIfOverwrite &&
                File.Exists(outputYtdPath) &&
                string.Equals(Path.GetFullPath(sourceYtdPath), Path.GetFullPath(outputYtdPath), StringComparison.OrdinalIgnoreCase))
            {
                string bakPath = outputYtdPath + ".bak";
                File.Copy(sourceYtdPath, bakPath, true);
            }

            byte[] srcBytes = File.ReadAllBytes(sourceYtdPath);
            object ytdObj = Activator.CreateInstance(_ytdFileType);

            MethodInfo loadMethod = _ytdFileType.GetMethod("Load", new[] { typeof(byte[]) });
            if (loadMethod != null)
            {
                loadMethod.Invoke(ytdObj, new object[] { srcBytes });
            }
            else
            {
                foreach (var m in _ytdFileType.GetMethods())
                {
                    if (m.Name == "Load" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(byte[]))
                    {
                        m.Invoke(ytdObj, new object[] { srcBytes, null });
                        break;
                    }
                }
            }

            object texDict = _ytdFileType.GetProperty("TextureDict")?.GetValue(ytdObj);
            object texturesBlock = texDict?.GetType().GetProperty("Textures")?.GetValue(texDict);
            var dataItemsField = texturesBlock?.GetType().GetField("data_items");
            var dataItemsProp = texturesBlock?.GetType().GetProperty("data_items");
            Array origItems = (dataItemsField != null ? dataItemsField.GetValue(texturesBlock) : dataItemsProp?.GetValue(texturesBlock)) as Array;

            if (origItems == null || origItems.Length == 0)
                throw new InvalidOperationException("Kaynak YTD içinde doku bulunamadı: " + Path.GetFileName(sourceYtdPath));

            var updateMap = new Dictionary<string, TextureUpdatePlan>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in updates)
                updateMap[u.OriginalName] = u;

            Type genericListType = typeof(List<>).MakeGenericType(_textureType);
            IList finalTexList = (IList)Activator.CreateInstance(genericListType);

            var nameProp = _textureType.GetProperty("Name");
            var hashProp = _textureType.GetProperty("NameHash");
            var unk32Prop = _textureType.GetProperty("Unknown_32h");

            for (int i = 0; i < origItems.Length; i++)
            {
                object origTex = origItems.GetValue(i);
                string origName = Convert.ToString(nameProp?.GetValue(origTex)) ?? "";

                if (updateMap.TryGetValue(origName, out TextureUpdatePlan plan))
                {
                    if (plan.ReplacementDdsBytes != null)
                    {
                        object newTex = _getTextureMethod.Invoke(null, new object[] { plan.ReplacementDdsBytes });
                        string finalName = !string.IsNullOrEmpty(plan.NewName) ? plan.NewName : origName;
                        nameProp?.SetValue(newTex, finalName);
                        if (hashProp != null && _genHashMethod != null)
                        {
                            object hashVal = _genHashMethod.Invoke(null, new object[] { finalName.ToLowerInvariant() });
                            hashProp.SetValue(newTex, hashVal);
                        }
                        if (plan.PreserveFormatFlags && unk32Prop != null)
                        {
                            try
                            {
                                object origUnk = unk32Prop.GetValue(origTex);
                                unk32Prop.SetValue(newTex, origUnk);
                            }
                            catch { }
                        }
                        finalTexList.Add(newTex);
                    }
                    else
                    {
                        // Untouched compressed bytes, only rename if requested!
                        if (!string.IsNullOrEmpty(plan.NewName) && !string.Equals(plan.NewName, origName, StringComparison.Ordinal))
                        {
                            nameProp?.SetValue(origTex, plan.NewName);
                            if (hashProp != null && _genHashMethod != null)
                            {
                                object hashVal = _genHashMethod.Invoke(null, new object[] { plan.NewName.ToLowerInvariant() });
                                hashProp.SetValue(origTex, hashVal);
                            }
                        }
                        finalTexList.Add(origTex);
                    }
                }
                else
                {
                    // Untouched texture: never re-encode!
                    finalTexList.Add(origTex);
                }
            }

            MethodInfo buildMethod = texDict.GetType().GetMethod("BuildFromTextureList");
            buildMethod.Invoke(texDict, new object[] { finalTexList });

            MethodInfo saveMethod = _ytdFileType.GetMethod("Save", Type.EmptyTypes);
            byte[] outYtdBytes = (byte[])saveMethod.Invoke(ytdObj, null);
            File.WriteAllBytes(outputYtdPath, outYtdBytes);
        }
    }
}
