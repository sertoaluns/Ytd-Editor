using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YTDEditor.Core
{
    public class ExpectedTextureSpec
    {
        public string ExpectedName { get; set; }
        public int ExpectedWidth { get; set; }
        public int ExpectedHeight { get; set; }
        public bool WasModified { get; set; }
        public byte[] OriginalRgba { get; set; }
        public byte[] ModifiedFootprintMask { get; set; }
        public bool HsbApplied { get; set; }
    }

    public class TextureVerifyReport
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string FormatStr { get; set; }
        public int MipLevels { get; set; }
        public double UnmaskedMeanRgbDiff { get; set; }
        public double UnmaskedMaxRgbDiff { get; set; }
        public double UnmaskedAlphaDiff { get; set; }
        public string Message { get; set; }
    }

    public class YtdVerificationResult
    {
        public string YtdPath { get; set; }
        public bool Passed { get; set; }
        public string Summary { get; set; }
        public List<TextureVerifyReport> Reports { get; set; } = new List<TextureVerifyReport>();
    }

    public static class Verifier
    {
        public static YtdVerificationResult VerifySavedYtd(
            YtdService ytdService,
            string savedYtdPath,
            List<ExpectedTextureSpec> expectedSpecs,
            double maxAllowedMeanRgbDiff = 1.5)
        {
            var res = new YtdVerificationResult { YtdPath = savedYtdPath };

            if (!File.Exists(savedYtdPath))
            {
                res.Passed = false;
                res.Summary = $"Doğrulama Hatası: Kaydedilen dosya bulunamadı ({Path.GetFileName(savedYtdPath)}).";
                return res;
            }

            List<YtdTextureEntry> reloaded;
            try
            {
                reloaded = ytdService.LoadYtd(savedYtdPath, true);
            }
            catch (Exception ex)
            {
                res.Passed = false;
                res.Summary = $"Doğrulama Hatası: Kaydedilen YTD yeniden okunamadı ({ex.Message}).";
                return res;
            }

            if (reloaded.Count != expectedSpecs.Count)
            {
                res.Passed = false;
                res.Summary = $"Doğrulama Hatası: Texture sayısı uyuşmuyor! Beklenen: {expectedSpecs.Count}, Okunan: {reloaded.Count}";
                return res;
            }

            var map = new Dictionary<string, YtdTextureEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in reloaded)
                map[e.Name] = e;

            bool allPassed = true;
            var lines = new List<string>();

            foreach (var spec in expectedSpecs)
            {
                if (!map.TryGetValue(spec.ExpectedName, out YtdTextureEntry entry))
                {
                    allPassed = false;
                    string msgMiss = $"[BAŞARISIZ] '{spec.ExpectedName}' dokusu kaydedilen YTD içinde bulunamadı!";
                    lines.Add(msgMiss);
                    res.Reports.Add(new TextureVerifyReport { Name = spec.ExpectedName, Passed = false, Message = msgMiss });
                    continue;
                }

                bool sizeOk = (entry.Width == spec.ExpectedWidth) && (entry.Height == spec.ExpectedHeight);
                bool fmtOk = spec.WasModified ? (entry.IsDxt5 && entry.MipLevels >= 1) : true;

                double meanRgbDiff = 0.0;
                double maxRgbDiff = 0.0;
                double meanAlphaDiff = 0.0;

                if (sizeOk && spec.OriginalRgba != null && entry.RgbaCache != null)
                {
                    int numPixels = entry.Width * entry.Height;
                    long sumRgb = 0;
                    long sumA = 0;
                    long count = 0;
                    int maxDiff = 0;

                    for (int i = 0; i < numPixels; i++)
                    {
                        bool isUnmasked = true;
                        if (spec.WasModified && spec.ModifiedFootprintMask != null && i < spec.ModifiedFootprintMask.Length)
                        {
                            isUnmasked = (spec.ModifiedFootprintMask[i] == 0);
                        }

                        if (!isUnmasked) continue;

                        int p4 = i * 4;
                        int dr = Math.Abs(spec.OriginalRgba[p4] - entry.RgbaCache[p4]);
                        int dg = Math.Abs(spec.OriginalRgba[p4 + 1] - entry.RgbaCache[p4 + 1]);
                        int db = Math.Abs(spec.OriginalRgba[p4 + 2] - entry.RgbaCache[p4 + 2]);
                        int da = Math.Abs(spec.OriginalRgba[p4 + 3] - entry.RgbaCache[p4 + 3]);

                        sumRgb += (dr + dg + db);
                        sumA += da;
                        count++;

                        int m = Math.Max(dr, Math.Max(dg, db));
                        if (m > maxDiff) maxDiff = m;
                    }

                    if (count > 0)
                    {
                        meanRgbDiff = (double)sumRgb / (count * 3.0);
                        maxRgbDiff = maxDiff;
                        meanAlphaDiff = (double)sumA / count;
                    }
                }

                bool rgbOk = spec.HsbApplied || (meanRgbDiff <= maxAllowedMeanRgbDiff);
                bool passed = sizeOk && fmtOk && rgbOk;
                if (!passed) allPassed = false;

                string tag = passed ? "BAŞARILI" : "BAŞARISIZ";
                string msg = $"[{tag}] Doku: {entry.Name} | Boyut: {entry.Width}x{entry.Height} | Format: {entry.FormatStr} (Mip:{entry.MipLevels}) | Değişmeyen Piksel Ort. RGB Farkı: {meanRgbDiff:F4} (Maks: {maxRgbDiff:F0}, Alfa Farkı: {meanAlphaDiff:F4})";
                lines.Add(msg);

                res.Reports.Add(new TextureVerifyReport
                {
                    Name = entry.Name,
                    Passed = passed,
                    Width = entry.Width,
                    Height = entry.Height,
                    FormatStr = entry.FormatStr,
                    MipLevels = entry.MipLevels,
                    UnmaskedMeanRgbDiff = meanRgbDiff,
                    UnmaskedMaxRgbDiff = maxRgbDiff,
                    UnmaskedAlphaDiff = meanAlphaDiff,
                    Message = msg
                });
            }

            res.Passed = allPassed;
            string head = allPassed
                ? $"YTD Doğrulama Başarılı ({Path.GetFileName(savedYtdPath)}): {res.Reports.Count} doku doğrulandı."
                : $"YTD Doğrulama BAŞARISIZ ({Path.GetFileName(savedYtdPath)})!";
            res.Summary = head + Environment.NewLine + string.Join(Environment.NewLine, lines);
            return res;
        }

        public static bool RunSelfDiagnostics(AppConfig config, DdsCodec ddsCodec, YtdService ytdService, out List<string> logs)
        {
            logs = new List<string>();
            bool allCoreOk = true;

            logs.Add("=== 1. Normalize Maske ve Grup / Alt-Varyant Taşıma Kontrolü ===");
            try
            {
                var m000 = new GroupMaskState("jbib_diff_000", 512, 2, 2);
                var m001 = new GroupMaskState("jbib_diff_001", 512, 2, 2);
                m000.PaintRectNormalized(0.25f, 0.25f, 0.50f, 0.50f, 1024, 1024, "a", false);

                // Shared group move
                m000.MoveMaskByPixels(20, 10, 1024, 1024, "a", false);
                byte[] maskA = m000.GetVariantBinaryMask("a", 1024, 1024);
                byte[] maskB2048 = m000.GetVariantBinaryMask("b", 2048, 2048);

                // Alt move only on variant b
                m000.MoveMaskByPixels(-10, -5, 1024, 1024, "b", true);
                var vtA = m000.GetVariantTransform("a");
                var vtB = m000.GetVariantTransform("b");

                if (!maskA.Any(b => b > 0) || !maskB2048.Any(b => b > 0))
                    throw new Exception("Normalize maske ölçekleme başarısız.");
                if (m001.HasAnyMask())
                    throw new Exception("Farklı numaralı gruba maske sızdı!");
                if (!vtA.IsIdentity() || vtB.IsIdentity())
                    throw new Exception("Alt ile taşıma sadece aktif varyantı etkilemeli!");

                logs.Add("[OK] Normalize maske (1024 -> 2048 ölçekleme), grup içi ortak taşıma ve Alt-tekil varyant taşıma doğrulandı.");
                logs.Add("[OK] Farklı numaralı slot gruplarının (000 vs 001) maske izolasyonu doğrulandı.");
            }
            catch (Exception ex)
            {
                allCoreOk = false;
                logs.Add("[HATA] Maske motoru testi başarısız: " + ex.Message);
            }

            logs.Add("");
            logs.Add("=== 2. Doldurma (Telea, Navier-Stokes, Klon, Düz Renk, Donor Histogram) ve Alfa Koruma Kontrolü ===");
            try
            {
                int w = 64, h = 64;
                byte[] synth = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++)
                {
                    synth[i * 4] = 45;
                    synth[i * 4 + 1] = 120;
                    synth[i * 4 + 2] = 200;
                    synth[i * 4 + 3] = 180; // Decal alpha
                }

                byte[] rawM = new byte[w * h];
                float[] softM = new float[w * h];
                for (int y = 20; y < 40; y++)
                {
                    for (int x = 20; x < 40; x++)
                    {
                        int idx = y * w + x;
                        rawM[idx] = 255;
                        softM[idx] = 1f;
                        synth[idx * 4] = 250;
                        synth[idx * 4 + 1] = 20;
                        synth[idx * 4 + 2] = 20;
                    }
                }

                string[] methods = { "telea", "ns", "solid", "clone", "donor" };
                foreach (string m in methods)
                {
                    var fs = new FillSettings
                    {
                        Method = m,
                        AlphaMode = "preserve",
                        DonorRgba = synth,
                        DonorWidth = w,
                        DonorHeight = h
                    };
                    byte[] outRgba = InpaintEngine.ApplyFillToRgba(synth, w, h, rawM, rawM, softM, fs);
                    for (int i = 0; i < w * h; i++)
                    {
                        if (rawM[i] == 0)
                        {
                            if (outRgba[i * 4] != synth[i * 4] ||
                                outRgba[i * 4 + 1] != synth[i * 4 + 1] ||
                                outRgba[i * 4 + 2] != synth[i * 4 + 2])
                            {
                                throw new Exception($"{m} yöntemi maske dışı pikselleri değiştirdi!");
                            }
                        }
                        if (outRgba[i * 4 + 3] != 180)
                            throw new Exception($"{m} yöntemi alfa kanalını bozdu!");
                    }
                }

                byte[] hsbOut = InpaintEngine.ApplyHsbAdjustment(synth, w, h, new HsbSettings { Enabled = true, HueShift = 30, SaturationShift = 10, BrightnessShift = 5 });
                for (int i = 0; i < w * h; i++)
                {
                    if (hsbOut[i * 4 + 3] != 180)
                        throw new Exception("HSB renk ayarı alfa kanalını değiştirdi!");
                }

                logs.Add("[OK] Telea, Navier-Stokes, Klonlama, Düz Renk, Donor Histogram ve HSB alfa koruma testleri geçti (Maske dışı fark: 0.0000).");
            }
            catch (Exception ex)
            {
                allCoreOk = false;
                logs.Add("[HATA] Doldurma/Alfa testi başarısız: " + ex.Message);
            }

            logs.Add("");
            logs.Add("=== 3. 4x4 DXT5 (BC3) Blok Seviyesi Kayıpsız Koruma Kontrolü ===");
            try
            {
                int w = 64, h = 64;
                int numBlocks = (w / 4) * (h / 4);
                byte[] hdr = DdsCodec.BuildStandardDxt5Header(w, h, 1);
                byte[] origDds = new byte[128 + numBlocks * 16];
                byte[] newDds = new byte[128 + numBlocks * 16];
                Buffer.BlockCopy(hdr, 0, origDds, 0, 128);
                Buffer.BlockCopy(hdr, 0, newDds, 0, 128);

                var rng = new Random(42);
                byte[] b1 = new byte[numBlocks * 16];
                byte[] b2 = new byte[numBlocks * 16];
                rng.NextBytes(b1);
                rng.NextBytes(b2);
                Buffer.BlockCopy(b1, 0, origDds, 128, b1.Length);
                Buffer.BlockCopy(b2, 0, newDds, 128, b2.Length);

                byte[] mask64 = new byte[w * h];
                for (int y = 16; y < 24; y++)
                    for (int x = 16; x < 24; x++)
                        mask64[y * w + x] = 255; // 2x2 = 4 blocks

                byte[] patchedDds = DdsCodec.PatchDxt5BlocksWithMask(origDds, newDds, mask64, w, h, out int touched, out int total);
                byte[] decOrig = DdsCodec.DecodeDxt5ToRgba(origDds, w, h, 128);
                byte[] decPatched = DdsCodec.DecodeDxt5ToRgba(patchedDds, w, h, 128);

                double diffSum = 0;
                int unmaskedCount = 0;
                for (int i = 0; i < w * h; i++)
                {
                    if (mask64[i] == 0)
                    {
                        diffSum += Math.Abs(decOrig[i * 4] - decPatched[i * 4]) +
                                   Math.Abs(decOrig[i * 4 + 1] - decPatched[i * 4 + 1]) +
                                   Math.Abs(decOrig[i * 4 + 2] - decPatched[i * 4 + 2]);
                        unmaskedCount++;
                    }
                }
                double meanDiff = diffSum / (unmaskedCount * 3.0);
                if (touched != 4 || total != 256 || meanDiff > 0.0)
                    throw new Exception($"Blok koruma hatası: touched={touched}, meanDiff={meanDiff}");

                logs.Add($"[OK] 4x4 DXT5 Blok Koruma: Toplam {total} bloktan sadece maskeye değen {touched} blok güncellendi, kalan {total - touched} blok orijinal baytlarıyla korundu (Ort. RGB Farkı: {meanDiff:F4}).");
            }
            catch (Exception ex)
            {
                allCoreOk = false;
                logs.Add("[HATA] DXT5 blok koruma testi başarısız: " + ex.Message);
            }

            logs.Add("");
            logs.Add("=== 4. Harici Araçlar (CodeWalker.Core.dll & DDS Encoder) Kontrolü ===");
            string dllPath = config.GetCodeWalkerDllPath();
            if (!string.IsNullOrEmpty(dllPath) && File.Exists(dllPath))
            {
                logs.Add("[OK] CodeWalker.Core.dll bulundu: " + dllPath);
                try
                {
                    ytdService.EnsureCodeWalkerLoaded();
                    logs.Add("[OK] CodeWalker.Core.dll .NET meclisi başarıyla yüklendi.");
                }
                catch (Exception ex)
                {
                    logs.Add("[UYARI] CodeWalker.Core.dll yükleme uyarısı: " + ex.Message);
                }
            }
            else
            {
                logs.Add("[UYARI] CodeWalker.Core.dll henüz seçilmedi (Üst Menü -> Ayarlar'dan CodeWalker klasörünü seçebilirsiniz).");
            }

            string toolType = config.GetResolvedDdsToolType();
            if (toolType != "none" && File.Exists(config.DdsToolPath))
                logs.Add($"[OK] DDS Encoder bulundu ({toolType}): {config.DdsToolPath}");
            else
                logs.Add("[UYARI] texconv.exe veya CompressonatorCLI.exe henüz seçilmedi (Ayarlar'dan seçebilirsiniz).");

            return allCoreOk;
        }
    }
}
