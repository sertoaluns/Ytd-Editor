using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace YTDEditor.Core
{
    public class MaskTemplateDto
    {
        public string name { get; set; }
        public string created_at { get; set; }
        public int[] reference_size { get; set; }
        public int grid_resolution { get; set; }
        public int dilate { get; set; }
        public int feather { get; set; }
        public string normalized_mask_deflate_b64 { get; set; }
        public string thumbnail_png_b64 { get; set; }
    }

    public class MaskTemplateInfo
    {
        public string FilePath { get; set; }
        public MaskTemplateDto Dto { get; set; }

        public string Name => Dto?.name ?? Path.GetFileNameWithoutExtension(FilePath);
        public string CreatedAt => Dto?.created_at ?? "";
        public int RefWidth => (Dto?.reference_size != null && Dto.reference_size.Length >= 2) ? Dto.reference_size[0] : 1024;
        public int RefHeight => (Dto?.reference_size != null && Dto.reference_size.Length >= 2) ? Dto.reference_size[1] : 1024;
        public int Dilate => Dto?.dilate ?? 2;
        public int Feather => Dto?.feather ?? 2;
    }

    public class TemplateManager
    {
        private readonly string _templatesDir;

        public TemplateManager(string templatesDir)
        {
            _templatesDir = templatesDir;
            Directory.CreateDirectory(_templatesDir);
        }

        private static string Slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "sablon";
            string s = name
                .Replace('ç', 'c').Replace('Ç', 'C')
                .Replace('ğ', 'g').Replace('Ğ', 'G')
                .Replace('ı', 'i').Replace('İ', 'I')
                .Replace('ö', 'o').Replace('Ö', 'O')
                .Replace('ş', 's').Replace('Ş', 'S')
                .Replace('ü', 'u').Replace('Ü', 'U')
                .Replace(' ', '_');
            s = Regex.Replace(s, @"[^a-zA-Z0-9_\-]", "");
            return string.IsNullOrWhiteSpace(s) ? "sablon" : s;
        }

        public List<MaskTemplateInfo> ListTemplates(string searchQuery = "")
        {
            Directory.CreateDirectory(_templatesDir);
            var list = new List<MaskTemplateInfo>();
            string q = (searchQuery ?? "").Trim().ToLowerInvariant();
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

            foreach (string file in Directory.GetFiles(_templatesDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var dto = ser.Deserialize<MaskTemplateDto>(json);
                    if (dto == null || string.IsNullOrEmpty(dto.normalized_mask_deflate_b64))
                        continue;
                    if (!string.IsNullOrEmpty(q) && !(dto.name ?? "").ToLowerInvariant().Contains(q))
                        continue;
                    list.Add(new MaskTemplateInfo { FilePath = file, Dto = dto });
                }
                catch { }
            }

            return list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public MaskTemplateInfo SaveTemplate(string name, GroupMaskState maskState, byte[] previewRgba = null, int prevW = 0, int prevH = 0)
        {
            string createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string b64Mask = maskState.ExportBakedGridBase64();
            string thumbB64 = BuildThumbnailBase64(maskState, previewRgba, prevW, prevH);

            var dto = new MaskTemplateDto
            {
                name = name,
                created_at = createdAt,
                reference_size = new[] { maskState.RefWidth, maskState.RefHeight },
                grid_resolution = maskState.GridRes,
                dilate = maskState.DilatePx,
                feather = maskState.FeatherPx,
                normalized_mask_deflate_b64 = b64Mask,
                thumbnail_png_b64 = thumbB64
            };

            string slug = Slugify(name);
            string targetPath = Path.Combine(_templatesDir, slug + ".json");
            int counter = 2;
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

            while (File.Exists(targetPath))
            {
                try
                {
                    var existing = ser.Deserialize<MaskTemplateDto>(File.ReadAllText(targetPath));
                    if (existing != null && string.Equals(existing.name, name, StringComparison.OrdinalIgnoreCase))
                        break;
                }
                catch { }
                targetPath = Path.Combine(_templatesDir, $"{slug}_{counter}.json");
                counter++;
            }

            File.WriteAllText(targetPath, ser.Serialize(dto));
            return new MaskTemplateInfo { FilePath = targetPath, Dto = dto };
        }

        private static string BuildThumbnailBase64(GroupMaskState maskState, byte[] rgba, int w, int h)
        {
            int tw = 72, th = 72;
            using (var bmp = new Bitmap(tw, th, PixelFormat.Format32bppArgb))
            {
                byte[] maskSmall = maskState.GetVariantBinaryMask("a", tw, th);
                for (int y = 0; y < th; y++)
                {
                    for (int x = 0; x < tw; x++)
                    {
                        bool m = maskSmall[y * tw + x] > 0;
                        if (m)
                        {
                            bmp.SetPixel(x, y, Color.FromArgb(255, 255, 55, 95));
                        }
                        else if (rgba != null && w > 0 && h > 0)
                        {
                            int sx = x * w / tw;
                            int sy = y * h / th;
                            int idx = (sy * w + sx) * 4;
                            bmp.SetPixel(x, y, Color.FromArgb(255, rgba[idx], rgba[idx + 1], rgba[idx + 2]));
                        }
                        else
                        {
                            bmp.SetPixel(x, y, Color.FromArgb(255, 32, 36, 46));
                        }
                    }
                }
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        public void ApplyTemplateToMaskState(MaskTemplateInfo info, GroupMaskState targetState)
        {
            if (info?.Dto == null) return;
            targetState.ImportBakedGridBase64(
                info.Dto.normalized_mask_deflate_b64,
                info.Dto.grid_resolution > 0 ? info.Dto.grid_resolution : 1024,
                info.RefWidth,
                info.RefHeight,
                info.Dilate,
                info.Feather);
        }

        public void RenameTemplate(MaskTemplateInfo info, string newName)
        {
            if (info?.Dto == null) return;
            info.Dto.name = newName;
            string slug = Slugify(newName);
            string newPath = Path.Combine(_templatesDir, slug + ".json");
            int c = 2;
            while (File.Exists(newPath) && !string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(info.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                newPath = Path.Combine(_templatesDir, $"{slug}_{c}.json");
                c++;
            }

            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(newPath, ser.Serialize(info.Dto));
            if (!string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(info.FilePath), StringComparison.OrdinalIgnoreCase) && File.Exists(info.FilePath))
                File.Delete(info.FilePath);
            info.FilePath = newPath;
        }

        public void DuplicateTemplate(MaskTemplateInfo info)
        {
            if (info?.Dto == null) return;
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var copyDto = ser.Deserialize<MaskTemplateDto>(ser.Serialize(info.Dto));
            copyDto.name = info.Name + " (Kopya)";
            copyDto.created_at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            string slug = Slugify(copyDto.name);
            string newPath = Path.Combine(_templatesDir, slug + ".json");
            int c = 2;
            while (File.Exists(newPath))
            {
                newPath = Path.Combine(_templatesDir, $"{slug}_{c}.json");
                c++;
            }
            File.WriteAllText(newPath, ser.Serialize(copyDto));
        }

        public void DeleteTemplate(MaskTemplateInfo info)
        {
            if (info != null && File.Exists(info.FilePath))
                File.Delete(info.FilePath);
        }

        public void ExportTemplate(MaskTemplateInfo info, string destPath)
        {
            if (info != null && File.Exists(info.FilePath))
                File.Copy(info.FilePath, destPath, true);
        }

        public void ImportTemplate(string sourceJsonPath)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string json = File.ReadAllText(sourceJsonPath);
            var dto = ser.Deserialize<MaskTemplateDto>(json);
            if (dto == null || string.IsNullOrEmpty(dto.normalized_mask_deflate_b64))
                throw new InvalidDataException("Seçilen JSON dosyası geçerli bir YTD Editor maske şablonu değil.");

            string slug = Slugify(dto.name ?? Path.GetFileNameWithoutExtension(sourceJsonPath));
            string target = Path.Combine(_templatesDir, slug + ".json");
            int c = 2;
            while (File.Exists(target))
            {
                target = Path.Combine(_templatesDir, $"{slug}_{c}.json");
                c++;
            }
            File.WriteAllText(target, ser.Serialize(dto));
        }
    }
}
