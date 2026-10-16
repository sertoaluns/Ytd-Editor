using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using YTDEditor.Core;

namespace YTDEditor.UI
{
    public class CursorCoordsEventArgs : EventArgs
    {
        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public float NormU { get; set; }
        public float NormV { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
    }

    public class CanvasControl : UserControl
    {
        public event EventHandler<CursorCoordsEventArgs> CursorCoordsChanged;
        public event EventHandler MaskModified;
        public event EventHandler<PointF> CloneSourcePicked;
        public event EventHandler<string> MoveStatusNote;
        public event EventHandler UndoRequested;
        public event EventHandler RedoRequested;
        public event EventHandler ClearMaskRequested;
        public event EventHandler<string> ToolChangedByShortcut;

        // Raw RGBA buffers
        private byte[] _origRgba;
        private byte[] _procRgba;
        private byte[] _refPatchRgba;
        private int _imgW;
        private int _imgH;

        // Cached Bitmaps
        private Bitmap _bmpOrig;
        private Bitmap _bmpProc;
        private Bitmap _bmpMaskOverlay;
        private Bitmap _bmpAlignOverlay;
        private TextureBrush _checkerBrush;

        // Mask state & active variant
        public GroupMaskState MaskState { get; private set; }
        public string ActiveVariant { get; private set; } = "a";

        // View & Tool state: "brush", "eraser", "rect", "circle", "triangle", "move", "clone_pick"
        public string ActiveTool { get; private set; } = "rect";
        public int BrushRadiusPx { get; set; } = 24;
        public float MaskOpacity { get; set; } = 0.48f;
        public Color MaskColor { get; set; } = Color.FromArgb(255, 50, 90);
        public bool ShowAlpha { get; set; } = true;
        public bool ShowCheckerboard { get; set; } = true;
        public bool ShowAlignmentOverlay { get; set; } = false;
        public bool SplitViewEnabled { get; set; } = false;
        public float SplitNormX { get; set; } = 0.5f;

        private float _zoomScale = 1.0f;
        private PointF _panOffset = new PointF(20, 20);

        // Mouse & Keyboard tracking
        private bool _isPanning;
        private bool _spacePressed;
        private Point _panStartMouse;
        private PointF _panStartOffset;
        private bool _draggingSplitLine;

        private bool _isDrawing;
        private bool _drawEraseMode;
        private PointF _lastUv;
        private PointF _shapeStartUv;
        private PointF _shapeCurrentUv;

        private bool _isMovingMask;
        private PointF _moveLastImgPt;
        private PointF? _hoverImgPt;

        public CanvasControl()
        {
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(235, 238, 242);
            TabStop = true;

            BuildCheckerBrush();
        }

        private static bool IsShapeTool(string tool)
        {
            return tool == "rect" || tool == "circle" || tool == "triangle";
        }

        private void BuildCheckerBrush()
        {
            int s = 16;
            var tile = new Bitmap(s * 2, s * 2, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(tile))
            {
                g.Clear(Color.FromArgb(230, 233, 240));
                using (var b = new SolidBrush(Color.White))
                {
                    g.FillRectangle(b, 0, 0, s, s);
                    g.FillRectangle(b, s, s, s, s);
                }
            }
            _checkerBrush = new TextureBrush(tile, WrapMode.Tile);
        }

        public void SetActiveTool(string toolName)
        {
            ActiveTool = toolName;
            Invalidate();
        }

        public void SetContent(
            byte[] origRgba,
            byte[] procRgba,
            int width,
            int height,
            GroupMaskState maskState,
            string activeVariant,
            byte[] refPatchRgba = null,
            bool resetView = false)
        {
            bool firstLoad = (_origRgba == null && origRgba != null);
            _origRgba = origRgba;
            _procRgba = procRgba ?? origRgba;
            _refPatchRgba = refPatchRgba;
            _imgW = width;
            _imgH = height;
            MaskState = maskState;
            ActiveVariant = activeVariant ?? "a";

            RebuildImageBitmaps();
            RebuildMaskOverlayBitmap();

            if (resetView || firstLoad)
                ZoomToFit();
            else
                Invalidate();
        }

