using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using YTDEditor.Core;

namespace YTDEditor.UI
{
    public static class PromptDialog
    {
        public static string ShowTextPrompt(IWin32Window owner, string title, string labelText, string defaultValue = "")
        {
            using (var form = new Form())
            {
                form.AutoScaleMode = AutoScaleMode.None;
                form.Font = DarkTheme.FontRegular;
                form.Text = title;
                form.Size = new Size(440, 165);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var lbl = new Label { Text = labelText, Location = new Point(14, 16), AutoSize = true };
                var txt = new TextBox { Text = defaultValue, Location = new Point(14, 42), Width = 395 };
                var btnOk = new Button { Text = Loc.T("Tamam", "OK", "OK"), Location = new Point(235, 80), Size = new Size(84, 30), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = Loc.T("İptal", "Cancel", "Abbrechen"), Location = new Point(325, 80), Size = new Size(84, 30), DialogResult = DialogResult.Cancel };

                form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;
                DarkTheme.ApplyDarkThemeRecursive(form);
                btnOk.BackColor = DarkTheme.BgButtonActive;
                btnOk.ForeColor = Color.White;

                return form.ShowDialog(owner) == DialogResult.OK ? txt.Text.Trim() : null;
            }
        }
    }

    public class SettingsForm : Form
    {
        private readonly AppConfig _config;
        private TextBox _txtCwDir;
        private TextBox _txtDdsTool;
        private CheckBox _chkOverwrite;
        private CheckBox _chkBlockPatch;

        public SettingsForm(AppConfig config)
        {
            _config = config;
            AutoScaleMode = AutoScaleMode.None;
            Font = DarkTheme.FontRegular;
            Text = Loc.T("YTD Editor - Ayarlar", "YTD Editor - Settings", "YTD Editor - Einstellungen");
            Size = new Size(650, 340);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var grpTools = new GroupBox
            {
                Text = Loc.T(
                    "Harici Araç Yolları (CodeWalker ve DDS Encoder)",
                    "External Tool Paths (CodeWalker & DDS Encoder)",
                    "Externe Werkzeugpfade (CodeWalker & DDS-Encoder)"),
                Location = new Point(14, 12),
                Size = new Size(606, 125)
            };

            var lblCw = new Label
            {
                Text = Loc.T(
                    "CodeWalker Klasörü (CodeWalker.Core.dll):",
                    "CodeWalker Folder (CodeWalker.Core.dll):",
                    "CodeWalker-Ordner (CodeWalker.Core.dll):"),
                Location = new Point(12, 26),
                AutoSize = true
            };
            _txtCwDir = new TextBox { Text = _config.CodeWalkerDir, Location = new Point(12, 46), Width = 472 };
            var btnCw = new Button
            {
                Text = Loc.T("Klasör Seç...", "Browse...", "Ordner..."),
                Location = new Point(492, 44),
                Size = new Size(100, 26)
            };
            btnCw.Click += (s, e) =>
            {
                using (var fbd = new FolderBrowserDialog
                {
                    SelectedPath = _txtCwDir.Text,
                    Description = Loc.T("CodeWalker.Core.dll içeren klasörü seçin", "Select folder containing CodeWalker.Core.dll", "Ordner mit CodeWalker.Core.dll auswählen")
                })
                {
                    if (fbd.ShowDialog(this) == DialogResult.OK)
                        _txtCwDir.Text = fbd.SelectedPath;
                }
            };

            var lblDds = new Label
            {
                Text = Loc.T(
                    "DDS Encoder (texconv.exe veya CompressonatorCLI.exe):",
                    "DDS Encoder (texconv.exe or CompressonatorCLI.exe):",
                    "DDS-Encoder (texconv.exe oder CompressonatorCLI.exe):"),
                Location = new Point(12, 74),
                AutoSize = true
            };
            _txtDdsTool = new TextBox { Text = _config.DdsToolPath, Location = new Point(12, 94), Width = 472 };
            var btnDds = new Button
            {
                Text = Loc.T("Dosya Seç...", "Browse...", "Datei..."),
                Location = new Point(492, 92),
                Size = new Size(100, 26)
            };
            btnDds.Click += (s, e) =>
            {
                using (var ofd = new OpenFileDialog { Filter = "EXE (*.exe)|*.exe|*.*|*.*", FileName = _txtDdsTool.Text })
                {
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                        _txtDdsTool.Text = ofd.FileName;
                }
            };

            grpTools.Controls.AddRange(new Control[] { lblCw, _txtCwDir, btnCw, lblDds, _txtDdsTool, btnDds });

            var grpOut = new GroupBox
            {
                Text = Loc.T(
                    "Kayıt ve Renk / Blok Koruma Ayarları",
                    "Save & Color / Block Preservation Settings",
                    "Speicher- & Farb- / Block-Schutzeinstellungen"),
                Location = new Point(14, 145),
                Size = new Size(606, 100)
            };
            _chkOverwrite = new CheckBox
            {
                Text = Loc.T(
                    "Orijinal dosyanın üstüne yaz (Otomatik .bak yedeği alınır; kapalıysa 'output' klasörüne yazar)",
                    "Overwrite original file (Creates automatic .bak backup; otherwise writes to 'output' folder)",
                    "Originaldatei überschreiben (Erstellt .bak-Backup; sonst Speicherung im 'output'-Ordner)"),
                Checked = _config.OverwriteOriginal,
                Location = new Point(14, 26),
                AutoSize = true
            };
            _chkBlockPatch = new CheckBox
            {
                Text = Loc.T(
                    "4x4 DXT5 Blok Seviyesi Kayıpsız Koruma (Maskeye değmeyen blokların orijinal baytlarını koru)",
                    "4x4 DXT5 Block-Level Lossless Preservation (Keep original bytes of untouched blocks)",
                    "Verlustfreier 4x4 DXT5-Blockschutz (Original-Bytes unberührter Blöcke beibehalten)"),
                Checked = _config.BlockLevelDxt5Patch,
                Location = new Point(14, 56),
                AutoSize = true
            };
            grpOut.Controls.AddRange(new Control[] { _chkOverwrite, _chkBlockPatch });

            var btnSave = new Button { Text = Loc.T("Kaydet", "Save", "Speichern"), Location = new Point(425, 255), Size = new Size(95, 32) };
            var btnCancel = new Button { Text = Loc.T("İptal", "Cancel", "Abbrechen"), Location = new Point(525, 255), Size = new Size(95, 32), DialogResult = DialogResult.Cancel };

            btnSave.Click += (s, e) =>
            {
                _config.CodeWalkerDir = _txtCwDir.Text.Trim();
                _config.DdsToolPath = _txtDdsTool.Text.Trim();
                _config.OverwriteOriginal = _chkOverwrite.Checked;
                _config.BlockLevelDxt5Patch = _chkBlockPatch.Checked;
                _config.Save();
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.AddRange(new Control[] { grpTools, grpOut, btnSave, btnCancel });
            DarkTheme.ApplyDarkThemeRecursive(this);
            btnSave.BackColor = DarkTheme.BgButtonActive;
            btnSave.ForeColor = Color.White;
        }
    }

