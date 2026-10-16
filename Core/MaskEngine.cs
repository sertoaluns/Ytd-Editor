using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;

namespace YTDEditor.Core
{
    public class VariantTransform
    {
        public float OffsetU { get; set; } = 0f;
        public float OffsetV { get; set; } = 0f;
        public float ScaleX { get; set; } = 1f;
        public float ScaleY { get; set; } = 1f;

        public VariantTransform Clone()
        {
            return new VariantTransform
            {
                OffsetU = OffsetU,
                OffsetV = OffsetV,
                ScaleX = ScaleX,
                ScaleY = ScaleY
            };
        }

        public bool IsIdentity()
        {
            return Math.Abs(OffsetU) < 1e-6f &&
                   Math.Abs(OffsetV) < 1e-6f &&
                   Math.Abs(ScaleX - 1f) < 1e-4f &&
                   Math.Abs(ScaleY - 1f) < 1e-4f;
        }
    }

    public class MaskSnapshot
    {
        public byte[] CompressedGrid { get; set; }
        public int GridRes { get; set; }
        public float GroupOffsetU { get; set; }
        public float GroupOffsetV { get; set; }
        public Dictionary<string, VariantTransform> VariantTransforms { get; set; }
    }

    public class AlignmentWarning
    {
        public string VariantLetter { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float AspectRatio { get; set; }
        public bool AspectMismatch { get; set; }
        public bool ResolutionDiffers { get; set; }
        public string Message { get; set; }
    }

    public class GroupMaskState
    {
        public string GroupKey { get; set; }
        public int GridRes { get; private set; }
        public byte[] NormGrid { get; private set; }
        public int RefWidth { get; set; } = 1024;
        public int RefHeight { get; set; } = 1024;

        public float GroupOffsetU { get; set; } = 0f;
        public float GroupOffsetV { get; set; } = 0f;
        public Dictionary<string, VariantTransform> VariantTransforms { get; private set; } =
            new Dictionary<string, VariantTransform>(StringComparer.OrdinalIgnoreCase);

        // Default 0 Dilate & 0 Feather -> Sharp vector-like exact pixel boundary (no gradient transition)
        public int DilatePx { get; set; } = 0;
        public int FeatherPx { get; set; } = 0;

        public bool HasCloneSource { get; set; } = false;
        public float CloneSourceU { get; set; } = 0.25f;
        public float CloneSourceV { get; set; } = 0.25f;

        private readonly List<MaskSnapshot> _undoStack = new List<MaskSnapshot>();
        private readonly List<MaskSnapshot> _redoStack = new List<MaskSnapshot>();
        private const int MaxHistory = 35;

        public GroupMaskState(string groupKey, int gridRes = 1024, int dilatePx = 0, int featherPx = 0)
        {
            GroupKey = groupKey;
            GridRes = gridRes;
            NormGrid = new byte[gridRes * gridRes];
            DilatePx = Math.Max(0, Math.Min(8, dilatePx));
            FeatherPx = Math.Max(0, Math.Min(8, featherPx));
        }

        public VariantTransform GetVariantTransform(string variantLetter)
        {
            string k = (variantLetter ?? "a").ToLowerInvariant();
            if (!VariantTransforms.TryGetValue(k, out VariantTransform vt))
            {
                vt = new VariantTransform();
                VariantTransforms[k] = vt;
            }
            return vt;
        }

        public bool HasAnyMask()
        {
            for (int i = 0; i < NormGrid.Length; i++)
            {
                if (NormGrid[i] > 0) return true;
            }
            return false;
        }