        public void UpdateProcessedPreview(byte[] procRgba, byte[] refPatchRgba = null)
        {
            _procRgba = procRgba ?? _origRgba;
            if (refPatchRgba != null)
                _refPatchRgba = refPatchRgba;
            RebuildImageBitmaps();
            RebuildMaskOverlayBitmap();
            Invalidate();
        }

        public void RebuildImageBitmaps()
        {
            _bmpOrig?.Dispose();
            _bmpProc?.Dispose();
            _bmpAlignOverlay?.Dispose();

            if (_origRgba == null || _imgW <= 0 || _imgH <= 0)
            {
                _bmpOrig = null;
                _bmpProc = null;
                _bmpAlignOverlay = null;
                return;
            }

            _bmpOrig = DarkTheme.RgbaToBitmap(_origRgba, _imgW, _imgH, ShowAlpha);
            _bmpProc = (_procRgba != null) ? DarkTheme.RgbaToBitmap(_procRgba, _imgW, _imgH, ShowAlpha) : null;

            if (_refPatchRgba != null && _refPatchRgba.Length == _imgW * _imgH * 4)
            {
                byte[] semi = new byte[_refPatchRgba.Length];
                for (int i = 0; i < _imgW * _imgH; i++)
                {
                    int p = i * 4;
                    semi[p] = _refPatchRgba[p];
                    semi[p + 1] = _refPatchRgba[p + 1];
                    semi[p + 2] = _refPatchRgba[p + 2];
                    semi[p + 3] = (byte)(_refPatchRgba[p + 3] * 0.45f);
                }
                _bmpAlignOverlay = DarkTheme.RgbaToBitmap(semi, _imgW, _imgH, true);
            }
            else
            {
                _bmpAlignOverlay = null;
            }
        }