    public class NewSlotForm : Form
    {
        private readonly SlotGroup _group;
        private readonly VariantEntry _sourceVariant;
        private readonly byte[] _cleanedRgba;
        private readonly int _imgW;
        private readonly int _imgH;
        private readonly string _sampleTexName;

        private TextBox _txtLetter;
        private Label _lblNewYtd;
        private Label _lblNewTex;
        private CheckBox _chkHsb;
        private TrackBar _sliderH;
        private TrackBar _sliderS;
        private TrackBar _sliderB;
        private Label _lblH;
        private Label _lblS;
        private Label _lblB;
        private PictureBox _picPreview;

        public int OriginalDisposition { get; private set; } = 0; // 1 = keep original, 2 = update original (.bak), 0 = cancel

        public NewSlotForm(
            SlotGroup group,
            VariantEntry sourceVariant,
            byte[] cleanedRgba,
            int imgW,
            int imgH,
            string sampleTexName)
        {
            _group = group;
            _sourceVariant = sourceVariant;
            _cleanedRgba = cleanedRgba;
            _imgW = imgW;
            _imgH = imgH;
            _sampleTexName = sampleTexName;

            AutoScaleMode = AutoScaleMode.None;
            Font = DarkTheme.FontRegular;
            Text = Loc.T("Yeni Slot Olarak Kaydet", "Save as New Slot", "Als neuen Slot speichern");
            Size = new Size(650, 435);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var grpSlot = new GroupBox
            {
                Text = Loc.T("Yeni Varyant Harfi ve İsimlendirme", "New Variant Letter & Naming", "Neuer Variantenbuchstabe & Benennung"),
                Location = new Point(14, 12),
                Size = new Size(606, 126)
            };
            string existing = string.Join(", ", _group.VariantLetters.Select(v => v.ToUpperInvariant()));
            var lblExist = new Label
            {
                Text = Loc.T($"Mevcut Varyantlar: {existing}", $"Existing Variants: {existing}", $"Vorhandene Varianten: {existing}"),
                Location = new Point(14, 24),
                AutoSize = true
            };

            var lblLet = new Label
            {
                Text = Loc.T("Yeni Varyant Harfi:", "New Variant Letter:", "Neuer Variantenbuchstabe:"),
                Location = new Point(14, 50),
                AutoSize = true
            };
            _txtLetter = new TextBox { Text = _group.NextAvailableLetter(), Location = new Point(160, 47), Width = 54, MaxLength = 2 };
            _txtLetter.TextChanged += (s, e) => UpdateNameLabels();

            _lblNewYtd = new Label { Location = new Point(14, 78), AutoSize = true };
            _lblNewTex = new Label { Location = new Point(14, 98), AutoSize = true };
            grpSlot.Controls.AddRange(new Control[] { lblExist, lblLet, _txtLetter, _lblNewYtd, _lblNewTex });

            var grpHsb = new GroupBox
            {
                Text = Loc.T(
                    "İsteğe Bağlı Renk Tonu Ayarı (Canlı Önizlemeli - Alfa Korunur)",
                    "Optional HSB Color Adjustment (Live Preview - Alpha Preserved)",
                    "Optionale HSB-Farbanpassung (Live-Vorschau - Alpha bleibt erhalten)"),
                Location = new Point(14, 146),
                Size = new Size(606, 198)
            };
            _chkHsb = new CheckBox
            {
                Text = Loc.T(
                    "Renk Tonu (Hue / Saturation / Brightness) Ayarını Etkinleştir (Varsayılan: Kapalı)",
                    "Enable Hue / Saturation / Brightness Adjustment (Default: Off)",
                    "Farbton / Sättigung / Helligkeit aktivieren (Standard: Aus)"),
                Checked = false,
                Location = new Point(14, 24),
                AutoSize = true
            };
            _chkHsb.CheckedChanged += (s, e) => OnHsbChanged();

            var lblHT = new Label { Text = "Hue:", Location = new Point(14, 58), AutoSize = true };
            _sliderH = new TrackBar { Minimum = -180, Maximum = 180, Value = 0, TickStyle = TickStyle.None, AutoSize = false, Size = new Size(230, 26), Location = new Point(106, 54), Enabled = false };
            _lblH = new Label { Text = "0°", Location = new Point(342, 58), AutoSize = true };

            var lblST = new Label { Text = "Saturation:", Location = new Point(14, 96), AutoSize = true };
            _sliderS = new TrackBar { Minimum = -100, Maximum = 100, Value = 0, TickStyle = TickStyle.None, AutoSize = false, Size = new Size(230, 26), Location = new Point(106, 92), Enabled = false };
            _lblS = new Label { Text = "%0", Location = new Point(342, 96), AutoSize = true };

            var lblBT = new Label { Text = "Brightness:", Location = new Point(14, 134), AutoSize = true };
            _sliderB = new TrackBar { Minimum = -100, Maximum = 100, Value = 0, TickStyle = TickStyle.None, AutoSize = false, Size = new Size(230, 26), Location = new Point(106, 130), Enabled = false };
            _lblB = new Label { Text = "%0", Location = new Point(342, 134), AutoSize = true };

            _sliderH.ValueChanged += (s, e) => OnHsbChanged();
            _sliderS.ValueChanged += (s, e) => OnHsbChanged();
            _sliderB.ValueChanged += (s, e) => OnHsbChanged();

            _picPreview = new PictureBox
            {
                Location = new Point(420, 42),
                Size = new Size(164, 142),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(243, 244, 246)
            };

            grpHsb.Controls.AddRange(new Control[] { _chkHsb, lblHT, _sliderH, _lblH, lblST, _sliderS, _lblS, lblBT, _sliderB, _lblB, _picPreview });

            var btnCreate = new Button
            {
                Text = Loc.T("Yeni Slotu Oluştur...", "Create New Slot...", "Neuen Slot erstellen..."),
                Location = new Point(330, 354),
                Size = new Size(185, 34)
            };
            var btnCancel = new Button
            {
                Text = Loc.T("İptal", "Cancel", "Abbrechen"),
                Location = new Point(525, 354),
                Size = new Size(95, 34),
                DialogResult = DialogResult.Cancel
            };

            btnCreate.Click += OnProceedClicked;

            Controls.AddRange(new Control[] { grpSlot, grpHsb, btnCreate, btnCancel });
            DarkTheme.ApplyDarkThemeRecursive(this);
            btnCreate.BackColor = DarkTheme.BgAccent;
            btnCreate.ForeColor = Color.White;

            UpdateNameLabels();
            UpdateLivePreview();
        }

