using System;
using System.Collections.Generic;

namespace YTDEditor.Core
{
    public class FillSettings
    {
        public string Method { get; set; } = "telea"; // "telea", "ns", "clone", "solid", "donor"
        public int InpaintRadius { get; set; } = 5;
        public string AlphaMode { get; set; } = "preserve"; // "preserve", "opaque"
        public bool HasCloneSource { get; set; } = false;
        public float CloneSourceU { get; set; } = 0.25f;
        public float CloneSourceV { get; set; } = 0.25f;
        public byte[] DonorRgba { get; set; }
        public int DonorWidth { get; set; }
        public int DonorHeight { get; set; }
        public string DonorVariantLetter { get; set; } = "";
    }

    public class HsbSettings
    {
        public bool Enabled { get; set; } = false;
        public int HueShift { get; set; } = 0;        // -180..+180
        public int SaturationShift { get; set; } = 0; // -100..+100
        public int BrightnessShift { get; set; } = 0; // -100..+100

        public bool IsActive => Enabled && (HueShift != 0 || SaturationShift != 0 || BrightnessShift != 0);
    }

    public static class InpaintEngine
    {
        public static byte[] ApplyFillToRgba(
            byte[] sourceRgba,
            int width,
            int height,
            byte[] rawBinaryMask,
            byte[] dilatedFootprintMask,
            float[] softFeatherAlpha,
            FillSettings settings)
        {
            byte[] result = (byte[])sourceRgba.Clone();
            int numPixels = width * height;

            bool anyMasked = false;
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (dilatedFootprintMask[row + x] > 0)
                    {
                        anyMasked = true;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (!anyMasked)
                return result;

            byte[] filledRgb = new byte[numPixels * 3];
            for (int i = 0; i < numPixels; i++)
            {
                filledRgb[i * 3] = sourceRgba[i * 4];
                filledRgb[i * 3 + 1] = sourceRgba[i * 4 + 1];
                filledRgb[i * 3 + 2] = sourceRgba[i * 4 + 2];
            }

            string method = (settings?.Method ?? "telea").ToLowerInvariant();

            if (method == "transparent")
            {
                // Pure transparent cut-out: fill RGB with surrounding average to avoid color bleeding, alpha blends to 0
                FillSolidSurroundingAverage(filledRgb, width, height, dilatedFootprintMask, minX, minY, maxX, maxY);
            }
            else if (method == "solid")
            {
                FillSolidSurroundingAverage(filledRgb, width, height, dilatedFootprintMask, minX, minY, maxX, maxY);
            }
            else if (method == "clone")
            {
                FillCloneStamp(filledRgb, sourceRgba, width, height, dilatedFootprintMask, minX, minY, maxX, maxY, settings);
            }
            else if (method == "donor" && settings.DonorRgba != null && settings.DonorWidth > 0 && settings.DonorHeight > 0)
            {
                FillDonorWithHistogramMatch(
                    filledRgb,
                    sourceRgba,
                    width,
                    height,
                    dilatedFootprintMask,
                    minX,
                    minY,
                    maxX,
                    maxY,
                    settings.DonorRgba,
                    settings.DonorWidth,
                    settings.DonorHeight);
            }
            else if (method == "ns")
            {
                // Navier-Stokes PDE harmonic diffusion initialized from Fast Marching Telea
                FillTeleaFastMarching(filledRgb, width, height, dilatedFootprintMask, minX, minY, maxX, maxY, Math.Max(3, settings?.InpaintRadius ?? 5));
                SmoothNavierStokesPde(filledRgb, width, height, dilatedFootprintMask, minX, minY, maxX, maxY, 18);
            }
            else
            {
                // Default: Telea Fast Marching Inpaint
                FillTeleaFastMarching(filledRgb, width, height, dilatedFootprintMask, minX, minY, maxX, maxY, Math.Max(3, settings?.InpaintRadius ?? 5));
            }

            bool makeOpaque = string.Equals(settings?.AlphaMode, "opaque", StringComparison.OrdinalIgnoreCase);
            bool makeTransparent = string.Equals(settings?.AlphaMode, "transparent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "transparent", StringComparison.OrdinalIgnoreCase);

            // Blend filledRgb into result using softFeatherAlpha; untouched pixels (w <= 0) remain 100% identical!
            for (int i = 0; i < numPixels; i++)
            {
                float w = softFeatherAlpha[i];
                if (w <= 0f) continue;
                if (w > 1f) w = 1f;
                float invW = 1f - w;

                int idx4 = i * 4;
                int idx3 = i * 3;

                result[idx4] = (byte)Math.Round(sourceRgba[idx4] * invW + filledRgb[idx3] * w);
                result[idx4 + 1] = (byte)Math.Round(sourceRgba[idx4 + 1] * invW + filledRgb[idx3 + 1] * w);
                result[idx4 + 2] = (byte)Math.Round(sourceRgba[idx4 + 2] * invW + filledRgb[idx3 + 2] * w);

                if (makeTransparent)
                {
                    result[idx4 + 3] = (byte)Math.Round(sourceRgba[idx4 + 3] * invW + 0f * w);
                }
                else if (makeOpaque)
                {
                    result[idx4 + 3] = (byte)Math.Round(sourceRgba[idx4 + 3] * invW + 255f * w);
                }
            }

            return result;
        }

        private static void FillTeleaFastMarching(
            byte[] rgb,
            int w,
            int h,
            byte[] mask,
            int minX,
            int minY,
            int maxX,
            int maxY,
            int radius)
        {
            // Expand bounding box slightly for neighborhood sampling
            int bx0 = Math.Max(0, minX - radius - 2);
            int by0 = Math.Max(0, minY - radius - 2);
            int bx1 = Math.Min(w - 1, maxX + radius + 2);
            int by1 = Math.Min(h - 1, maxY + radius + 2);

            int bw = bx1 - bx0 + 1;
            int bh = by1 - by0 + 1;

            // state: 0 = KNOWN (outside mask), 1 = BAND (boundary), 2 = UNKNOWN (inside mask)
            byte[] state = new byte[bw * bh];
            var queue = new Queue<int>();

            for (int ly = 0; ly < bh; ly++)
            {
                int gy = by0 + ly;
                for (int lx = 0; lx < bw; lx++)
                {
                    int gx = bx0 + lx;
                    state[ly * bw + lx] = mask[gy * w + gx] > 0 ? (byte)2 : (byte)0;
                }
            }

            int[] dx4 = { -1, 1, 0, 0 };
            int[] dy4 = { 0, 0, -1, 1 };

            // Seed initial narrow band (UNKNOWN pixels adjacent to KNOWN pixels)
            for (int ly = 0; ly < bh; ly++)
            {
                for (int lx = 0; lx < bw; lx++)
                {
                    int lIdx = ly * bw + lx;
                    if (state[lIdx] != 2) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = lx + dx4[d];
                        int ny = ly + dy4[d];
                        if (nx >= 0 && nx < bw && ny >= 0 && ny < bh && state[ny * bw + nx] == 0)
                        {
                            state[lIdx] = 1;
                            queue.Enqueue(lIdx);
                            break;
                        }
                    }
                }
            }

            int r2 = radius * radius;
            while (queue.Count > 0)
            {
                int lIdx = queue.Dequeue();
                int lx = lIdx % bw;
                int ly = lIdx / bw;
                int gx = bx0 + lx;
                int gy = by0 + ly;

                double sumR = 0, sumG = 0, sumB = 0, sumW = 0;

                for (int ky = -radius; ky <= radius; ky++)
                {
                    int nly = ly + ky;
                    if (nly < 0 || nly >= bh) continue;
                    int ngy = by0 + nly;
                    for (int kx = -radius; kx <= radius; kx++)
                    {
                        int d2 = kx * kx + ky * ky;
                        if (d2 == 0 || d2 > r2) continue;
                        int nlx = lx + kx;
                        if (nlx < 0 || nlx >= bw) continue;

                        if (state[nly * bw + nlx] == 0)
                        {
                            int ngx = bx0 + nlx;
                            double weight = 1.0 / (d2 * Math.Sqrt(d2));
                            int p3 = (ngy * w + ngx) * 3;
                            sumR += rgb[p3] * weight;
                            sumG += rgb[p3 + 1] * weight;
                            sumB += rgb[p3 + 2] * weight;
                            sumW += weight;
                        }
                    }
                }

                if (sumW > 0)
                {
                    int dst3 = (gy * w + gx) * 3;
                    rgb[dst3] = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(sumR / sumW)));
                    rgb[dst3 + 1] = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(sumG / sumW)));
                    rgb[dst3 + 2] = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(sumB / sumW)));
                }

                state[lIdx] = 0; // Mark KNOWN

                for (int d = 0; d < 4; d++)
                {
                    int nx = lx + dx4[d];
                    int ny = ly + dy4[d];
                    if (nx >= 0 && nx < bw && ny >= 0 && ny < bh)
                    {
                        int nIdx = ny * bw + nx;
                        if (state[nIdx] == 2)
                        {
                            state[nIdx] = 1;
                            queue.Enqueue(nIdx);
                        }
                    }
                }
            }

            // Gentle harmonic relaxation pass inside mask for ultra-smooth gradients
            SmoothNavierStokesPde(rgb, w, h, mask, minX, minY, maxX, maxY, 6);
        }

        private static void SmoothNavierStokesPde(
            byte[] rgb,
            int w,
            int h,
            byte[] mask,
            int minX,
            int minY,
            int maxX,
            int maxY,
            int iterations)
        {
            for (int iter = 0; iter < iterations; iter++)
            {
                for (int y = Math.Max(1, minY); y <= Math.Min(h - 2, maxY); y++)
                {
                    int row = y * w;
                    for (int x = Math.Max(1, minX); x <= Math.Min(w - 2, maxX); x++)
                    {
                        int idx = row + x;
                        if (mask[idx] == 0) continue;

                        int p = idx * 3;
                        int pL = (idx - 1) * 3;
                        int pR = (idx + 1) * 3;
                        int pU = (idx - w) * 3;
                        int pD = (idx + w) * 3;

                        for (int c = 0; c < 3; c++)
                        {
                            int avg = (rgb[pL + c] + rgb[pR + c] + rgb[pU + c] + rgb[pD + c] + 2) >> 2;
                            rgb[p + c] = (byte)avg;
                        }
                    }
                }
            }
        }

        private static void FillSolidSurroundingAverage(
            byte[] rgb,
            int w,
            int h,
            byte[] mask,
            int minX,
            int minY,
            int maxX,
            int maxY)
        {
            byte[] outer = GroupMaskState.DilateMaskDisk(mask, w, h, 7);
            long sumR = 0, sumG = 0, sumB = 0, count = 0;

            int y0 = Math.Max(0, minY - 8);
            int y1 = Math.Min(h - 1, maxY + 8);
            int x0 = Math.Max(0, minX - 8);
            int x1 = Math.Min(w - 1, maxX + 8);

            for (int y = y0; y <= y1; y++)
            {
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    int idx = row + x;
                    if (outer[idx] > 0 && mask[idx] == 0)
                    {
                        int p3 = idx * 3;
                        sumR += rgb[p3];
                        sumG += rgb[p3 + 1];
                        sumB += rgb[p3 + 2];
                        count++;
                    }
                }
            }

            byte meanR = count > 0 ? (byte)(sumR / count) : (byte)128;
            byte meanG = count > 0 ? (byte)(sumG / count) : (byte)128;
            byte meanB = count > 0 ? (byte)(sumB / count) : (byte)128;

            for (int y = minY; y <= maxY; y++)
            {
                int row = y * w;
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = row + x;
                    if (mask[idx] > 0)
                    {
                        int p3 = idx * 3;
                        rgb[p3] = meanR;
                        rgb[p3 + 1] = meanG;
                        rgb[p3 + 2] = meanB;
                    }
                }
            }
        }

        private static void FillCloneStamp(
            byte[] rgb,
            byte[] sourceRgba,
            int w,
            int h,
            byte[] mask,
            int minX,
            int minY,
            int maxX,
            int maxY,
            FillSettings settings)
        {
            int maskCx = (minX + maxX) / 2;
            int maskCy = (minY + maxY) / 2;

            int srcCx, srcCy;
            if (settings != null && settings.HasCloneSource)
            {
                srcCx = (int)Math.Round(settings.CloneSourceU * (w - 1));
                srcCy = (int)Math.Round(settings.CloneSourceV * (h - 1));
            }
            else
            {
                int bw = maxX - minX + 1;
                srcCx = Math.Max(0, Math.Min(w - 1, maskCx - bw - 12));
                srcCy = maskCy;
            }

            int dx = srcCx - maskCx;
            int dy = srcCy - maskCy;

            for (int y = minY; y <= maxY; y++)
            {
                int row = y * w;
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = row + x;
                    if (mask[idx] == 0) continue;

                    int sx = Math.Max(0, Math.Min(w - 1, x + dx));
                    int sy = Math.Max(0, Math.Min(h - 1, y + dy));
                    int src4 = (sy * w + sx) * 4;
                    int dst3 = idx * 3;

                    rgb[dst3] = sourceRgba[src4];
                    rgb[dst3 + 1] = sourceRgba[src4 + 1];
                    rgb[dst3 + 2] = sourceRgba[src4 + 2];
                }
            }
        }

        private static void FillDonorWithHistogramMatch(
            byte[] rgb,
            byte[] targetRgba,
            int w,
            int h,
            byte[] mask,
            int minX,
            int minY,
            int maxX,
            int maxY,
            byte[] donorRgba,
            int donorW,
            int donorH)
        {
            byte[] outer = GroupMaskState.DilateMaskDisk(mask, w, h, 9);

            int y0 = Math.Max(0, minY - 10);
            int y1 = Math.Min(h - 1, maxY + 10);
            int x0 = Math.Max(0, minX - 10);
            int x1 = Math.Min(w - 1, maxX + 10);

            var donorRing = new List<byte>[3] { new List<byte>(), new List<byte>(), new List<byte>() };
            var targetRing = new List<byte>[3] { new List<byte>(), new List<byte>(), new List<byte>() };

            for (int y = y0; y <= y1; y++)
            {
                int dy = Math.Min(donorH - 1, y * donorH / h);
                for (int x = x0; x <= x1; x++)
                {
                    int idx = y * w + x;
                    if (outer[idx] > 0 && mask[idx] == 0)
                    {
                        int dx = Math.Min(donorW - 1, x * donorW / w);
                        int d4 = (dy * donorW + dx) * 4;
                        int t4 = idx * 4;
                        for (int c = 0; c < 3; c++)
                        {
                            donorRing[c].Add(donorRgba[d4 + c]);
                            targetRing[c].Add(targetRgba[t4 + c]);
                        }
                    }
                }
            }

            // Build per-channel histogram matching LUT (256 entries)
            byte[][] lut = new byte[3][];
            for (int c = 0; c < 3; c++)
            {
                lut[c] = BuildHistogramMatchingLut(donorRing[c], targetRing[c]);
            }

            for (int y = minY; y <= maxY; y++)
            {
                int dy = Math.Min(donorH - 1, y * donorH / h);
                int row = y * w;
                for (int x = minX; x <= maxX; x++)
                {
                    int idx = row + x;
                    if (mask[idx] == 0) continue;

                    int dx = Math.Min(donorW - 1, x * donorW / w);
                    int d4 = (dy * donorW + dx) * 4;
                    int dst3 = idx * 3;

                    rgb[dst3] = lut[0][donorRgba[d4]];
                    rgb[dst3 + 1] = lut[1][donorRgba[d4 + 1]];
                    rgb[dst3 + 2] = lut[2][donorRgba[d4 + 2]];
                }
            }
        }

        private static byte[] BuildHistogramMatchingLut(List<byte> srcSamples, List<byte> dstSamples)
        {
            byte[] lut = new byte[256];
            if (srcSamples.Count < 8 || dstSamples.Count < 8)
            {
                for (int i = 0; i < 256; i++) lut[i] = (byte)i;
                return lut;
            }

            srcSamples.Sort();
            dstSamples.Sort();

            double sMean = 0, dMean = 0;
            for (int i = 0; i < srcSamples.Count; i++) sMean += srcSamples[i];
            for (int i = 0; i < dstSamples.Count; i++) dMean += dstSamples[i];
            sMean /= srcSamples.Count;
            dMean /= dstSamples.Count;

            double sVar = 0, dVar = 0;
            for (int i = 0; i < srcSamples.Count; i++) { double d = srcSamples[i] - sMean; sVar += d * d; }
            for (int i = 0; i < dstSamples.Count; i++) { double d = dstSamples[i] - dMean; dVar += d * d; }
            double sStd = Math.Sqrt(sVar / srcSamples.Count) + 1e-4;
            double dStd = Math.Sqrt(dVar / dstSamples.Count) + 1e-4;

            int[] srcCdf = new int[256];
            foreach (byte b in srcSamples) srcCdf[b]++;
            for (int i = 1; i < 256; i++) srcCdf[i] += srcCdf[i - 1];

            int nSrc = srcSamples.Count;
            int nDst = dstSamples.Count;

            for (int v = 0; v < 256; v++)
            {
                double q = (double)srcCdf[v] / nSrc;
                int dstIdx = Math.Min(nDst - 1, Math.Max(0, (int)Math.Round(q * (nDst - 1))));
                double quantileVal = dstSamples[dstIdx];
                double linearVal = (v - sMean) * (dStd / sStd) + dMean;
                int blended = (int)Math.Round(0.7 * quantileVal + 0.3 * linearVal);
                lut[v] = (byte)Math.Min(255, Math.Max(0, blended));
            }
            return lut;
        }

        public static byte[] ApplyHsbAdjustment(byte[] rgba, int width, int height, HsbSettings hsb)
        {
            if (hsb == null || !hsb.IsActive)
                return (byte[])rgba.Clone();

            byte[] outRgba = (byte[])rgba.Clone();
            int len = width * height;
            float hueDelta = hsb.HueShift;
            float satFactor = 1f + (hsb.SaturationShift / 100f);
            float valOffset = (hsb.BrightnessShift / 100f) * 255f;

            for (int i = 0; i < len; i++)
            {
                int idx = i * 4;
                float r = outRgba[idx];
                float g = outRgba[idx + 1];
                float b = outRgba[idx + 2];
                // Strictly preserve outRgba[idx + 3] (Alpha)!

                RgbToHsv(r, g, b, out float h, out float s, out float v);
                h = (h + hueDelta) % 360f;
                if (h < 0f) h += 360f;
                s = Math.Min(1f, Math.Max(0f, s * satFactor));
                v = Math.Min(255f, Math.Max(0f, v + valOffset));

                HsvToRgb(h, s, v, out byte nr, out byte ng, out byte nb);
                outRgba[idx] = nr;
                outRgba[idx + 1] = ng;
                outRgba[idx + 2] = nb;
            }
            return outRgba;
        }

        private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;
            v = max;
            s = max <= 1e-4f ? 0f : (delta / max);

            if (delta <= 1e-4f)
            {
                h = 0f;
            }
            else if (max == r)
            {
                h = 60f * (((g - b) / delta) % 6f);
            }
            else if (max == g)
            {
                h = 60f * (((b - r) / delta) + 2f);
            }
            else
            {
                h = 60f * (((r - g) / delta) + 4f);
            }
            if (h < 0f) h += 360f;
        }

        private static void HsvToRgb(float h, float s, float v, out byte r, out byte g, out byte b)
        {
            float c = v * s;
            float x = c * (1f - Math.Abs(((h / 60f) % 2f) - 1f));
            float m = v - c;

            float rf, gf, bf;
            if (h < 60f) { rf = c; gf = x; bf = 0; }
            else if (h < 120f) { rf = x; gf = c; bf = 0; }
            else if (h < 180f) { rf = 0; gf = c; bf = x; }
            else if (h < 240f) { rf = 0; gf = x; bf = c; }
            else if (h < 300f) { rf = x; gf = 0; bf = c; }
            else { rf = c; gf = 0; bf = x; }

            r = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(rf + m)));
            g = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(gf + m)));
            b = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(bf + m)));
        }
    }
}