        public void RebuildMaskOverlayBitmap()
        {
            if (_origRgba == null || _imgW <= 0 || _imgH <= 0 || MaskState == null || !MaskState.HasAnyMask())
            {
                _bmpMaskOverlay?.Dispose();
                _bmpMaskOverlay = null;
                return;
            }

            MaskState.GetVariantProcessedMasks(
                ActiveVariant,
                _imgW,
                _imgH,
                out byte[] rawBin,
                out byte[] dilatedBin,
                out float[] softAlpha);

            int len = _imgW * _imgH;
            byte[] overlayBgra = new byte[len * 4];
            byte r = MaskColor.R;
            byte g = MaskColor.G;
            byte b = MaskColor.B;
            byte baseAlpha = (byte)Math.Min(255, Math.Max(0, (int)(MaskOpacity * 255f)));

            for (int y = 0; y < _imgH; y++)
            {
                int row = y * _imgW;
                for (int x = 0; x < _imgW; x++)
                {
                    int i = row + x;
                    float sa = softAlpha[i];
                    if (sa <= 0f) continue;

                    int p = i * 4;
                    bool isEdge = false;
                    if (rawBin[i] > 0)
                    {
                        if (x == 0 || x == _imgW - 1 || y == 0 || y == _imgH - 1 ||
                            rawBin[i - 1] == 0 || rawBin[i + 1] == 0 || rawBin[i - _imgW] == 0 || rawBin[i + _imgW] == 0)
                        {
                            isEdge = true;
                        }
                    }

                    if (isEdge)
                    {
                        overlayBgra[p] = 255;     // B
                        overlayBgra[p + 1] = 255; // G
                        overlayBgra[p + 2] = 255; // R
                        overlayBgra[p + 3] = 230; // A
                    }
                    else
                    {
                        byte a = (sa >= 0.999f) ? baseAlpha : (byte)Math.Min(255, Math.Max(0, (int)(sa * baseAlpha)));
                        overlayBgra[p] = b;
                        overlayBgra[p + 1] = g;
                        overlayBgra[p + 2] = r;
                        overlayBgra[p + 3] = a;
                    }
                }
            }

            if (_bmpMaskOverlay == null || _bmpMaskOverlay.Width != _imgW || _bmpMaskOverlay.Height != _imgH)
            {
                _bmpMaskOverlay?.Dispose();
                _bmpMaskOverlay = new Bitmap(_imgW, _imgH, PixelFormat.Format32bppArgb);
            }

            var bmpData = _bmpMaskOverlay.LockBits(new Rectangle(0, 0, _imgW, _imgH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(overlayBgra, 0, bmpData.Scan0, overlayBgra.Length);
            }
            finally
            {
                _bmpMaskOverlay.UnlockBits(bmpData);
            }
        }

        // Ultra-fast dirty-rectangle overlay update during live brush/eraser dragging (0ms lag)
        private void UpdateMaskOverlayStrokeRegion(float u0, float v0, float u1, float v1)
        {
            if (_origRgba == null || _imgW <= 0 || _imgH <= 0 || MaskState == null)
                return;

            if (_bmpMaskOverlay == null || _bmpMaskOverlay.Width != _imgW || _bmpMaskOverlay.Height != _imgH)
            {
                _bmpMaskOverlay?.Dispose();
                _bmpMaskOverlay = new Bitmap(_imgW, _imgH, PixelFormat.Format32bppArgb);
            }

            int x0 = (int)Math.Round(u0 * (_imgW - 1));
            int y0 = (int)Math.Round(v0 * (_imgH - 1));
            int x1 = (int)Math.Round(u1 * (_imgW - 1));
            int y1 = (int)Math.Round(v1 * (_imgH - 1));
            int pad = BrushRadiusPx + 4;

            int minX = Math.Max(0, Math.Min(x0, x1) - pad);
            int maxX = Math.Min(_imgW - 1, Math.Max(x0, x1) + pad);
            int minY = Math.Max(0, Math.Min(y0, y1) - pad);
            int maxY = Math.Min(_imgH - 1, Math.Max(y0, y1) + pad);
            if (maxX < minX || maxY < minY) return;

            var vt = MaskState.GetVariantTransform(ActiveVariant);
            float totalDu = MaskState.GroupOffsetU + vt.OffsetU;
            float totalDv = MaskState.GroupOffsetV + vt.OffsetV;
            float sx = Math.Abs(vt.ScaleX) > 1e-4f ? vt.ScaleX : 1f;
            float sy = Math.Abs(vt.ScaleY) > 1e-4f ? vt.ScaleY : 1f;
            float invW = 1f / Math.Max(1, _imgW - 1);
            float invH = 1f / Math.Max(1, _imgH - 1);
            int gMax = MaskState.GridRes - 1;
            int gRes = MaskState.GridRes;
            byte[] normGrid = MaskState.NormGrid;

            byte r = MaskColor.R;
            byte g = MaskColor.G;
            byte b = MaskColor.B;
            byte a = (byte)Math.Min(255, Math.Max(40, (int)(MaskOpacity * 255f)));

            int rw = maxX - minX + 1;
            int rh = maxY - minY + 1;
            var rect = new Rectangle(minX, minY, rw, rh);
            var bmpData = _bmpMaskOverlay.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = bmpData.Stride;
                byte[] rowBuf = new byte[rw * 4];
                for (int y = minY; y <= maxY; y++)
                {
                    float v = y * invH;
                    float vBase = ((v - totalDv) - 0.5f) / sy + 0.5f;
                    int gy = (int)Math.Round(vBase * gMax);
                    int srcRow = (gy >= 0 && gy <= gMax) ? (gy * gRes) : -1;

                    for (int x = minX; x <= maxX; x++)
                    {
                        int localIdx = (x - minX) * 4;
                        byte masked = 0;
                        if (srcRow >= 0)
                        {
                            float u = x * invW;
                            float uBase = ((u - totalDu) - 0.5f) / sx + 0.5f;
                            int gx = (int)Math.Round(uBase * gMax);
                            if (gx >= 0 && gx <= gMax)
                                masked = normGrid[srcRow + gx];
                        }

                        if (masked > 0)
                        {
                            rowBuf[localIdx] = b;
                            rowBuf[localIdx + 1] = g;
                            rowBuf[localIdx + 2] = r;
                            rowBuf[localIdx + 3] = a;
                        }
                        else
                        {
                            rowBuf[localIdx] = 0;
                            rowBuf[localIdx + 1] = 0;
                            rowBuf[localIdx + 2] = 0;
                            rowBuf[localIdx + 3] = 0;
                        }
                    }

                    IntPtr dstRowPtr = IntPtr.Add(bmpData.Scan0, (y - minY) * stride);
                    Marshal.Copy(rowBuf, 0, dstRowPtr, rowBuf.Length);
                }
            }
            finally
            {
                _bmpMaskOverlay.UnlockBits(bmpData);
            }
        }