        public string GetNewLetter()
        {
            string s = _txtLetter.Text.Trim().ToLowerInvariant();
            return (s.Length > 0 && char.IsLetter(s[0])) ? s[0].ToString() : "d";
        }

        public HsbSettings GetHsbSettings()
        {
            return new HsbSettings
            {
                Enabled = _chkHsb.Checked,
                HueShift = _sliderH.Value,
                SaturationShift = _sliderS.Value,
                BrightnessShift = _sliderB.Value
            };
        }

        private void UpdateNameLabels()
        {
            string let = GetNewLetter();
            string newStem = _group.BuildVariantStem(let, _sourceVariant);
            string newTex = SlotGrouper.ReplaceVariantLetterInName(_sampleTexName, _sourceVariant.VariantLetter, let);
            _lblNewYtd.Text = Loc.T($"Yeni YTD Dosya Adı:  {newStem}.ytd", $"New YTD File Name:  {newStem}.ytd", $"Neuer YTD-Dateiname:  {newStem}.ytd");
            _lblNewTex.Text = Loc.T($"Yeni YTD İçi Doku Adı:  {newTex}", $"New Internal Texture Name:  {newTex}", $"Neuer interner Texturname:  {newTex}");
        }

        private void OnHsbChanged()
        {
            bool en = _chkHsb.Checked;
            _sliderH.Enabled = en;
            _sliderS.Enabled = en;
            _sliderB.Enabled = en;
            _lblH.Text = $"{_sliderH.Value}°";
            _lblS.Text = $"%{_sliderS.Value}";
            _lblB.Text = $"%{_sliderB.Value}";
            UpdateLivePreview();
        }