        private static byte[] CompressBytes(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true))
                {
                    ds.Write(raw, 0, raw.Length);
                }
                return ms.ToArray();
            }
        }

        private static byte[] DecompressBytes(byte[] comp, int expectedLen)
        {
            byte[] outBuf = new byte[expectedLen];
            using (var ms = new MemoryStream(comp))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < expectedLen)
                {
                    int n = ds.Read(outBuf, read, expectedLen - read);
                    if (n <= 0) break;
                    read += n;
                }
            }
            return outBuf;
        }

        private MaskSnapshot MakeSnapshot()
        {
            var vtCopy = new Dictionary<string, VariantTransform>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in VariantTransforms)
                vtCopy[kv.Key] = kv.Value.Clone();

            return new MaskSnapshot
            {
                CompressedGrid = CompressBytes(NormGrid),
                GridRes = GridRes,
                GroupOffsetU = GroupOffsetU,
                GroupOffsetV = GroupOffsetV,
                VariantTransforms = vtCopy
            };
        }

        private void RestoreSnapshot(MaskSnapshot snap)
        {
            GridRes = snap.GridRes;
            NormGrid = DecompressBytes(snap.CompressedGrid, GridRes * GridRes);
            GroupOffsetU = snap.GroupOffsetU;
            GroupOffsetV = snap.GroupOffsetV;
            VariantTransforms.Clear();
            foreach (var kv in snap.VariantTransforms)
                VariantTransforms[kv.Key] = kv.Value.Clone();
        }

        public void PushUndo()
        {
            _undoStack.Add(MakeSnapshot());
            if (_undoStack.Count > MaxHistory)
                _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        public bool Undo()
        {
            if (_undoStack.Count == 0) return false;
            _redoStack.Add(MakeSnapshot());
            var snap = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            RestoreSnapshot(snap);
            return true;
        }

        public bool Redo()
        {
            if (_redoStack.Count == 0) return false;
            _undoStack.Add(MakeSnapshot());
            var snap = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            RestoreSnapshot(snap);
            return true;
        }

        public void ClearMask()
        {
            if (HasAnyMask())
                PushUndo();
            Array.Clear(NormGrid, 0, NormGrid.Length);
            GroupOffsetU = 0f;
            GroupOffsetV = 0f;
            VariantTransforms.Clear();
        }

        public void CanvasUvToGridXy(float u, float v, string variantLetter, out int gx, out int gy)
        {
            var vt = GetVariantTransform(variantLetter);
            float sx = Math.Abs(vt.ScaleX) > 1e-4f ? vt.ScaleX : 1f;
            float sy = Math.Abs(vt.ScaleY) > 1e-4f ? vt.ScaleY : 1f;

            float uUnshifted = u - GroupOffsetU - vt.OffsetU;
            float vUnshifted = v - GroupOffsetV - vt.OffsetV;

            float uBase = (uUnshifted - 0.5f) / sx + 0.5f;
            float vBase = (vUnshifted - 0.5f) / sy + 0.5f;

            gx = (int)Math.Round(uBase * (GridRes - 1));
            gy = (int)Math.Round(vBase * (GridRes - 1));
        }

        public void PaintStrokeNormalized(
            float u0, float v0, float u1, float v1,
            float brushPxRadius, int canvasW, int canvasH,
            string variantLetter, bool erase)
        {
            RefWidth = canvasW;
            RefHeight = canvasH;

            CanvasUvToGridXy(u0, v0, variantLetter, out int x0, out int y0);
            CanvasUvToGridXy(u1, v1, variantLetter, out int x1, out int y1);

            var vt = GetVariantTransform(variantLetter);
            float scaleAvg = Math.Max(0.1f, (Math.Abs(vt.ScaleX) + Math.Abs(vt.ScaleY)) * 0.5f);
            float normRadX = (brushPxRadius / Math.Max(1, canvasW)) * GridRes / scaleAvg;
            float normRadY = (brushPxRadius / Math.Max(1, canvasH)) * GridRes / scaleAvg;
            int rad = Math.Max(1, (int)Math.Round((normRadX + normRadY) * 0.5f));

            byte val = erase ? (byte)0 : (byte)255;

            int dx = x1 - x0;
            int dy = y1 - y0;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy)));
            int r2 = rad * rad;

            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                int cx = (int)Math.Round(x0 + dx * t);
                int cy = (int)Math.Round(y0 + dy * t);

                int minY = Math.Max(0, cy - rad);
                int maxY = Math.Min(GridRes - 1, cy + rad);
                int minX = Math.Max(0, cx - rad);
                int maxX = Math.Min(GridRes - 1, cx + rad);

                for (int y = minY; y <= maxY; y++)
                {
                    int py = y - cy;
                    int row = y * GridRes;
                    for (int x = minX; x <= maxX; x++)
                    {
                        int px = x - cx;
                        if (px * px + py * py <= r2)
                            NormGrid[row + x] = val;
                    }
                }
            }
        }

        public void PaintRectNormalized(
            float u0, float v0, float u1, float v1,
            int canvasW, int canvasH, string variantLetter, bool erase)
        {
            RefWidth = canvasW;
            RefHeight = canvasH;

            CanvasUvToGridXy(u0, v0, variantLetter, out int x0, out int y0);
            CanvasUvToGridXy(u1, v1, variantLetter, out int x1, out int y1);

            int xmin = Math.Max(0, Math.Min(GridRes - 1, Math.Min(x0, x1)));
            int xmax = Math.Max(0, Math.Min(GridRes - 1, Math.Max(x0, x1)));
            int ymin = Math.Max(0, Math.Min(GridRes - 1, Math.Min(y0, y1)));
            int ymax = Math.Max(0, Math.Min(GridRes - 1, Math.Max(y0, y1)));

            byte val = erase ? (byte)0 : (byte)255;
            for (int y = ymin; y <= ymax; y++)
            {
                int row = y * GridRes;
                for (int x = xmin; x <= xmax; x++)
                    NormGrid[row + x] = val;
            }
        }

        public void PaintCircleNormalized(
            float u0, float v0, float u1, float v1,
            int canvasW, int canvasH, string variantLetter, bool erase)
        {
            RefWidth = canvasW;
            RefHeight = canvasH;

            CanvasUvToGridXy(u0, v0, variantLetter, out int x0, out int y0);
            CanvasUvToGridXy(u1, v1, variantLetter, out int x1, out int y1);

            int xmin = Math.Max(0, Math.Min(GridRes - 1, Math.Min(x0, x1)));
            int xmax = Math.Max(0, Math.Min(GridRes - 1, Math.Max(x0, x1)));
            int ymin = Math.Max(0, Math.Min(GridRes - 1, Math.Min(y0, y1)));
            int ymax = Math.Max(0, Math.Min(GridRes - 1, Math.Max(y0, y1)));

            float cx = (x0 + x1) * 0.5f;
            float cy = (y0 + y1) * 0.5f;
            float rx = Math.Max(0.5f, Math.Abs(x1 - x0) * 0.5f);
            float ry = Math.Max(0.5f, Math.Abs(y1 - y0) * 0.5f);
            float invRx2 = 1f / (rx * rx);
            float invRy2 = 1f / (ry * ry);

            byte val = erase ? (byte)0 : (byte)255;
            for (int y = ymin; y <= ymax; y++)
            {
                float dy = y - cy;
                float dyTerm = (dy * dy) * invRy2;
                int row = y * GridRes;
                for (int x = xmin; x <= xmax; x++)
                {
                    float dx = x - cx;
                    if ((dx * dx) * invRx2 + dyTerm <= 1.0f)
                        NormGrid[row + x] = val;
                }
            }
        }

        public void PaintTriangleNormalized(
            float u0, float v0, float u1, float v1,
            int canvasW, int canvasH, string variantLetter, bool erase)
        {
            RefWidth = canvasW;
            RefHeight = canvasH;

            CanvasUvToGridXy(u0, v0, variantLetter, out int x0, out int y0);
            CanvasUvToGridXy(u1, v1, variantLetter, out int x1, out int y1);

            int xmin = Math.Max(0, Math.Min(GridRes - 1, Math.Min(x0, x1)));
            int xmax = Math.Max(0, Math.Min(GridRes - 1, Math.Max(x0, x1)));
            int ymin = Math.Max(0, Math.Min(GridRes - 1, Math.Min(y0, y1)));
            int ymax = Math.Max(0, Math.Min(GridRes - 1, Math.Max(y0, y1)));

            // Triangle vertices: apex at top/start Y center (ax, ay), base at (bx, by) and (cx, cy)
            PointF a = new PointF((x0 + x1) * 0.5f, y0);
            PointF b = new PointF(x0, y1);
            PointF c = new PointF(x1, y1);

            byte val = erase ? (byte)0 : (byte)255;
            for (int y = ymin; y <= ymax; y++)
            {
                int row = y * GridRes;
                for (int x = xmin; x <= xmax; x++)
                {
                    if (PointInTriangle(x, y, a, b, c))
                        NormGrid[row + x] = val;
                }
            }
        }

        private static bool PointInTriangle(float px, float py, PointF a, PointF b, PointF c)
        {
            float d1 = Sign2D(px, py, a.X, a.Y, b.X, b.Y);
            float d2 = Sign2D(px, py, b.X, b.Y, c.X, c.Y);
            float d3 = Sign2D(px, py, c.X, c.Y, a.X, a.Y);

            bool hasNeg = (d1 < -1e-4f) || (d2 < -1e-4f) || (d3 < -1e-4f);
            bool hasPos = (d1 > 1e-4f) || (d2 > 1e-4f) || (d3 > 1e-4f);
            return !(hasNeg && hasPos);
        }

        private static float Sign2D(float p1x, float p1y, float p2x, float p2y, float p3x, float p3y)
        {
            return (p1x - p3x) * (p2y - p3y) - (p2x - p3x) * (p1y - p3y);
        }

        public void MoveMaskByPixels(
            float dxPx, float dyPx, int canvasW, int canvasH,
            string activeVariant, bool onlyActiveVariant)
        {
            float du = dxPx / Math.Max(1, canvasW);
            float dv = dyPx / Math.Max(1, canvasH);

            if (onlyActiveVariant)
            {
                var vt = GetVariantTransform(activeVariant);
                vt.OffsetU += du;
                vt.OffsetV += dv;
            }
            else
            {
                GroupOffsetU += du;
                GroupOffsetV += dv;
            }
        }

        public byte[] GetVariantBinaryMask(string variantLetter, int targetW, int targetH)
        {
            byte[] outMask = new byte[targetW * targetH];
            if (!HasAnyMask())
                return outMask;

            var vt = GetVariantTransform(variantLetter);
            float totalDu = GroupOffsetU + vt.OffsetU;
            float totalDv = GroupOffsetV + vt.OffsetV;
            float sx = Math.Abs(vt.ScaleX) > 1e-4f ? vt.ScaleX : 1f;
            float sy = Math.Abs(vt.ScaleY) > 1e-4f ? vt.ScaleY : 1f;

            float invW = 1f / Math.Max(1, targetW - 1);
            float invH = 1f / Math.Max(1, targetH - 1);
            int gMax = GridRes - 1;

            for (int y = 0; y < targetH; y++)
            {
                float v = y * invH;
                float vUnshifted = v - totalDv;
                float vBase = (vUnshifted - 0.5f) / sy + 0.5f;
                int gy = (int)Math.Round(vBase * gMax);
                if (gy < 0 || gy > gMax) continue;

                int dstRow = y * targetW;
                int srcRow = gy * GridRes;

                for (int x = 0; x < targetW; x++)
                {
                    float u = x * invW;
                    float uUnshifted = u - totalDu;
                    float uBase = (uUnshifted - 0.5f) / sx + 0.5f;
                    int gx = (int)Math.Round(uBase * gMax);
                    if (gx >= 0 && gx <= gMax)
                    {
                        outMask[dstRow + x] = NormGrid[srcRow + gx];
                    }
                }
            }

            return outMask;
        }

        public void GetVariantProcessedMasks(
            string variantLetter,
            int targetW,
            int targetH,
            out byte[] rawBinaryMask,
            out byte[] dilatedFootprintMask,
            out float[] softFeatherAlpha)
        {
            rawBinaryMask = GetVariantBinaryMask(variantLetter, targetW, targetH);
            int len = targetW * targetH;
            dilatedFootprintMask = new byte[len];
            softFeatherAlpha = new float[len];

            bool any = false;
            for (int i = 0; i < len; i++)
            {
                if (rawBinaryMask[i] > 0) { any = true; break; }
            }
            if (!any) return;

            int dilate = Math.Max(0, Math.Min(8, DilatePx));
            int feather = Math.Max(0, Math.Min(8, FeatherPx));

            // Fast sharp binary path when Dilate == 0 and Feather == 0 (no gradient transition)
            if (dilate == 0 && feather == 0)
            {
                Buffer.BlockCopy(rawBinaryMask, 0, dilatedFootprintMask, 0, len);
                for (int i = 0; i < len; i++)
                    softFeatherAlpha[i] = rawBinaryMask[i] > 0 ? 1f : 0f;
                return;
            }

            byte[] dilated = (dilate > 0)
                ? DilateMaskDisk(rawBinaryMask, targetW, targetH, dilate)
                : (byte[])rawBinaryMask.Clone();

            if (feather > 0)
            {
                softFeatherAlpha = BoxBlurFeather(dilated, targetW, targetH, feather);
                for (int i = 0; i < len; i++)
                {
                    dilatedFootprintMask[i] = softFeatherAlpha[i] > 0.002f ? (byte)255 : (byte)0;
                }
            }
            else
            {
                Buffer.BlockCopy(dilated, 0, dilatedFootprintMask, 0, len);
                for (int i = 0; i < len; i++)
                    softFeatherAlpha[i] = dilated[i] > 0 ? 1f : 0f;
            }
        }

        public static byte[] DilateMaskDisk(byte[] src, int w, int h, int radius)
        {
            if (radius <= 0) return (byte[])src.Clone();
            byte[] dst = new byte[w * h];
            int r2 = radius * radius;

            var offsets = new List<Point>();
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy <= r2)
                        offsets.Add(new Point(dx, dy));
                }
            }

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (src[row + x] == 0) continue;
                    foreach (var pt in offsets)
                    {
                        int nx = x + pt.X;
                        int ny = y + pt.Y;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                            dst[ny * w + nx] = 255;
                    }
                }
            }
            return dst;
        }

        private static float[] BoxBlurFeather(byte[] dilated, int w, int h, int radius)
        {
            int len = w * h;
            float[] temp = new float[len];
            float[] dst = new float[len];
            for (int i = 0; i < len; i++)
                dst[i] = dilated[i] / 255f;

            for (int iter = 0; iter < 2; iter++)
            {
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        int count = 0;
                        int xmin = Math.Max(0, x - radius);
                        int xmax = Math.Min(w - 1, x + radius);
                        for (int kx = xmin; kx <= xmax; kx++)
                        {
                            sum += dst[row + kx];
                            count++;
                        }
                        temp[row + x] = sum / count;
                    }
                }

                for (int y = 0; y < h; y++)
                {
                    int ymin = Math.Max(0, y - radius);
                    int ymax = Math.Min(h - 1, y + radius);
                    int count = ymax - ymin + 1;
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        for (int ky = ymin; ky <= ymax; ky++)
                            sum += temp[ky * w + x];
                        dst[row + x] = Math.Min(1f, Math.Max(0f, sum / count));
                    }
                }
            }
            return dst;
        }

        public string ExportBakedGridBase64()
        {
            byte[] baked = GetVariantBinaryMask("a", GridRes, GridRes);
            byte[] comp = CompressBytes(baked);
            return Convert.ToBase64String(comp);
        }

        public void ImportBakedGridBase64(string b64, int gridRes, int refW, int refH, int dilate, int feather)
        {
            PushUndo();
            byte[] comp = Convert.FromBase64String(b64);
            byte[] raw = DecompressBytes(comp, gridRes * gridRes);

            if (gridRes == GridRes)
            {
                NormGrid = raw;
            }
            else
            {
                NormGrid = new byte[GridRes * GridRes];
                for (int y = 0; y < GridRes; y++)
                {
                    int sy = y * gridRes / GridRes;
                    for (int x = 0; x < GridRes; x++)
                    {
                        int sx = x * gridRes / GridRes;
                        NormGrid[y * GridRes + x] = raw[sy * gridRes + sx];
                    }
                }
            }

            RefWidth = refW;
            RefHeight = refH;
            DilatePx = Math.Max(0, Math.Min(8, dilate));
            FeatherPx = Math.Max(0, Math.Min(8, feather));
            GroupOffsetU = 0f;
            GroupOffsetV = 0f;
            VariantTransforms.Clear();
        }

        public static List<AlignmentWarning> CheckGroupAlignment(
            Dictionary<string, Size> variantSizes,
            int refWidth,
            int refHeight)
        {
            var warnings = new List<AlignmentWarning>();
            if (variantSizes == null || variantSizes.Count == 0)
                return warnings;

            if (refWidth <= 0 || refHeight <= 0)
            {
                foreach (var kv in variantSizes)
                {
                    refWidth = kv.Value.Width;
                    refHeight = kv.Value.Height;
                    break;
                }
            }

            float refAspect = (float)refWidth / Math.Max(1, refHeight);

            foreach (var kv in variantSizes)
            {
                string v = kv.Key;
                int w = kv.Value.Width;
                int h = kv.Value.Height;
                float aspect = (float)w / Math.Max(1, h);
                bool aspectMismatch = Math.Abs(aspect - refAspect) > 0.01f;
                bool resDiff = (w != refWidth) || (h != refHeight);

                if (aspectMismatch)
                {
                    warnings.Add(new AlignmentWarning
                    {
                        VariantLetter = v,
                        Width = w,
                        Height = h,
                        AspectRatio = aspect,
                        AspectMismatch = true,
                        ResolutionDiffers = resDiff,
                        Message = $"Varyant '{v.ToUpperInvariant()}' en/boy oranı farklı ({w}x{h} vs referans {refWidth}x{refHeight})! Maske hizasını kontrol edin."
                    });
                }
                else if (resDiff)
                {
                    warnings.Add(new AlignmentWarning
                    {
                        VariantLetter = v,
                        Width = w,
                        Height = h,
                        AspectRatio = aspect,
                        AspectMismatch = false,
                        ResolutionDiffers = true,
                        Message = $"Varyant '{v.ToUpperInvariant()}' çözünürlüğü farklı ({w}x{h} vs referans {refWidth}x{refHeight}). Normalize maske otomatik ölçeklendi."
                    });
                }
            }
            return warnings;
        }
    }
}