        public void ZoomToFit()
        {
            if (_imgW <= 0 || _imgH <= 0) return;
            float availW = Math.Max(64, Width - 32);
            float availH = Math.Max(64, Height - 32);

            float sx = availW / _imgW;
            float sy = availH / _imgH;
            _zoomScale = Math.Max(0.05f, Math.Min(Math.Min(sx, sy), 8f));

            float dispW = _imgW * _zoomScale;
            float dispH = _imgH * _zoomScale;
            _panOffset = new PointF((Width - dispW) * 0.5f, (Height - dispH) * 0.5f);
            Invalidate();
        }

        public void ZoomOneToOne()
        {
            if (_imgW <= 0 || _imgH <= 0) return;
            _zoomScale = 1.0f;
            _panOffset = new PointF((Width - _imgW) * 0.5f, Math.Max(10, (Height - _imgH) * 0.5f));
            Invalidate();
        }

        public void ZoomBy(float factor, PointF? anchor = null)
        {
            if (_imgW <= 0 || _imgH <= 0) return;
            PointF a = anchor ?? new PointF(Width * 0.5f, Height * 0.5f);

            float oldScale = _zoomScale;
            float newScale = Math.Max(0.05f, Math.Min(32f, oldScale * factor));
            if (Math.Abs(newScale - oldScale) < 1e-6f) return;

            float ix = (a.X - _panOffset.X) / oldScale;
            float iy = (a.Y - _panOffset.Y) / oldScale;

            _zoomScale = newScale;
            _panOffset = new PointF(a.X - ix * newScale, a.Y - iy * newScale);
            Invalidate();
        }

        private PointF ScreenToImage(Point pt)
        {
            return new PointF(
                (pt.X - _panOffset.X) / Math.Max(1e-5f, _zoomScale),
                (pt.Y - _panOffset.Y) / Math.Max(1e-5f, _zoomScale));
        }

        private PointF ImageToNormalizedUv(PointF imgPt)
        {
            if (_imgW <= 0 || _imgH <= 0) return PointF.Empty;
            float u = Math.Max(0f, Math.Min(1f, imgPt.X / Math.Max(1, _imgW - 1)));
            float v = Math.Max(0f, Math.Min(1f, imgPt.Y / Math.Max(1, _imgH - 1)));
            return new PointF(u, v);
        }

        private PointF ConstrainShiftSquareUv(PointF startUv, PointF curUv)
        {
            if (_imgW <= 0 || _imgH <= 0) return curUv;
            float dxPx = (curUv.X - startUv.X) * (_imgW - 1);
            float dyPx = (curUv.Y - startUv.Y) * (_imgH - 1);
            float sidePx = Math.Max(Math.Abs(dxPx), Math.Abs(dyPx));

            float signX = dxPx >= 0 ? 1f : -1f;
            float signY = dyPx >= 0 ? 1f : -1f;

            float newU = Math.Max(0f, Math.Min(1f, startUv.X + (signX * sidePx) / Math.Max(1, _imgW - 1)));
            float newV = Math.Max(0f, Math.Min(1f, startUv.Y + (signY * sidePx) / Math.Max(1, _imgH - 1)));
            return new PointF(newU, newV);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            if (_bmpOrig == null || _imgW <= 0 || _imgH <= 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(DarkTheme.TextMuted))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(
                        Loc.T(
                            "Sol panelden bir YTD klasörü seçin ve düzenlemek için bir slot grubu açın.",
                            "Select a YTD folder from the left panel and open a slot group to edit.",
                            "Wählen Sie links einen YTD-Ordner aus und öffnen Sie eine Slot-Gruppe zum Bearbeiten."),
                        DarkTheme.FontRegular, br, ClientRectangle, sf);
                }
                return;
            }