        private void UpdateLivePreview()
        {
            if (_cleanedRgba == null || _imgW <= 0 || _imgH <= 0) return;

            int pw = Math.Min(160, _imgW);
            int ph = Math.Min(160, _imgH);
            byte[] small = new byte[pw * ph * 4];
            for (int y = 0; y < ph; y++)
            {
                int sy = y * _imgH / ph;
                for (int x = 0; x < pw; x++)
                {
                    int sx = x * _imgW / pw;
                    int s4 = (sy * _imgW + sx) * 4;
                    int d4 = (y * pw + x) * 4;
                    small[d4] = _cleanedRgba[s4];
                    small[d4 + 1] = _cleanedRgba[s4 + 1];
                    small[d4 + 2] = _cleanedRgba[s4 + 2];
                    small[d4 + 3] = _cleanedRgba[s4 + 3];
                }
            }

            var hsb = GetHsbSettings();
            if (hsb.IsActive)
                small = InpaintEngine.ApplyHsbAdjustment(small, pw, ph, hsb);

            var oldImg = _picPreview.Image;
            _picPreview.Image = DarkTheme.RgbaToBitmap(small, pw, ph, true);
            oldImg?.Dispose();
        }

        private void OnProceedClicked(object sender, EventArgs e)
        {
            string let = GetNewLetter();
            string newYtdName = _group.BuildVariantStem(let, _sourceVariant) + ".ytd";

            // Mandatory confirmation dialog every single time without "Bir daha sorma"
            int disp = AskOriginalFileDisposition(this, _sourceVariant.FileName, newYtdName);
            if (disp == 0) return;

            OriginalDisposition = disp;
            DialogResult = DialogResult.OK;
            Close();
        }

