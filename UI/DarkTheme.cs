using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YTDEditor.UI
{
    public static class Loc
    {
        public static string Lang { get; set; } = "tr"; // "tr", "en", "de"

        public static string T(string tr, string en, string de)
        {
            if (string.Equals(Lang, "en", StringComparison.OrdinalIgnoreCase))
                return en;
            if (string.Equals(Lang, "de", StringComparison.OrdinalIgnoreCase))
                return de;
            return tr;
        }
    }

    public static class DarkTheme
    {
        public static readonly Color BgMain = Color.FromArgb(240, 242, 245);
        public static readonly Color BgPanel = Color.White;
        public static readonly Color BgInput = Color.White;
        public static readonly Color BgButton = Color.FromArgb(248, 249, 250);
        public static readonly Color BgButtonActive = Color.FromArgb(0, 120, 215);
        public static readonly Color BgSuccess = Color.FromArgb(16, 124, 65);
        public static readonly Color BgAccent = Color.FromArgb(92, 45, 145);
        public static readonly Color BorderColor = Color.FromArgb(204, 206, 219);
        public static readonly Color TextPrimary = Color.FromArgb(30, 30, 30);
        public static readonly Color TextMuted = Color.FromArgb(100, 105, 115);

        // Pixel-unit fonts guarantee identical layout on 100%, 125%, 150%, and 200% Windows DPI scaling
        public static readonly Font FontRegular = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FontBold = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
        public static readonly Font FontSmall = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FontMono = new Font("Consolas", 12f, FontStyle.Regular, GraphicsUnit.Pixel);

        public static void StyleButton(Button btn, Color? bgColor = null)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = bgColor.HasValue ? ControlPaint.Dark(bgColor.Value, 0.08f) : BorderColor;
            btn.FlatAppearance.BorderSize = 1;
            btn.BackColor = bgColor ?? BgButton;
            btn.ForeColor = (bgColor.HasValue && bgColor.Value != BgButton) ? Color.White : TextPrimary;
            btn.Font = bgColor.HasValue ? FontBold : FontRegular;
            btn.UseVisualStyleBackColor = false;
            btn.Cursor = Cursors.Hand;
        }

        public static void ApplyDarkThemeRecursive(Control root)
        {
            if (root is Form frm)
            {
                frm.AutoScaleMode = AutoScaleMode.None;
                frm.BackColor = BgMain;
                frm.ForeColor = TextPrimary;
                frm.Font = FontRegular;
            }
            else if (root is UserControl uc)
            {
                uc.AutoScaleMode = AutoScaleMode.None;
                uc.BackColor = BgMain;
                uc.ForeColor = TextPrimary;
                uc.Font = FontRegular;
            }

            foreach (Control c in root.Controls)
            {
                if (c is MenuStrip || c is ToolStrip || c is StatusStrip)
                    continue;

                if (c is GroupBox gb)
                {
                    gb.BackColor = BgPanel;
                    gb.ForeColor = Color.FromArgb(45, 55, 72);
                    gb.Font = FontBold;
                }
                else if (c is TabControl tc)
                {
                    tc.Font = FontRegular;
                }
                else if (c is TabPage tp)
                {
                    tp.BackColor = BgPanel;
                    tp.ForeColor = TextPrimary;
                    tp.Font = FontRegular;
                }
                else if (c is Panel || c is SplitContainer || c is FlowLayoutPanel || c is TableLayoutPanel)
                {
                    if (c.BackColor == SystemColors.Control || c.BackColor == Color.FromArgb(22, 24, 30))
                        c.BackColor = BgMain;
                    c.ForeColor = TextPrimary;
                    c.Font = FontRegular;
                }
                else if (c is Button btn)
                {
                    if (btn.FlatStyle != FlatStyle.Flat)
                        StyleButton(btn);
                    else
                        btn.Font = (btn.BackColor == BgButtonActive || btn.BackColor == BgSuccess || btn.BackColor == BgAccent) ? FontBold : FontRegular;
                }
                else if (c is TextBox tb)
                {
                    tb.BackColor = BgInput;
                    tb.ForeColor = TextPrimary;
                    if (tb.Font.Name != "Consolas")
                        tb.Font = FontRegular;
                    else
                        tb.Font = FontMono;
                }
                else if (c is ListBox || c is TreeView || c is ComboBox || c is NumericUpDown || c is CheckedListBox)
                {
                    c.BackColor = BgInput;
                    c.ForeColor = TextPrimary;
                    c.Font = FontRegular;
                }
                else if (c is Label || c is CheckBox || c is RadioButton)
                {
                    if (c.ForeColor == SystemColors.ControlText || c.ForeColor == Color.White)
                        c.ForeColor = TextPrimary;
                    c.Font = (c.Font != null && c.Font.Bold) ? FontBold : FontRegular;
                }

                if (c.HasChildren)
                    ApplyDarkThemeRecursive(c);
            }
        }

        public static Bitmap CreateIcon(string name, Color? customColor = null)
        {
            var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                Color primary = customColor ?? Color.FromArgb(45, 55, 72);
                Color accent = customColor ?? Color.FromArgb(0, 120, 215);

                switch (name)
                {
                    case "brush":
                        using (var penHandle = new Pen(customColor ?? Color.FromArgb(180, 83, 9), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(penHandle, 13, 2, 7, 8);
                        using (var brushTip = new SolidBrush(accent))
                        {
                            PointF[] tip = { new PointF(7, 8), new PointF(2, 14), new PointF(8, 12), new PointF(9, 9) };
                            g.FillPolygon(brushTip, tip);
                        }
                        break;

                    case "eraser":
                        {
                            PointF[] pts = { new PointF(9, 2), new PointF(14, 7), new PointF(7, 14), new PointF(2, 9) };
                            using (var bFill = new SolidBrush(customColor.HasValue ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(244, 114, 182)))
                                g.FillPolygon(bFill, pts);
                            using (var pen = new Pen(primary, 1.4f))
                                g.DrawPolygon(pen, pts);
                            using (var penLine = new Pen(primary, 1.2f))
                                g.DrawLine(penLine, 5, 6, 10, 11);
                        }
                        break;

                    case "rect":
                        using (var bFill = new SolidBrush(customColor.HasValue ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(45, 0, 120, 215)))
                            g.FillRectangle(bFill, 2, 2, 12, 12);
                        using (var pen = new Pen(accent, 1.6f))
                            g.DrawRectangle(pen, 2, 2, 12, 12);
                        break;

                    case "circle":
                        using (var bFill = new SolidBrush(customColor.HasValue ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(45, 0, 120, 215)))
                            g.FillEllipse(bFill, 2, 2, 12, 12);
                        using (var pen = new Pen(accent, 1.6f))
                            g.DrawEllipse(pen, 2, 2, 12, 12);
                        break;

                    case "triangle":
                        {
                            PointF[] triPts = { new PointF(8f, 2f), new PointF(14f, 13.5f), new PointF(2f, 13.5f) };
                            using (var bFill = new SolidBrush(customColor.HasValue ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(45, 0, 120, 215)))
                                g.FillPolygon(bFill, triPts);
                            using (var pen = new Pen(accent, 1.6f) { LineJoin = LineJoin.Round })
                                g.DrawPolygon(pen, triPts);
                        }
                        break;

                    case "move":
                        using (var pen = new Pen(primary, 1.6f))
                        {
                            g.DrawLine(pen, 8, 1, 8, 15);
                            g.DrawLine(pen, 1, 8, 15, 8);
                            g.DrawLines(pen, new[] { new Point(5, 4), new Point(8, 1), new Point(11, 4) });
                            g.DrawLines(pen, new[] { new Point(5, 12), new Point(8, 15), new Point(11, 12) });
                            g.DrawLines(pen, new[] { new Point(4, 5), new Point(1, 8), new Point(4, 11) });
                            g.DrawLines(pen, new[] { new Point(12, 5), new Point(15, 8), new Point(12, 11) });
                        }
                        break;

                    case "clone":
                        using (var pen = new Pen(customColor ?? Color.FromArgb(124, 58, 237), 1.5f))
                        {
                            g.DrawEllipse(pen, 3, 3, 10, 10);
                            g.DrawLine(pen, 8, 1, 8, 15);
                            g.DrawLine(pen, 1, 8, 15, 8);
                        }
                        break;

                    case "color":
                        using (var b = new SolidBrush(customColor ?? Color.FromArgb(255, 50, 90)))
                            g.FillRectangle(b, 2, 2, 12, 12);
                        using (var pen = new Pen(Color.FromArgb(80, 80, 80), 1.2f))
                            g.DrawRectangle(pen, 2, 2, 12, 12);
                        break;

                    case "split":
                        using (var bLeft = new SolidBrush(Color.FromArgb(147, 197, 253)))
                            g.FillRectangle(bLeft, 2, 2, 6, 12);
                        using (var bRight = new SolidBrush(Color.FromArgb(229, 231, 235)))
                            g.FillRectangle(bRight, 8, 2, 6, 12);
                        using (var pen = new Pen(primary, 1.2f))
                        {
                            g.DrawRectangle(pen, 2, 2, 12, 12);
                            g.DrawLine(new Pen(accent, 1.8f), 8, 1, 8, 15);
                        }
                        break;

                    case "align":
                        using (var pen = new Pen(primary, 1.5f))
                        {
                            g.DrawArc(pen, 1, 4, 14, 8, 0, 360);
                            using (var b = new SolidBrush(accent))
                                g.FillEllipse(b, 6, 6, 4, 4);
                        }
                        break;

                    case "checker":
                        using (var bGray = new SolidBrush(Color.FromArgb(156, 163, 175)))
                        using (var bLight = new SolidBrush(Color.White))
                        {
                            for (int r = 0; r < 3; r++)
                                for (int c = 0; c < 3; c++)
                                    g.FillRectangle(((r + c) % 2 == 0) ? bGray : bLight, 2 + c * 4, 2 + r * 4, 4, 4);
                        }
                        using (var pen = new Pen(primary, 1f))
                            g.DrawRectangle(pen, 2, 2, 12, 12);
                        break;

                    case "alpha":
                        using (var f = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var b = new SolidBrush(accent))
                            g.DrawString("A", f, b, 3, 1);
                        using (var pen = new Pen(primary, 1.2f))
                            g.DrawRectangle(pen, 2, 2, 12, 12);
                        break;

                    case "zoom_in":
                        using (var pen = new Pen(primary, 1.5f))
                        {
                            g.DrawEllipse(pen, 2, 2, 9, 9);
                            g.DrawLine(pen, 10, 10, 14, 14);
                            g.DrawLine(pen, 4, 6.5f, 9, 6.5f);
                            g.DrawLine(pen, 6.5f, 4, 6.5f, 9);
                        }
                        break;

                    case "zoom_out":
                        using (var pen = new Pen(primary, 1.5f))
                        {
                            g.DrawEllipse(pen, 2, 2, 9, 9);
                            g.DrawLine(pen, 10, 10, 14, 14);
                            g.DrawLine(pen, 4, 6.5f, 9, 6.5f);
                        }
                        break;

                    case "zoom_fit":
                        using (var pen = new Pen(primary, 1.5f))
                        {
                            g.DrawLines(pen, new[] { new Point(2, 6), new Point(2, 2), new Point(6, 2) });
                            g.DrawLines(pen, new[] { new Point(10, 2), new Point(14, 2), new Point(14, 6) });
                            g.DrawLines(pen, new[] { new Point(2, 10), new Point(2, 14), new Point(6, 14) });
                            g.DrawLines(pen, new[] { new Point(10, 14), new Point(14, 14), new Point(14, 10) });
                        }
                        break;

                    case "zoom_1to1":
                        using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var b = new SolidBrush(primary))
                            g.DrawString("1:1", f, b, 0, 2);
                        break;

                    case "undo":
                        using (var pen = new Pen(primary, 1.6f))
                        {
                            g.DrawArc(pen, 4, 4, 9, 9, 180, 180);
                            g.DrawLines(pen, new[] { new Point(2, 6), new Point(4, 9), new Point(7, 6) });
                        }
                        break;

                    case "redo":
                        using (var pen = new Pen(primary, 1.6f))
                        {
                            g.DrawArc(pen, 3, 4, 9, 9, 180, 180);
                            g.DrawLines(pen, new[] { new Point(9, 6), new Point(12, 9), new Point(14, 6) });
                        }
                        break;

                    case "clear":
                        using (var pen = new Pen(Color.FromArgb(220, 38, 38), 1.4f))
                        {
                            g.DrawRectangle(pen, 4, 4, 8, 10);
                            g.DrawLine(pen, 2, 4, 14, 4);
                            g.DrawLine(pen, 6, 2, 10, 2);
                            g.DrawLine(pen, 6.5f, 6, 6.5f, 12);
                            g.DrawLine(pen, 9.5f, 6, 9.5f, 12);
                        }
                        break;

                    case "save":
                        using (var b = new SolidBrush(Color.FromArgb(16, 124, 65)))
                            g.FillRectangle(b, 2, 2, 12, 12);
                        using (var bWhite = new SolidBrush(Color.White))
                        {
                            g.FillRectangle(bWhite, 4, 2, 8, 5);
                            g.FillRectangle(bWhite, 4, 9, 8, 5);
                        }
                        break;

                    case "new_slot":
                        using (var b = new SolidBrush(Color.FromArgb(0, 120, 215)))
                            g.FillEllipse(b, 1, 1, 14, 14);
                        using (var pen = new Pen(Color.White, 2f))
                        {
                            g.DrawLine(pen, 8, 4, 8, 12);
                            g.DrawLine(pen, 4, 8, 12, 8);
                        }
                        break;

                    case "folder":
                        using (var b = new SolidBrush(Color.FromArgb(245, 158, 11)))
                        {
                            g.FillRectangle(b, 1, 3, 6, 3);
                            g.FillRectangle(b, 1, 5, 14, 9);
                        }
                        using (var pen = new Pen(Color.FromArgb(180, 83, 9), 1f))
                            g.DrawRectangle(pen, 1, 5, 13, 8);
                        break;

                    case "import":
                        using (var pen = new Pen(accent, 1.6f))
                        {
                            g.DrawLine(pen, 8, 2, 8, 10);
                            g.DrawLines(pen, new[] { new Point(5, 7), new Point(8, 10), new Point(11, 7) });
                            g.DrawLines(pen, new[] { new Point(2, 10), new Point(2, 14), new Point(14, 14), new Point(14, 10) });
                        }
                        break;

                    default:
                        using (var pen = new Pen(primary, 1.5f))
                            g.DrawRectangle(pen, 3, 3, 10, 10);
                        break;
                }
            }
            return bmp;
        }

        public static Bitmap RgbaToBitmap(byte[] rgba, int width, int height, bool showAlpha = true)
        {
            if (rgba == null || width <= 0 || height <= 0)
                return null;

            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int numPixels = width * height;
                byte[] bgra = new byte[numPixels * 4];
                for (int i = 0; i < numPixels; i++)
                {
                    int p = i * 4;
                    bgra[p] = rgba[p + 2];     // B
                    bgra[p + 1] = rgba[p + 1]; // G
                    bgra[p + 2] = rgba[p];     // R
                    bgra[p + 3] = showAlpha ? rgba[p + 3] : (byte)255; // A
                }
                Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        public static byte[] BitmapToStraightRgba(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            byte[] rgba = new byte[w * h * 4];

            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] raw = new byte[stride * h];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);

                for (int y = 0; y < h; y++)
                {
                    int srcRow = y * stride;
                    int dstRow = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        int s = srcRow + x * 4;
                        int d = dstRow + x * 4;
                        rgba[d] = raw[s + 2];     // R
                        rgba[d + 1] = raw[s + 1]; // G
                        rgba[d + 2] = raw[s];     // B
                        rgba[d + 3] = raw[s + 3]; // A
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return rgba;
        }
    }
}