            var destRect = new RectangleF(_panOffset.X, _panOffset.Y, _imgW * _zoomScale, _imgH * _zoomScale);

            // 1. Background (Checkerboard or White)
            if (ShowCheckerboard && _checkerBrush != null)
                g.FillRectangle(_checkerBrush, destRect);
            else
                g.FillRectangle(Brushes.White, destRect);

            // Crisp pixel rendering without blurry gradient interpolation at >= 1x zoom
            g.InterpolationMode = _zoomScale >= 1.0f ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // 2. Draw Original / Processed (Split View vs Normal)
            if (SplitViewEnabled && _bmpProc != null)
            {
                float splitScreenX = destRect.Left + destRect.Width * SplitNormX;

                var stateLeft = g.Save();
                g.SetClip(new RectangleF(destRect.Left, destRect.Top, splitScreenX - destRect.Left, destRect.Height));
                g.DrawImage(_bmpOrig, destRect);
                g.Restore(stateLeft);

                var stateRight = g.Save();
                g.SetClip(new RectangleF(splitScreenX, destRect.Top, destRect.Right - splitScreenX, destRect.Height));
                g.DrawImage(_bmpProc, destRect);
                g.Restore(stateRight);
            }
            else
            {
                g.DrawImage(_bmpProc ?? _bmpOrig, destRect);
            }

            // 3. Alignment Overlay
            if (ShowAlignmentOverlay && _bmpAlignOverlay != null)
            {
                g.DrawImage(_bmpAlignOverlay, destRect);
            }