        public static int AskOriginalFileDisposition(IWin32Window owner, string sourceFileName, string newSlotFileName)
        {
            using (var dlg = new Form())
            {
                dlg.AutoScaleMode = AutoScaleMode.None;
                dlg.Font = DarkTheme.FontRegular;
                dlg.Text = Loc.T("Orijinal dosya ne olsun?", "What should happen to the original file?", "Was soll mit der Originaldatei geschehen?");
                dlg.Size = new Size(540, 245);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var lbl = new Label
                {
                    Text = Loc.T(
                        $"Yeni slot oluşturuluyor: {newSlotFileName}\nKaynak varyant: {sourceFileName}\n\nOrijinal dosya ne olsun?",
                        $"Creating new slot: {newSlotFileName}\nSource variant: {sourceFileName}\n\nWhat should happen to the original file?",
                        $"Neuer Slot wird erstellt: {newSlotFileName}\nQuellvariante: {sourceFileName}\n\nWas soll mit der Originaldatei geschehen?"),
                    Location = new Point(16, 14),
                    Size = new Size(495, 62),
                    Font = DarkTheme.FontBold
                };

                int choice = 0;

                var btn1 = new Button
                {
                    Text = Loc.T(
                        "1) Orijinale dokunma, sadece yeni slot oluşsun",
                        "1) Keep original untouched, only create new slot",
                        "1) Original nicht ändern, nur neuen Slot erstellen"),
                    Location = new Point(16, 82),
                    Size = new Size(492, 34)
                };
                var btn2 = new Button
                {
                    Text = Loc.T(
                        "2) Orijinal de temizlenmiş haliyle güncellensin (.bak yedeği alınarak)",
                        "2) Also update original with cleaned texture (creates .bak backup)",
                        "2) Original ebenfalls bereinigt aktualisieren (mit .bak-Backup)"),
                    Location = new Point(16, 122),
                    Size = new Size(492, 34)
                };
                var btn3 = new Button
                {
                    Text = Loc.T("3) İptal", "3) Cancel", "3) Abbrechen"),
                    Location = new Point(16, 162),
                    Size = new Size(492, 32)
                };

                btn1.Click += (s, e) => { choice = 1; dlg.DialogResult = DialogResult.OK; dlg.Close(); };
                btn2.Click += (s, e) => { choice = 2; dlg.DialogResult = DialogResult.OK; dlg.Close(); };
                btn3.Click += (s, e) => { choice = 0; dlg.DialogResult = DialogResult.Cancel; dlg.Close(); };

                dlg.Controls.AddRange(new Control[] { lbl, btn1, btn2, btn3 });
                DarkTheme.ApplyDarkThemeRecursive(dlg);
                lbl.Font = DarkTheme.FontBold;
                btn1.BackColor = DarkTheme.BgButtonActive;
                btn1.ForeColor = Color.White;
                btn2.BackColor = DarkTheme.BgSuccess;
                btn2.ForeColor = Color.White;

                dlg.ShowDialog(owner);
                return choice;
            }
        }
    }

    public class ImportOptionsForm : Form
    {
        private RadioButton _rbScalePreserve;
        private RadioButton _rbScaleStretch;
        private RadioButton _rbAcceptNew;
        public Size FixedNewSize { get; private set; }

        public ImportOptionsForm(int origW, int origH, int impW, int impH)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = DarkTheme.FontRegular;
            Text = Loc.T("Görsel İçe Aktarma (Import) Boyut Seçenekleri", "Image Import Dimension Options", "Bildimport-Größenoptionen");
            Size = new Size(550, 265);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            int fw = Math.Max(4, ((impW + 2) / 4) * 4);
            int fh = Math.Max(4, ((impH + 2) / 4) * 4);
            FixedNewSize = new Size(fw, fh);

            bool notMult4 = (impW % 4 != 0) || (impH % 4 != 0);
            string msg = Loc.T(
                $"Mevcut Doku Boyutu: {origW} x {origH} px\nİçe Aktarılan Görsel Boyutu: {impW} x {impH} px",
                $"Current Texture Size: {origW} x {origH} px\nImported Image Size: {impW} x {impH} px",
                $"Aktuelle Texturgröße: {origW} x {origH} px\nImportierte Bildgröße: {impW} x {impH} px");
            if (notMult4)
            {
                msg += Loc.T(
                    $"\nUYARI: DXT5 için boyutlar 4'ün katı olmalıdır! Yeni boyut seçilirse {fw} x {fh} px olarak düzeltilecektir.",
                    $"\nWARNING: DXT5 dimensions must be a multiple of 4! If new size is accepted, it will be adjusted to {fw} x {fh} px.",
                    $"\nWARNUNG: DXT5-Maße müssen durch 4 teilbar sein! Bei Annahme wird die Größe auf {fw} x {fh} px angepasst.");
            }

            var lbl = new Label { Text = msg, Location = new Point(16, 14), Size = new Size(505, 62) };

            _rbScalePreserve = new RadioButton
            {
                Text = Loc.T(
                    $"Orijinal boyuta ({origW} x {origH}) ölçekle (En/boy oranını koruyarak)",
                    $"Scale to original size ({origW} x {origH}) preserving aspect ratio",
                    $"Auf Originalgröße ({origW} x {origH}) skalieren (Seitenverhältnis beibehalten)"),
                Checked = true,
                Location = new Point(18, 82),
                AutoSize = true
            };
            _rbScaleStretch = new RadioButton
            {
                Text = Loc.T(
                    $"Orijinal boyuta ({origW} x {origH}) tam oturt",
                    $"Stretch to exact original size ({origW} x {origH})",
                    $"Exakt auf Originalgröße ({origW} x {origH}) anpassen"),
                Location = new Point(18, 110),
                AutoSize = true
            };
            _rbAcceptNew = new RadioButton
            {
                Text = Loc.T(
                    $"Yeni boyutu kabul et ({fw} x {fh} px)",
                    $"Accept new dimensions ({fw} x {fh} px)",
                    $"Neue Bildgröße übernehmen ({fw} x {fh} px)"),
                Location = new Point(18, 138),
                AutoSize = true
            };

            var btnOk = new Button
            {
                Text = Loc.T("İçe Aktar (Alfa Korunarak)", "Import (Preserve Alpha)", "Importieren (Alpha behalten)"),
                Location = new Point(240, 180),
                Size = new Size(185, 32),
                DialogResult = DialogResult.OK
            };
            var btnCancel = new Button
            {
                Text = Loc.T("İptal", "Cancel", "Abbrechen"),
                Location = new Point(435, 180),
                Size = new Size(85, 32),
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[] { lbl, _rbScalePreserve, _rbScaleStretch, _rbAcceptNew, btnOk, btnCancel });
            DarkTheme.ApplyDarkThemeRecursive(this);
            btnOk.BackColor = DarkTheme.BgButtonActive;
            btnOk.ForeColor = Color.White;
        }

        public string SelectedMode
        {
            get
            {
                if (_rbScalePreserve.Checked) return "scale_preserve";
                if (_rbScaleStretch.Checked) return "scale_stretch";
                return "accept_new";
            }
        }
    }

    public class BatchAllGroupsForm : Form
    {
        private readonly List<MaskTemplateInfo> _templates;
        private readonly List<SlotGroup> _groups;
        private CheckedListBox _chkList;