            // 4. Mask Overlay (Always NearestNeighbor so pixel edges are razor-sharp, never gradient-blurred)
            if (_bmpMaskOverlay != null)
            {
                var prevInterp = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(_bmpMaskOverlay, destRect);
                g.InterpolationMode = prevInterp;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 5. Split View Divider Line
            if (SplitViewEnabled)
            {
                float splitScreenX = destRect.Left + destRect.Width * SplitNormX;
                using (var pen = new Pen(Color.FromArgb(234, 179, 8), 2f))
                {
                    g.DrawLine(pen, splitScreenX, destRect.Top, splitScreenX, destRect.Bottom);
                }
                float cy = destRect.Top + destRect.Height * 0.5f;
                using (var br = new SolidBrush(Color.FromArgb(234, 179, 8)))
                {
                    g.FillEllipse(br, splitScreenX - 8, cy - 8, 16, 16);
                }

                using (var bgBr = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                using (var borderPen = new Pen(Color.FromArgb(209, 213, 219)))
                using (var textBr = new SolidBrush(DarkTheme.TextPrimary))
                {
                    g.FillRectangle(bgBr, splitScreenX - 64, destRect.Top + 10, 56, 20);
                    g.DrawRectangle(borderPen, splitScreenX - 64, destRect.Top + 10, 56, 20);

                    g.FillRectangle(bgBr, splitScreenX + 8, destRect.Top + 10, 62, 20);
                    g.DrawRectangle(borderPen, splitScreenX + 8, destRect.Top + 10, 62, 20);

                    g.DrawString(Loc.T("ÖNCE", "BEFORE", "VORHER"), DarkTheme.FontBold, textBr, splitScreenX - 58, destRect.Top + 12);
                    g.DrawString(Loc.T("SONRA", "AFTER", "NACHHER"), DarkTheme.FontBold, textBr, splitScreenX + 12, destRect.Top + 12);
                }
            }

            // 6. Active Vector Shape Preview (Square/Rect, Circle, Triangle) - 0ms GPU/GDI+ vector preview
            if (_isDrawing && IsShapeTool(ActiveTool))
            {
                float x0 = destRect.Left + _shapeStartUv.X * destRect.Width;
                float y0 = destRect.Top + _shapeStartUv.Y * destRect.Height;
                float x1 = destRect.Left + _shapeCurrentUv.X * destRect.Width;
                float y1 = destRect.Top + _shapeCurrentUv.Y * destRect.Height;

                var selRect = new RectangleF(
                    Math.Min(x0, x1),
                    Math.Min(y0, y1),
                    Math.Max(1f, Math.Abs(x1 - x0)),
                    Math.Max(1f, Math.Abs(y1 - y0)));

                Color c = _drawEraseMode ? Color.FromArgb(239, 68, 68) : Color.FromArgb(0, 120, 215);
                using (var pen = new Pen(c, 1.8f))
                using (var br = new SolidBrush(Color.FromArgb(65, c)))
                {
                    if (ActiveTool == "rect")
                    {
                        g.FillRectangle(br, selRect);
                        g.DrawRectangle(pen, selRect.X, selRect.Y, selRect.Width, selRect.Height);
                    }
                    else if (ActiveTool == "circle")
                    {
                        g.FillEllipse(br, selRect);
                        g.DrawEllipse(pen, selRect);
                    }
                    else if (ActiveTool == "triangle")
                    {
                        PointF[] triPts =
                        {
                            new PointF((x0 + x1) * 0.5f, y0),
                            new PointF(x0, y1),
                            new PointF(x1, y1)
                        };
                        g.FillPolygon(br, triPts);
                        g.DrawPolygon(pen, triPts);
                    }
                }
            }

            // 7. Clone Source Crosshair
            if (MaskState != null && MaskState.HasCloneSource)
            {
                float sx = destRect.Left + MaskState.CloneSourceU * destRect.Width;
                float sy = destRect.Top + MaskState.CloneSourceV * destRect.Height;
                using (var pen = new Pen(Color.FromArgb(16, 185, 129), 2f))
                {
                    g.DrawEllipse(pen, sx - 7, sy - 7, 14, 14);
                    g.DrawLine(pen, sx - 11, sy, sx + 11, sy);
                    g.DrawLine(pen, sx, sy - 11, sx, sy + 11);
                }
            }

            // 8. Brush / Eraser Cursor Circle
            if (_hoverImgPt.HasValue && (ActiveTool == "brush" || ActiveTool == "eraser") && !_isPanning)
            {
                float sx = destRect.Left + _hoverImgPt.Value.X * _zoomScale;
                float sy = destRect.Top + _hoverImgPt.Value.Y * _zoomScale;
                float r = Math.Max(2f, BrushRadiusPx * _zoomScale);
                Color col = ActiveTool == "brush" ? Color.FromArgb(210, 0, 120, 215) : Color.FromArgb(210, 239, 68, 68);
                using (var pen = new Pen(col, 1.5f))
                {
                    g.DrawEllipse(pen, sx - r, sy - r, r * 2f, r * 2f);
                }
            }

            using (var borderPen = new Pen(Color.FromArgb(180, 185, 195), 1f))
            {
                g.DrawRectangle(borderPen, destRect.X, destRect.Y, destRect.Width, destRect.Height);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta == 0) return;
            float factor = e.Delta > 0 ? 1.18f : (1f / 1.18f);
            ZoomBy(factor, new PointF(e.X, e.Y));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (_origRgba == null || _imgW <= 0 || _imgH <= 0) return;

            var destRect = new RectangleF(_panOffset.X, _panOffset.Y, _imgW * _zoomScale, _imgH * _zoomScale);

            // Middle click or Space+Left click -> Pan
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && _spacePressed))
            {
                _isPanning = true;
                _panStartMouse = e.Location;
                _panStartOffset = _panOffset;
                Cursor = Cursors.SizeAll;
                return;
            }

            // Split View divider drag
            if (SplitViewEnabled && e.Button == MouseButtons.Left)
            {
                float splitScreenX = destRect.Left + destRect.Width * SplitNormX;
                if (Math.Abs(e.X - splitScreenX) <= 12f && e.Y >= destRect.Top && e.Y <= destRect.Bottom)
                {
                    _draggingSplitLine = true;
                    return;
                }
            }

            if (MaskState == null) return;

            PointF imgPt = ScreenToImage(e.Location);
            PointF uv = ImageToNormalizedUv(imgPt);

            if (ActiveTool == "clone_pick" && e.Button == MouseButtons.Left)
            {
                MaskState.HasCloneSource = true;
                MaskState.CloneSourceU = uv.X;
                MaskState.CloneSourceV = uv.Y;
                CloneSourcePicked?.Invoke(this, uv);
                SetActiveTool("rect");
                ToolChangedByShortcut?.Invoke(this, "rect");
                MaskModified?.Invoke(this, EventArgs.Empty);
                Invalidate();
                return;
            }

            if (ActiveTool == "move" && e.Button == MouseButtons.Left)
            {
                MaskState.PushUndo();
                _isMovingMask = true;
                _moveLastImgPt = imgPt;
                return;
            }

            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                bool erase = (ActiveTool == "eraser") || (e.Button == MouseButtons.Right);
                _drawEraseMode = erase;
                MaskState.PushUndo();
                _isDrawing = true;
                _lastUv = uv;

                if (IsShapeTool(ActiveTool))
                {
                    _shapeStartUv = uv;
                    _shapeCurrentUv = uv;
                }
                else
                {
                    MaskState.PaintStrokeNormalized(uv.X, uv.Y, uv.X, uv.Y, BrushRadiusPx, _imgW, _imgH, ActiveVariant, erase);
                    UpdateMaskOverlayStrokeRegion(uv.X, uv.Y, uv.X, uv.Y);
                }
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_origRgba == null || _imgW <= 0 || _imgH <= 0) return;

            PointF imgPt = ScreenToImage(e.Location);
            _hoverImgPt = imgPt;

            int pxX = Math.Max(0, Math.Min(_imgW - 1, (int)Math.Floor(imgPt.X)));
            int pxY = Math.Max(0, Math.Min(_imgH - 1, (int)Math.Floor(imgPt.Y)));
            PointF uv = ImageToNormalizedUv(imgPt);

            CursorCoordsChanged?.Invoke(this, new CursorCoordsEventArgs
            {
                PixelX = pxX,
                PixelY = pxY,
                NormU = uv.X,
                NormV = uv.Y,
                ImageWidth = _imgW,
                ImageHeight = _imgH
            });

            if (_isPanning)
            {
                _panOffset = new PointF(
                    _panStartOffset.X + (e.X - _panStartMouse.X),
                    _panStartOffset.Y + (e.Y - _panStartMouse.Y));
                Invalidate();
                return;
            }

            if (_draggingSplitLine)
            {
                float destW = Math.Max(1f, _imgW * _zoomScale);
                SplitNormX = Math.Max(0.02f, Math.Min(0.98f, (e.X - _panOffset.X) / destW));
                Invalidate();
                return;
            }

            if (MaskState == null)
            {
                Invalidate();
                return;
            }

            if (_isMovingMask)
            {
                float dxPx = imgPt.X - _moveLastImgPt.X;
                float dyPx = imgPt.Y - _moveLastImgPt.Y;
                _moveLastImgPt = imgPt;

                bool altHeld = (ModifierKeys & Keys.Alt) == Keys.Alt;
                MaskState.MoveMaskByPixels(dxPx, dyPx, _imgW, _imgH, ActiveVariant, altHeld);

                string scope = altHeld
                    ? $"Sadece aktif varyant ({ActiveVariant.ToUpperInvariant()})"
                    : $"Gruptaki tüm varyantlar ({MaskState.GroupKey})";
                MoveStatusNote?.Invoke(this, "Maske taşınıyor: " + scope);

                RebuildMaskOverlayBitmap();
                Invalidate();
                return;
            }

            if (_isDrawing)
            {
                if (IsShapeTool(ActiveTool))
                {
                    bool shiftHeld = (ModifierKeys & Keys.Shift) == Keys.Shift;
                    _shapeCurrentUv = shiftHeld ? ConstrainShiftSquareUv(_shapeStartUv, uv) : uv;
                    Invalidate();
                }
                else
                {
                    MaskState.PaintStrokeNormalized(_lastUv.X, _lastUv.Y, uv.X, uv.Y, BrushRadiusPx, _imgW, _imgH, ActiveVariant, _drawEraseMode);
                    UpdateMaskOverlayStrokeRegion(_lastUv.X, _lastUv.Y, uv.X, uv.Y);
                    _lastUv = uv;
                    Invalidate();
                }
                return;
            }