        public BatchAllGroupsForm(List<MaskTemplateInfo> templates, List<SlotGroup> groups)
        {
            _templates = templates;
            _groups = groups;
            int totalFiles = _groups.Sum(g => g.Variants.Count);

            AutoScaleMode = AutoScaleMode.None;
            Font = DarkTheme.FontRegular;
            Text = Loc.T(
                "Klasördeki Tüm Gruplara Toplu Şablon Uygula",
                "Apply Templates to All Groups in Folder",
                "Vorlagen auf alle Gruppen im Ordner anwenden");
            Size = new Size(570, 415);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var lbl = new Label
            {
                Text = Loc.T(
                    $"Klasörde toplam {_groups.Count} slot grubu ve {totalFiles} adet YTD dosyası bulundu.\nUygulamak istediğiniz bir veya birden fazla maske şablonunu işaretleyin:",
                    $"Found {_groups.Count} slot groups and {totalFiles} YTD files in folder.\nCheck one or more mask templates to apply across all groups:",
                    $"{_groups.Count} Slot-Gruppen und {totalFiles} YTD-Dateien im Ordner gefunden.\nWählen Sie eine oder mehrere Maskenvorlagen zur Anwendung aus:"),
                Location = new Point(14, 14),
                Size = new Size(525, 42)
            };

            _chkList = new CheckedListBox
            {
                Location = new Point(14, 60),
                Size = new Size(525, 255),
                CheckOnClick = true
            };
            for (int i = 0; i < _templates.Count; i++)
            {
                var t = _templates[i];
                _chkList.Items.Add($"{t.Name} (Ref: {t.RefWidth}x{t.RefHeight}, Dilate:{t.Dilate}, Feather:{t.Feather})", i == 0);
            }

            var btnAll = new Button
            {
                Text = Loc.T("Tümünü Seç", "Select All", "Alle auswählen"),
                Location = new Point(14, 326),
                Size = new Size(106, 32)
            };
            btnAll.Click += (s, e) =>
            {
                for (int i = 0; i < _chkList.Items.Count; i++)
                    _chkList.SetItemChecked(i, true);
            };

            var btnRun = new Button
            {
                Text = Loc.T(
                    $"Seçili Şablonları {totalFiles} Dosyaya Uygula...",
                    $"Apply Selected to {totalFiles} Files...",
                    $"Auf {totalFiles} Dateien anwenden..."),
                Location = new Point(215, 326),
                Size = new Size(230, 32)
            };
            var btnCancel = new Button
            {
                Text = Loc.T("İptal", "Cancel", "Abbrechen"),
                Location = new Point(455, 326),
                Size = new Size(84, 32),
                DialogResult = DialogResult.Cancel
            };

            btnRun.Click += (s, e) =>
            {
                var sel = GetSelectedTemplates();
                if (sel.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        Loc.T("Lütfen en az bir maske şablonu işaretleyin.", "Please select at least one mask template.", "Bitte wählen Sie mindestens eine Maskenvorlage aus."),
                        Loc.T("Uyarı", "Warning", "Warnung"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
                string names = string.Join(", ", sel.Select(x => x.Name));
                var ans = MessageBox.Show(
                    this,
                    Loc.T(
                        $"Seçilen Şablonlar: {names}\nEtkilenecek Grup Sayısı: {_groups.Count}\nEtkilenecek Toplam YTD Dosyası Sayısı: {totalFiles}\n\nToplu işlemi başlatmak istediğinize emin misiniz?",
                        $"Selected Templates: {names}\nAffected Slot Groups: {_groups.Count}\nTotal Affected YTD Files: {totalFiles}\n\nAre you sure you want to start batch processing?",
                        $"Ausgewählte Vorlagen: {names}\nBetroffene Gruppen: {_groups.Count}\nBetroffene YTD-Dateien gesamt: {totalFiles}\n\nMöchten Sie die Stapelverarbeitung starten?"),
                    Loc.T("Toplu İşlem Onayı", "Confirm Batch Processing", "Stapelverarbeitung bestätigen"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (ans == DialogResult.Yes)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };

            Controls.AddRange(new Control[] { lbl, _chkList, btnAll, btnRun, btnCancel });
            DarkTheme.ApplyDarkThemeRecursive(this);
            btnRun.BackColor = DarkTheme.BgButtonActive;
            btnRun.ForeColor = Color.White;
        }

        public List<MaskTemplateInfo> GetSelectedTemplates()
        {
            var list = new List<MaskTemplateInfo>();
            for (int i = 0; i < _chkList.Items.Count; i++)
            {
                if (_chkList.GetItemChecked(i))
                    list.Add(_templates[i]);
            }
            return list;
        }
    }

    public class ReportForm : Form
    {
        public ReportForm(string title, string headerText, IEnumerable<string> lines)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = DarkTheme.FontRegular;
            Text = title;
            Size = new Size(740, 480);
            StartPosition = FormStartPosition.CenterParent;

            var lbl = new Label
            {
                Text = headerText,
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(10, 8, 10, 4),
                Font = DarkTheme.FontBold
            };

            var txt = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = DarkTheme.FontMono,
                Text = string.Join(Environment.NewLine, lines)
            };

            var pnlBot = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            var btnClose = new Button
            {
                Text = Loc.T("Kapat", "Close", "Schließen"),
                Size = new Size(100, 30),
                Location = new Point(615, 7),
                DialogResult = DialogResult.OK
            };
            pnlBot.Controls.Add(btnClose);
            pnlBot.Resize += (s, e) => btnClose.Left = pnlBot.Width - 114;

            Controls.Add(txt);
            Controls.Add(lbl);
            Controls.Add(pnlBot);
            txt.BringToFront();

            DarkTheme.ApplyDarkThemeRecursive(this);
            lbl.Font = DarkTheme.FontBold;
            txt.Font = DarkTheme.FontMono;
            btnClose.BackColor = DarkTheme.BgButtonActive;
            btnClose.ForeColor = Color.White;
        }
    }
}