            if (ActiveTool == "brush" || ActiveTool == "eraser")
                Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isPanning)
            {
                _isPanning = false;
                Cursor = Cursors.Default;
                return;
            }

            if (_draggingSplitLine)
            {
                _draggingSplitLine = false;
                return;
            }

            if (_isMovingMask)
            {
                _isMovingMask = false;
                RebuildMaskOverlayBitmap();
                MaskModified?.Invoke(this, EventArgs.Empty);
                Invalidate();
                return;
            }

            if (_isDrawing)
            {
                _isDrawing = false;
                if (MaskState != null && _imgW > 0 && _imgH > 0)
                {
                    if (ActiveTool == "rect")
                    {
                        MaskState.PaintRectNormalized(_shapeStartUv.X, _shapeStartUv.Y, _shapeCurrentUv.X, _shapeCurrentUv.Y, _imgW, _imgH, ActiveVariant, _drawEraseMode);
                    }
                    else if (ActiveTool == "circle")
                    {
                        MaskState.PaintCircleNormalized(_shapeStartUv.X, _shapeStartUv.Y, _shapeCurrentUv.X, _shapeCurrentUv.Y, _imgW, _imgH, ActiveVariant, _drawEraseMode);
                    }
                    else if (ActiveTool == "triangle")
                    {
                        MaskState.PaintTriangleNormalized(_shapeStartUv.X, _shapeStartUv.Y, _shapeCurrentUv.X, _shapeCurrentUv.Y, _imgW, _imgH, ActiveVariant, _drawEraseMode);
                    }

                    RebuildMaskOverlayBitmap();
                    MaskModified?.Invoke(this, EventArgs.Empty);
                }
                Invalidate();
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Space && !_spacePressed)
            {
                _spacePressed = true;
                Cursor = Cursors.Hand;
                e.Handled = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Z)
            {
                UndoRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Y)
            {
                RedoRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            if (!e.Control && !e.Alt)
            {
                if (e.KeyCode == Keys.B) { SetActiveTool("brush"); ToolChangedByShortcut?.Invoke(this, "brush"); e.Handled = true; return; }
                if (e.KeyCode == Keys.E) { SetActiveTool("eraser"); ToolChangedByShortcut?.Invoke(this, "eraser"); e.Handled = true; return; }
                if (e.KeyCode == Keys.R) { SetActiveTool("rect"); ToolChangedByShortcut?.Invoke(this, "rect"); e.Handled = true; return; }
                if (e.KeyCode == Keys.C) { SetActiveTool("circle"); ToolChangedByShortcut?.Invoke(this, "circle"); e.Handled = true; return; }
                if (e.KeyCode == Keys.T) { SetActiveTool("triangle"); ToolChangedByShortcut?.Invoke(this, "triangle"); e.Handled = true; return; }
                if (e.KeyCode == Keys.M) { SetActiveTool("move"); ToolChangedByShortcut?.Invoke(this, "move"); e.Handled = true; return; }
                if (e.KeyCode == Keys.Delete) { ClearMaskRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
            }

            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                if (MaskState != null && _imgW > 0 && _imgH > 0 && MaskState.HasAnyMask())
                {
                    float step = e.Shift ? 10f : 1f;
                    bool altOnly = e.Alt;
                    float dx = e.KeyCode == Keys.Left ? -step : (e.KeyCode == Keys.Right ? step : 0f);
                    float dy = e.KeyCode == Keys.Up ? -step : (e.KeyCode == Keys.Down ? step : 0f);

                    MaskState.PushUndo();
                    MaskState.MoveMaskByPixels(dx, dy, _imgW, _imgH, ActiveVariant, altOnly);

                    string scope = altOnly
                        ? $"Sadece aktif varyant ({ActiveVariant.ToUpperInvariant()})"
                        : $"Gruptaki tüm varyantlar ({MaskState.GroupKey})";
                    MoveStatusNote?.Invoke(this, $"Maske ok tuşuyla kaydırıldı ({dx:F0}, {dy:F0} px) -> {scope}");

                    RebuildMaskOverlayBitmap();
                    MaskModified?.Invoke(this, EventArgs.Empty);
                    Invalidate();
                    e.Handled = true;
                }
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space)
            {
                _spacePressed = false;
                if (!_isPanning)
                    Cursor = Cursors.Default;
                e.Handled = true;
            }
        }
    }
}
