using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using YTDEditor.Core;

namespace YTDEditor.UI
{
    public class ImportedOverrideEntry
    {
        public byte[] Rgba { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class MainForm : Form
    {
        private readonly AppConfig _config;
        private readonly DdsCodec _ddsCodec;
        private readonly YtdService _ytdService;
        private readonly TemplateManager _templateManager;
        private readonly ToolTip _toolTip;

        // State
        private string _currentFolder;
        private List<SlotGroup> _slotGroups = new List<SlotGroup>();
        private SlotGroup _activeGroup;
        private VariantEntry _activeVariant;

        // Independent mask state per numbered slot group key (000_* never leaks to 001_*)
        private readonly Dictionary<string, GroupMaskState> _groupMasks =
            new Dictionary<string, GroupMaskState>(StringComparer.OrdinalIgnoreCase);

        // Cached loaded YTD entries for active group: variantLetter -> List<YtdTextureEntry>
        private readonly Dictionary<string, List<YtdTextureEntry>> _groupYtdCache =
            new Dictionary<string, List<YtdTextureEntry>>(StringComparer.OrdinalIgnoreCase);

        private int _activeTextureIndex = 0;

        // Imported overrides: "variantLetter|texName" -> ImportedOverrideEntry
        private readonly Dictionary<string, ImportedOverrideEntry> _importedOverrides =
            new Dictionary<string, ImportedOverrideEntry>(StringComparer.OrdinalIgnoreCase);

        private byte[] _referencePatchRgba;

        // UI Controls - Top Toolbar under MenuStrip
        private Panel _topContainer;
        private MenuStrip _mainMenu;
        private Panel _toolBarPanel;
        private Button _btnToolBrush;
        private Button _btnToolEraser;
        private Button _btnToolRect;
        private Button _btnToolCircle;
        private Button _btnToolTriangle;
        private Button _btnToolMove;
        private Button _btnToolClonePick;
        private TrackBar _sliderBrushSize;
        private Label _lblBrushSize;
        private TrackBar _sliderMaskOpacity;
        private Label _lblMaskOpacity;
        private Button _btnMaskColor;
        private Button _btnToolSplit;
        private Button _btnToolAlignOverlay;
        private Button _btnToolZoomOut;
        private Button _btnToolZoomIn;
        private Button _btnToolZoomFit;
        private Button _btnToolZoom1to1;
        private CheckBox _chkToolChecker;
        private CheckBox _chkToolAlpha;
        private Button _btnToolUndo;
        private Button _btnToolRedo;
        private Button _btnToolClear;
        public Button BtnToolSave { get; private set; }
        public Button BtnToolSaveNewSlot { get; private set; }

        // UI Controls - Left
        private Button _btnOpenFolder;
        private TreeView _treeGroups;
        private ListBox _listTextures;
        private CheckBox _chkApplyNormSpec;
        private Button _btnImportTex;

        // UI Controls - Center
        private TabControl _tabVariants;
        private Label _lblGroupBadge;
        private Label _lblAlignmentBanner;
        private CanvasControl _canvas;

        // UI Controls - Right (Edge, FineTune, Fill, Batch, Templates)
        private NumericUpDown _numDilate;
        private NumericUpDown _numFeather;
        private NumericUpDown _numVarDx;
        private NumericUpDown _numVarDy;
        private NumericUpDown _numVarSx;
        private NumericUpDown _numVarSy;
        private bool _suppressFineTuneEvents;

        private ComboBox _cmbFillMethod;
        private Button _btnPickCloneSrc;
        private Panel _pnlDonorRow;
        private Label _lblDonor;
        private ComboBox _cmbDonorVariant;
        private ComboBox _cmbAlphaMode;

        private Button _btnBatchGroup;
        private Button _btnBatchFolder;

        // Template Manager controls
        private TextBox _txtNewTemplateName;
        private TextBox _txtSearchTemplates;
        private ListBox _listTemplates;
        private PictureBox _picTemplateThumb;
        private Label _lblTemplateMeta;
        private List<MaskTemplateInfo> _currentTemplateItems = new List<MaskTemplateInfo>();

        // Localized Menu Items & Groups
        private ToolStripMenuItem _mFile;
        private ToolStripMenuItem _mEdit;
        private ToolStripMenuItem _mBatch;
        private ToolStripMenuItem _mSettings;
        private ToolStripMenuItem _mSelfCheck;
        private ToolStripMenuItem _mLangMenu;
        private ToolStripMenuItem _mLangTr;
        private ToolStripMenuItem _mLangEn;
        private ToolStripMenuItem _mLangDe;

        private ToolStripMenuItem _miFileOpen;
        private ToolStripMenuItem _miFileImport;
        private ToolStripMenuItem _miFileSave;
        private ToolStripMenuItem _miFileSaveNew;
        private ToolStripMenuItem _miFileExit;

        private ToolStripMenuItem _miEditUndo;
        private ToolStripMenuItem _miEditRedo;
        private ToolStripMenuItem _miEditClear;

        private ToolStripMenuItem _miBatchGroup;
        private ToolStripMenuItem _miBatchFolder;

        private GroupBox _grpSlots;
        private GroupBox _grpTexs;
        private GroupBox _grpFill;
        private GroupBox _grpEdge;
        private GroupBox _grpBatch;
        private GroupBox _grpTemplates;

        private Label _lblSzTag;
        private Label _lblOpTag;
        private Label _lblMeth;
        private Label _lblAlpha;
        private Label _lblDil;
        private Label _lblFea;
        private Label _lblVarOff;
        private Label _lblVarSc;
        private Label _lblSearch;
        private Button _btnResetTf;
        private Button _btnSaveTpl;
        private Button _btnTplLoad;
        private Button _btnTplRen;
        private Button _btnTplDup;
        private Button _btnTplDel;
        private Button _btnTplImp;
        private Button _btnTplExp;
        private Button _btnClearLog;

        // UI Controls - Bottom & Status
        private StatusStrip _statusStrip;
        private Label _lblProgStatus;
        private ProgressBar _progressBar;
        private TextBox _txtLog;
        private ToolStripStatusLabel _statusNote;
        private ToolStripStatusLabel _statusCoords;
        private ToolStripStatusLabel _statusSize;

        private Timer _previewDebounceTimer;

        public MainForm(AppConfig config)
        {
            _config = config;
            _ddsCodec = new DdsCodec(config);
            _ytdService = new YtdService(config, _ddsCodec);
            _templateManager = new TemplateManager(config.TemplatesDir);

            AutoScaleMode = AutoScaleMode.None;
            Font = DarkTheme.FontRegular;

            _toolTip = new ToolTip
            {
                AutoPopDelay = 8000,
                InitialDelay = 250,
                ReshowDelay = 100,
                ShowAlways = true
            };

            Text = "YTD Editor - GTA5 / FiveM Kıyafet Doku ve Patch Düzenleyici";
            Size = new Size(1380, 820);
            MinimumSize = new Size(1060, 680);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            BackColor = DarkTheme.BgMain;

            _previewDebounceTimer = new Timer { Interval = 45 };
            _previewDebounceTimer.Tick += (s, e) =>
            {
                _previewDebounceTimer.Stop();
                RecomputeLivePreview();
            };

            BuildMenuBar();
            BuildTopIconToolbar();
            AssembleTopContainer();
            BuildStatusBar();
            BuildMainLayout();

            DarkTheme.ApplyDarkThemeRecursive(this);
            StyleSpecialButtons();

            // Set initial language from config
            Loc.Lang = !string.IsNullOrEmpty(_config.Language) ? _config.Language : "tr";
            ApplyLanguage(Loc.Lang);

            RefreshTemplateList();

            AppendLog(Loc.T("YTD Editor başlatıldı. Sol panelden bir YTD klasörü seçin.", "YTD Editor started. Select a YTD folder from left panel.", "YTD Editor gestartet. Wählen Sie einen YTD-Ordner im linken Bereich."));
            string cwDll = _config.GetCodeWalkerDllPath();
            if (!string.IsNullOrEmpty(cwDll))
                AppendLog("CodeWalker.Core.dll: " + cwDll);
            else
                AppendLog(Loc.T("[BİLGİ] CodeWalker klasörü henüz seçilmedi (Üst Menü -> Ayarlar'dan seçebilirsiniz).", "[INFO] CodeWalker folder not set yet (Top Menu -> Settings).", "[INFO] CodeWalker-Ordner noch nicht festgelegt (Menü -> Einstellungen)."));

            string ddsType = _config.GetResolvedDdsToolType();
            if (ddsType != "none")
                AppendLog($"DDS Encoder ({ddsType}): {_config.DdsToolPath}");
            else
                AppendLog(Loc.T("[BİLGİ] texconv.exe veya CompressonatorCLI.exe henüz seçilmedi (Ayarlar'dan seçebilirsiniz).", "[INFO] texconv.exe or CompressonatorCLI.exe not set yet (Settings).", "[INFO] texconv.exe oder CompressonatorCLI.exe noch nicht festgelegt (Einstellungen)."));
        }

        private void StyleSpecialButtons()
        {
            _btnOpenFolder.BackColor = DarkTheme.BgButtonActive;
            _btnOpenFolder.ForeColor = Color.White;
            _btnOpenFolder.Image = DarkTheme.CreateIcon("folder", Color.White);

            _btnImportTex.Image = DarkTheme.CreateIcon("import");

            _btnBatchGroup.BackColor = DarkTheme.BgButtonActive;
            _btnBatchGroup.ForeColor = Color.White;

            BtnToolSave.BackColor = DarkTheme.BgSuccess;
            BtnToolSave.ForeColor = Color.White;
            BtnToolSave.Image = DarkTheme.CreateIcon("save", Color.White);

            BtnToolSaveNewSlot.BackColor = DarkTheme.BgAccent;
            BtnToolSaveNewSlot.ForeColor = Color.White;
            BtnToolSaveNewSlot.Image = DarkTheme.CreateIcon("new_slot", Color.White);

            SetActiveCanvasTool("rect");
            UpdateToggleButtonVisuals();
        }

        // ------------------------------------------------------------------
        // Menu Bar & Status Bar
        // ------------------------------------------------------------------
        private void BuildMenuBar()
        {
            _mainMenu = new MenuStrip
            {
                BackColor = Color.FromArgb(248, 249, 250),
                ForeColor = DarkTheme.TextPrimary,
                Font = DarkTheme.FontRegular,
                Dock = DockStyle.Top,
                Padding = new Padding(4, 2, 4, 2)
            };

            _mFile = new ToolStripMenuItem("Dosya") { ForeColor = DarkTheme.TextPrimary };
            _miFileOpen = new ToolStripMenuItem("Klasör Seç...", DarkTheme.CreateIcon("folder"), (s, e) => OnSelectFolder());
            _miFileImport = new ToolStripMenuItem("Görsel İçe Aktar (PNG / DDS DXT5)...", DarkTheme.CreateIcon("import"), (s, e) => OnImportTexture());
            _miFileSave = new ToolStripMenuItem("Kaydet (Seçili Varyant / Grup)", DarkTheme.CreateIcon("save"), (s, e) => OnSaveClicked());
            _miFileSaveNew = new ToolStripMenuItem("Yeni Slot Olarak Kaydet...", DarkTheme.CreateIcon("new_slot"), (s, e) => OnSaveNewSlotClicked());
            _miFileExit = new ToolStripMenuItem("Çıkış", null, (s, e) => Close());

            _mFile.DropDownItems.Add(_miFileOpen);
            _mFile.DropDownItems.Add(_miFileImport);
            _mFile.DropDownItems.Add(new ToolStripSeparator());
            _mFile.DropDownItems.Add(_miFileSave);
            _mFile.DropDownItems.Add(_miFileSaveNew);
            _mFile.DropDownItems.Add(new ToolStripSeparator());
            _mFile.DropDownItems.Add(_miFileExit);

            _mEdit = new ToolStripMenuItem("Düzenle") { ForeColor = DarkTheme.TextPrimary };
            _miEditUndo = new ToolStripMenuItem("Geri Al (Ctrl+Z)", DarkTheme.CreateIcon("undo"), (s, e) => OnUndo());
            _miEditRedo = new ToolStripMenuItem("Yinele (Ctrl+Y)", DarkTheme.CreateIcon("redo"), (s, e) => OnRedo());
            _miEditClear = new ToolStripMenuItem("Maskeyi Temizle (Delete)", DarkTheme.CreateIcon("clear"), (s, e) => OnClearMask());

            _mEdit.DropDownItems.Add(_miEditUndo);
            _mEdit.DropDownItems.Add(_miEditRedo);
            _mEdit.DropDownItems.Add(_miEditClear);

            _mBatch = new ToolStripMenuItem("Toplu İşlem") { ForeColor = DarkTheme.TextPrimary };
            _miBatchGroup = new ToolStripMenuItem("Seçili Gruba Toplu Uygula (A, B, C...)", null, (s, e) => OnBatchSelectedGroup());
            _miBatchFolder = new ToolStripMenuItem("Klasördeki Tüm Gruplara Şablon Uygula...", null, (s, e) => OnBatchAllGroups());

            _mBatch.DropDownItems.Add(_miBatchGroup);
            _mBatch.DropDownItems.Add(_miBatchFolder);

            _mSettings = new ToolStripMenuItem("Ayarlar", null, (s, e) => OnOpenSettings())
            {
                ForeColor = DarkTheme.TextPrimary
            };

            _mSelfCheck = new ToolStripMenuItem("Kendi Kendini Kontrol Et", null, (s, e) => OnRunSelfCheck())
            {
                ForeColor = Color.FromArgb(5, 150, 105),
                Font = DarkTheme.FontBold
            };

            _mLangMenu = new ToolStripMenuItem("🌐 Dil / Language / Sprache") { ForeColor = DarkTheme.TextPrimary };
            _mLangTr = new ToolStripMenuItem("Türkçe (TR)", null, (s, e) => ApplyLanguage("tr"));
            _mLangEn = new ToolStripMenuItem("English (EN)", null, (s, e) => ApplyLanguage("en"));
            _mLangDe = new ToolStripMenuItem("Deutsch (DE)", null, (s, e) => ApplyLanguage("de"));

            _mLangMenu.DropDownItems.Add(_mLangTr);
            _mLangMenu.DropDownItems.Add(_mLangEn);
            _mLangMenu.DropDownItems.Add(_mLangDe);

            _mainMenu.Items.AddRange(new ToolStripItem[] { _mFile, _mEdit, _mBatch, _mSettings, _mSelfCheck, _mLangMenu });
            MainMenuStrip = _mainMenu;
        }

        // ------------------------------------------------------------------
        // Top Toolbar directly under MenuStrip (Crisp GDI+ Icons + Tooltips)
        // ------------------------------------------------------------------
        private void BuildTopIconToolbar()
        {
            _toolBarPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(245, 246, 248),
                Padding = new Padding(4, 3, 4, 3)
            };

            _toolBarPanel.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(216, 220, 226), 1f))
                {
                    e.Graphics.DrawLine(pen, 0, _toolBarPanel.Height - 1, _toolBarPanel.Width, _toolBarPanel.Height - 1);
                }
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            // Drawing & Vector Shape tools with GDI+ vector icons
            _btnToolRect = MakeIconBtn("rect", "Kare / Dikdörtgen Seçim Aracı (Kısayol: R)\nSürükleyerek keskin kare/dikdörtgen temizleme alanı belirler.\nShift basılıyken: Tam kare çizer | Sağ tık sürükle: Maskeden siler.", (s, e) => SetActiveCanvasTool("rect"));
            _btnToolCircle = MakeIconBtn("circle", "Daire / Elips Seçim Aracı (Kısayol: C)\nSürükleyerek keskin daire/elips temizleme alanı belirler.\nShift basılıyken: Tam daire çizer | Sağ tık sürükle: Maskeden siler.", (s, e) => SetActiveCanvasTool("circle"));
            _btnToolTriangle = MakeIconBtn("triangle", "Üçgen Seçim Aracı (Kısayol: T)\nSürükleyerek keskin üçgen temizleme alanı belirler.\nSağ tık sürükle: Maskeden siler.", (s, e) => SetActiveCanvasTool("triangle"));
            _btnToolBrush = MakeIconBtn("brush", "Fırça Aracı (Kısayol: B)\nSol tık: Keskin maske boyar | Sağ tık: Maske siler.", (s, e) => SetActiveCanvasTool("brush"));
            _btnToolEraser = MakeIconBtn("eraser", "Silgi Aracı (Kısayol: E)\nMaskelenmiş alanı keskin olarak siler.", (s, e) => SetActiveCanvasTool("eraser"));
            _btnToolMove = MakeIconBtn("move", "Maskeyi Taşı (Kısayol: M)\nMaskeyi sürükleyerek veya ok tuşlarıyla kaydırır.\nShift + Ok: 10px adımla kaydırır.\nAlt basılıyken: Yalnızca aktif sekmedeki varyantı kaydırır.", (s, e) => SetActiveCanvasTool("move"));
            _btnToolClonePick = MakeIconBtn("clone", "Klon Kaynak Noktası Seç\nKlonlama doldurma yöntemi için tuval üzerinden kaynak doku noktası seçer.", (s, e) => SetActiveCanvasTool("clone_pick"));

            flow.Controls.AddRange(new Control[] { _btnToolRect, _btnToolCircle, _btnToolTriangle, _btnToolBrush, _btnToolEraser, _btnToolMove, _btnToolClonePick });
            flow.Controls.Add(MakeSeparator());

            // Brush size slider
            _lblSzTag = new Label
            {
                Text = "Boyut:",
                AutoSize = false,
                Size = new Size(38, 28),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 1, 2, 0)
            };
            _toolTip.SetToolTip(_lblSzTag, "Fırça ve silgi yarıçapı (1 - 150 piksel)");
            flow.Controls.Add(_lblSzTag);

            _sliderBrushSize = new TrackBar
            {
                Minimum = 1,
                Maximum = 150,
                Value = 24,
                TickStyle = TickStyle.None,
                AutoSize = false,
                Size = new Size(64, 24),
                Margin = new Padding(0, 3, 0, 0)
            };
            _toolTip.SetToolTip(_sliderBrushSize, "Fırça Boyutu: 1 - 150 piksel");
            _sliderBrushSize.ValueChanged += (s, e) =>
            {
                if (_canvas != null)
                {
                    _canvas.BrushRadiusPx = _sliderBrushSize.Value;
                    _lblBrushSize.Text = $"{_sliderBrushSize.Value}px";
                    _canvas.Invalidate();
                }
            };
            flow.Controls.Add(_sliderBrushSize);

            _lblBrushSize = new Label
            {
                Text = "24px",
                AutoSize = false,
                Size = new Size(34, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(1, 1, 2, 0)
            };
            flow.Controls.Add(_lblBrushSize);

            flow.Controls.Add(MakeSeparator());

            // Mask Opacity slider
            _lblOpTag = new Label
            {
                Text = "Opak:",
                AutoSize = false,
                Size = new Size(36, 28),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 1, 2, 0)
            };
            _toolTip.SetToolTip(_lblOpTag, "Tuval üzerindeki maske önizleme opaklığı (%0 - %100)");
            flow.Controls.Add(_lblOpTag);

            _sliderMaskOpacity = new TrackBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 48,
                TickStyle = TickStyle.None,
                AutoSize = false,
                Size = new Size(58, 24),
                Margin = new Padding(0, 3, 0, 0)
            };
            _toolTip.SetToolTip(_sliderMaskOpacity, "Maske Opaklığı: %0 - %100");
            _sliderMaskOpacity.ValueChanged += (s, e) =>
            {
                if (_canvas != null)
                {
                    _canvas.MaskOpacity = _sliderMaskOpacity.Value / 100f;
                    _lblMaskOpacity.Text = $"%{_sliderMaskOpacity.Value}";
                    _canvas.RebuildMaskOverlayBitmap();
                    _canvas.Invalidate();
                }
            };
            flow.Controls.Add(_sliderMaskOpacity);

            _lblMaskOpacity = new Label
            {
                Text = "%48",
                AutoSize = false,
                Size = new Size(32, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(1, 1, 2, 0)
            };
            flow.Controls.Add(_lblMaskOpacity);

            _btnMaskColor = MakeIconBtn("color", "Maske Rengi Seç\nTuval üzerindeki yarı saydam maske rengini değiştirir.", (s, e) => OnPickMaskColor());
            _btnMaskColor.BackColor = _config.MaskColorR > 0
                ? Color.FromArgb(_config.MaskColorR, _config.MaskColorG, _config.MaskColorB)
                : Color.FromArgb(255, 50, 90);
            _btnMaskColor.Image = null; // Pure color swatch button
            flow.Controls.Add(_btnMaskColor);

            flow.Controls.Add(MakeSeparator());

            // Split view & Alignment overlay
            _btnToolSplit = MakeIconBtn("split", "Önce / Sonra Karşılaştırma Sürgüsü (Split View)\nOrijinal ile temizlenmiş sonucu karşılaştırmak için ortadaki çizgiyi sürükleyin.", (s, e) =>
            {
                if (_canvas != null)
                {
                    _canvas.SplitViewEnabled = !_canvas.SplitViewEnabled;
                    UpdateToggleButtonVisuals();
                    _canvas.Invalidate();
                }
            });

            _btnToolAlignOverlay = MakeIconBtn("align", "Hizalama Kontrol Overlay\nReferans varyantın patch bölgesini yarı saydam olarak diğer varyantların üstünde gösterir.", (s, e) =>
            {
                if (_canvas != null)
                {
                    _canvas.ShowAlignmentOverlay = !_canvas.ShowAlignmentOverlay;
                    UpdateToggleButtonVisuals();
                    _canvas.Invalidate();
                }
            });

            _chkToolChecker = new CheckBox
            {
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                Text = "",
                Image = DarkTheme.CreateIcon("checker"),
                ImageAlign = ContentAlignment.MiddleCenter,
                Checked = true,
                Size = new Size(28, 28),
                Margin = new Padding(1, 1, 1, 0),
                Cursor = Cursors.Hand
            };
            _toolTip.SetToolTip(_chkToolChecker, "Dama Arka Planı Göster / Gizle\nŞeffaf (transparan) bölgelerin altında dama desenini gösterir.");
            _chkToolChecker.CheckedChanged += (s, e) =>
            {
                if (_canvas != null)
                {
                    _canvas.ShowCheckerboard = _chkToolChecker.Checked;
                    UpdateToggleButtonVisuals();
                    _canvas.Invalidate();
                }
            };

            _chkToolAlpha = new CheckBox
            {
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                Text = "",
                Image = DarkTheme.CreateIcon("alpha"),
                ImageAlign = ContentAlignment.MiddleCenter,
                Checked = true,
                Size = new Size(28, 28),
                Margin = new Padding(1, 1, 1, 0),
                Cursor = Cursors.Hand
            };
            _toolTip.SetToolTip(_chkToolAlpha, "Alfa Kanalını Göster / Gizle\nDokunun şeffaflık (alfa) kanalını önizlemede açar veya kapatır.");
            _chkToolAlpha.CheckedChanged += (s, e) =>
            {
                if (_canvas != null)
                {
                    _canvas.ShowAlpha = _chkToolAlpha.Checked;
                    UpdateToggleButtonVisuals();
                    _canvas.RebuildImageBitmaps();
                    _canvas.Invalidate();
                }
            };

            flow.Controls.AddRange(new Control[] { _btnToolSplit, _btnToolAlignOverlay, _chkToolChecker, _chkToolAlpha });
            flow.Controls.Add(MakeSeparator());

            // Zoom controls
            _btnToolZoomOut = MakeIconBtn("zoom_out", "Uzaklaş (Zoom Out - Fare Tekerleği)", (s, e) => _canvas?.ZoomBy(0.8f));
            _btnToolZoomIn = MakeIconBtn("zoom_in", "Yakınlaş (Zoom In - Fare Tekerleği)", (s, e) => _canvas?.ZoomBy(1.25f));
            _btnToolZoomFit = MakeIconBtn("zoom_fit", "Ekrana Sığdır (Fit)", (s, e) => _canvas?.ZoomToFit());
            _btnToolZoom1to1 = MakeIconBtn("zoom_1to1", "Gerçek Boyut (1:1 Piksel)", (s, e) => _canvas?.ZoomOneToOne());

            flow.Controls.AddRange(new Control[] { _btnToolZoomOut, _btnToolZoomIn, _btnToolZoomFit, _btnToolZoom1to1 });
            flow.Controls.Add(MakeSeparator());

            // Undo / Redo / Clear
            _btnToolUndo = MakeIconBtn("undo", "Geri Al (Ctrl+Z)", (s, e) => OnUndo());
            _btnToolRedo = MakeIconBtn("redo", "Yinele (Ctrl+Y)", (s, e) => OnRedo());
            _btnToolClear = MakeIconBtn("clear", "Maskeyi Temizle (Delete)\nAktif grubun maskesini sıfırlar.", (s, e) => OnClearMask());

            flow.Controls.AddRange(new Control[] { _btnToolUndo, _btnToolRedo, _btnToolClear });
            flow.Controls.Add(MakeSeparator());

            // Save actions
            BtnToolSave = new Button
            {
                Text = "Kaydet",
                Image = DarkTheme.CreateIcon("save", Color.White),
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(80, 28),
                Margin = new Padding(2, 1, 2, 0),
                Cursor = Cursors.Hand
            };
            DarkTheme.StyleButton(BtnToolSave, DarkTheme.BgSuccess);
            _toolTip.SetToolTip(BtnToolSave, "Kaydet (Ctrl+S)\nSeçili varyantı veya gruptaki tüm varyantları DDS DXT5 (BC3_UNORM) olarak kaydeder.");
            BtnToolSave.Click += (s, e) => OnSaveClicked();

            BtnToolSaveNewSlot = new Button
            {
                Text = "Yeni Slot",
                Image = DarkTheme.CreateIcon("new_slot", Color.White),
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(92, 28),
                Margin = new Padding(2, 1, 2, 0),
                Cursor = Cursors.Hand
            };
            DarkTheme.StyleButton(BtnToolSaveNewSlot, DarkTheme.BgAccent);
            _toolTip.SetToolTip(BtnToolSaveNewSlot, "Yeni Slot Olarak Kaydet\nKaynak varyantı kopyalayıp sıradaki boş harfle (örn. d) yeni slot oluşturur.");
            BtnToolSaveNewSlot.Click += (s, e) => OnSaveNewSlotClicked();

            flow.Controls.Add(BtnToolSave);
            flow.Controls.Add(BtnToolSaveNewSlot);

            _toolBarPanel.Controls.Add(flow);
        }

        private void AssembleTopContainer()
        {
            _topContainer = new Panel
            {
                Dock = DockStyle.Top,
                Height = 61
            };
            _toolBarPanel.Dock = DockStyle.Top;
            _mainMenu.Dock = DockStyle.Top;

            _topContainer.Controls.Add(_toolBarPanel);
            _topContainer.Controls.Add(_mainMenu);
            _mainMenu.BringToFront();
            _toolBarPanel.SendToBack();
        }

        private Button MakeIconBtn(string iconName, string tooltip, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = "",
                Tag = iconName,
                Image = DarkTheme.CreateIcon(iconName),
                ImageAlign = ContentAlignment.MiddleCenter,
                Size = new Size(28, 28),
                Margin = new Padding(1, 1, 1, 0),
                Cursor = Cursors.Hand
            };
            DarkTheme.StyleButton(btn);
            _toolTip.SetToolTip(btn, tooltip);
            btn.Click += onClick;
            return btn;
        }

        private Control MakeSeparator()
        {
            var pnl = new Panel
            {
                Size = new Size(7, 24),
                Margin = new Padding(2, 3, 2, 0)
            };
            pnl.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(209, 213, 219), 1f))
                {
                    e.Graphics.DrawLine(pen, 3, 2, 3, pnl.Height - 3);
                }
            };
            return pnl;
        }

        private void SetActiveCanvasTool(string toolName)
        {
            if (_canvas != null)
                _canvas.SetActiveTool(toolName);

            Button[] btns = { _btnToolRect, _btnToolCircle, _btnToolTriangle, _btnToolBrush, _btnToolEraser, _btnToolMove, _btnToolClonePick };
            string[] names = { "rect", "circle", "triangle", "brush", "eraser", "move", "clone_pick" };
            string[] icons = { "rect", "circle", "triangle", "brush", "eraser", "move", "clone" };

            for (int i = 0; i < btns.Length; i++)
            {
                if (btns[i] == null) continue;
                bool active = (names[i] == toolName);
                btns[i].BackColor = active ? DarkTheme.BgButtonActive : DarkTheme.BgButton;
                btns[i].ForeColor = active ? Color.White : DarkTheme.TextPrimary;
                btns[i].Image = DarkTheme.CreateIcon(icons[i], active ? Color.White : (Color?)null);
            }
        }

        private void UpdateToggleButtonVisuals()
        {
            if (_btnToolSplit != null && _canvas != null)
            {
                bool on = _canvas.SplitViewEnabled;
                _btnToolSplit.BackColor = on ? DarkTheme.BgButtonActive : DarkTheme.BgButton;
                _btnToolSplit.Image = DarkTheme.CreateIcon("split", on ? Color.White : (Color?)null);
            }
            if (_btnToolAlignOverlay != null && _canvas != null)
            {
                bool on = _canvas.ShowAlignmentOverlay;
                _btnToolAlignOverlay.BackColor = on ? DarkTheme.BgButtonActive : DarkTheme.BgButton;
                _btnToolAlignOverlay.Image = DarkTheme.CreateIcon("align", on ? Color.White : (Color?)null);
            }
            if (_chkToolChecker != null)
            {
                bool on = _chkToolChecker.Checked;
                _chkToolChecker.BackColor = on ? Color.FromArgb(219, 234, 254) : DarkTheme.BgButton;
                _chkToolChecker.FlatAppearance.BorderColor = on ? DarkTheme.BgButtonActive : Color.FromArgb(209, 213, 219);
            }
            if (_chkToolAlpha != null)
            {
                bool on = _chkToolAlpha.Checked;
                _chkToolAlpha.BackColor = on ? Color.FromArgb(219, 234, 254) : DarkTheme.BgButton;
                _chkToolAlpha.FlatAppearance.BorderColor = on ? DarkTheme.BgButtonActive : Color.FromArgb(209, 213, 219);
            }
        }

        private void OnPickMaskColor()
        {
            using (var cd = new ColorDialog { Color = _canvas?.MaskColor ?? Color.FromArgb(255, 50, 90), FullOpen = true })
            {
                if (cd.ShowDialog(this) == DialogResult.OK && _canvas != null)
                {
                    _canvas.MaskColor = cd.Color;
                    _btnMaskColor.BackColor = cd.Color;
                    _canvas.RebuildMaskOverlayBitmap();
                    _canvas.Invalidate();
                }
            }
        }

        private void BuildStatusBar()
        {
            _statusStrip = new StatusStrip
            {
                BackColor = Color.FromArgb(243, 244, 246),
                ForeColor = DarkTheme.TextMuted,
                Font = DarkTheme.FontRegular,
                Dock = DockStyle.Bottom,
                SizingGrip = true
            };

            _statusNote = new ToolStripStatusLabel("Hazır")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(37, 99, 235)
            };
            _statusCoords = new ToolStripStatusLabel("Piksel: (-, -) | Normalize: (0.000, 0.000)")
            {
                ForeColor = DarkTheme.TextPrimary
            };
            _statusSize = new ToolStripStatusLabel("Boyut: - x - | Format: -")
            {
                ForeColor = DarkTheme.TextPrimary
            };

            _statusStrip.Items.AddRange(new ToolStripItem[]
            {
                _statusNote,
                _statusCoords,
                new ToolStripStatusLabel(" | "),
                _statusSize
            });
        }

        // ------------------------------------------------------------------
        // Main UI Layout (Responsive Windows Forms Panels & TableLayoutPanels)
        // ------------------------------------------------------------------
        private void BuildMainLayout()
        {
            var workAreaPanel = new Panel { Dock = DockStyle.Fill };

            var leftPanel = new Panel { Dock = DockStyle.Left, Width = 265, Padding = new Padding(4) };
            var leftSplitter = new Splitter { Dock = DockStyle.Left, Width = 5, MinSize = 200, MinExtra = 400 };

            var rightPanel = new Panel { Dock = DockStyle.Right, Width = 360, Padding = new Padding(4) };
            var rightSplitter = new Splitter { Dock = DockStyle.Right, Width = 5, MinSize = 290, MinExtra = 400 };

            var centerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2) };

            // ================= LEFT PANEL =================

            _btnOpenFolder = new Button
            {
                Text = " Klasör Seç (YTD Dosyalarını Tara)",
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Top,
                Height = 32
            };
            _btnOpenFolder.Click += (s, e) => OnSelectFolder();

            _grpTexs = new GroupBox
            {
                Text = "YTD İçindeki Dokular (Textures)",
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };

            _btnImportTex = new Button
            {
                Text = " Görsel İçe Aktar (PNG / DDS DXT5)",
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Bottom,
                Height = 30
            };
            _btnImportTex.Click += (s, e) => OnImportTexture();

            _chkApplyNormSpec = new CheckBox
            {
                Text = "Maskeyi _n ve _s dokulara da uygula",
                Checked = _config.ApplyToNormalSpecDefault,
                Dock = DockStyle.Bottom,
                Height = 28
            };
            _chkApplyNormSpec.CheckedChanged += (s, e) =>
            {
                _config.ApplyToNormalSpecDefault = _chkApplyNormSpec.Checked;
                _config.Save();
                SchedulePreviewUpdate();
            };

            _listTextures = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _listTextures.SelectedIndexChanged += (s, e) =>
            {
                if (_listTextures.SelectedIndex >= 0)
                {
                    _activeTextureIndex = _listTextures.SelectedIndex;
                    DisplayActiveTexture(false);
                }
            };

            _grpTexs.Controls.Add(_listTextures);
            _grpTexs.Controls.Add(_chkApplyNormSpec);
            _grpTexs.Controls.Add(_btnImportTex);
            _listTextures.BringToFront();

            _grpSlots = new GroupBox
            {
                Text = "Slot Aileleri ve Varyantlar (000_a/b/c...)",
                Dock = DockStyle.Fill,
                Padding = new Padding(6)
            };
            _treeGroups = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                FullRowSelect = true,
                ItemHeight = 20
            };
            _treeGroups.AfterSelect += OnTreeAfterSelect;
            _grpSlots.Controls.Add(_treeGroups);

            var leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 5
            };
            leftSplit.Panel1.Controls.Add(_grpSlots);
            leftSplit.Panel2.Controls.Add(_grpTexs);

            leftSplit.Resize += (s, e) =>
            {
                if (leftSplit.Height > 180)
                {
                    try
                    {
                        leftSplit.SplitterDistance = Math.Max(90, Math.Min(leftSplit.Height - 90, (int)(leftSplit.Height * 0.54)));
                    }
                    catch { }
                }
            };

            leftPanel.Controls.Add(leftSplit);
            leftPanel.Controls.Add(_btnOpenFolder);
            leftSplit.BringToFront();

            // ================= CENTER PANEL =================
            var topBarPanel = new Panel { Dock = DockStyle.Top, Height = 28 };
            _lblGroupBadge = new Label
            {
                Text = "Seçili Grup: -",
                Dock = DockStyle.Right,
                AutoSize = false,
                Width = 210,
                TextAlign = ContentAlignment.MiddleRight,
                Font = DarkTheme.FontBold,
                ForeColor = Color.FromArgb(37, 99, 235)
            };

            _tabVariants = new TabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.Normal
            };
            _tabVariants.SelectedIndexChanged += OnVariantTabChanged;
            topBarPanel.Controls.Add(_tabVariants);
            topBarPanel.Controls.Add(_lblGroupBadge);

            _lblAlignmentBanner = new Label
            {
                Text = "",
                Dock = DockStyle.Top,
                Height = 26,
                Visible = false,
                BackColor = Color.FromArgb(254, 243, 199),
                ForeColor = Color.FromArgb(146, 64, 14),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0)
            };

            _canvas = new CanvasControl { Dock = DockStyle.Fill };
            _canvas.CursorCoordsChanged += OnCanvasCursorCoordsChanged;
            _canvas.MaskModified += OnCanvasMaskModified;
            _canvas.CloneSourcePicked += OnCanvasCloneSourcePicked;
            _canvas.MoveStatusNote += (s, note) => _statusNote.Text = note;
            _canvas.UndoRequested += (s, e) => OnUndo();
            _canvas.RedoRequested += (s, e) => OnRedo();
            _canvas.ClearMaskRequested += (s, e) => OnClearMask();
            _canvas.ToolChangedByShortcut += (s, tool) => SetActiveCanvasTool(tool);

            centerPanel.Controls.Add(_canvas);
            centerPanel.Controls.Add(_lblAlignmentBanner);
            centerPanel.Controls.Add(topBarPanel);
            _canvas.BringToFront();

            // ================= RIGHT PANEL (Proportional, Zero-Overlap Layout) =================

            // 1. Fill Method & Alpha GroupBox
            _grpFill = new GroupBox
            {
                Text = "Doldurma Yöntemi ve Alfa Seçeneği",
                Dock = DockStyle.Top,
                Height = 114,
                Padding = new Padding(6, 4, 6, 4)
            };

            var tblFill = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0)
            };
            tblFill.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68f));
            tblFill.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tblFill.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tblFill.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tblFill.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

            _lblMeth = new Label { Text = "Yöntem:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _cmbFillMethod = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 2)
            };
            _cmbFillMethod.Items.AddRange(new object[]
            {
                "Inpaint - Telea (Varsayılan)",
                "Inpaint - Navier-Stokes",
                "Klonlama (Kaynak Noktası Seç)",
                "Düz Renk (Çevre Ortalaması)",
                "Donor Kopyala (Temiz Varyant)",
                "Şeffaflaştır / Sil (Alfa = 0, Transparan)"
            });
            _cmbFillMethod.SelectedIndex = 0;
            _cmbFillMethod.SelectedIndexChanged += OnFillMethodChanged;

            _lblAlpha = new Label { Text = "Alfa Modu:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _cmbAlphaMode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 2)
            };
            _cmbAlphaMode.Items.AddRange(new object[]
            {
                "Alfa Korunsun (Orijinal)",
                "Opak Yap (Alfa = 255)",
                "Tamamen Şeffaf Yap (Alfa = 0)"
            });
            _cmbAlphaMode.SelectedIndex = 0;
            _cmbAlphaMode.SelectedIndexChanged += (s, e) => SchedulePreviewUpdate();

            var pnlExtraFill = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            _btnPickCloneSrc = new Button
            {
                Text = "Tuvalden Klon Kaynak Noktası Seç",
                Dock = DockStyle.Fill,
                Visible = false
            };
            _btnPickCloneSrc.Click += (s, e) => SetActiveCanvasTool("clone_pick");

            _pnlDonorRow = new Panel { Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0) };
            _lblDonor = new Label { Text = "Donor:", Dock = DockStyle.Left, Width = 52, TextAlign = ContentAlignment.MiddleLeft };
            _cmbDonorVariant = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cmbDonorVariant.SelectedIndexChanged += (s, e) => SchedulePreviewUpdate();
            _pnlDonorRow.Controls.Add(_cmbDonorVariant);
            _pnlDonorRow.Controls.Add(_lblDonor);
            _cmbDonorVariant.BringToFront();

            pnlExtraFill.Controls.Add(_btnPickCloneSrc);
            pnlExtraFill.Controls.Add(_pnlDonorRow);

            tblFill.Controls.Add(_lblMeth, 0, 0);
            tblFill.Controls.Add(_cmbFillMethod, 1, 0);
            tblFill.Controls.Add(_lblAlpha, 0, 1);
            tblFill.Controls.Add(_cmbAlphaMode, 1, 1);
            tblFill.Controls.Add(pnlExtraFill, 0, 2);
            tblFill.SetColumnSpan(pnlExtraFill, 2);

            _grpFill.Controls.Add(tblFill);

            // 2. Mask Edge & Per-Variant Alignment Fine-Tuning GroupBox
            _grpEdge = new GroupBox
            {
                Text = "Maske Kenarı ve Varyant İnce Ayarı",
                Dock = DockStyle.Top,
                Height = 110,
                Padding = new Padding(6, 4, 6, 4)
            };

            var tblEdge = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 3,
                Margin = new Padding(0)
            };
            tblEdge.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88f));
            tblEdge.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));
            tblEdge.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));
            tblEdge.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            tblEdge.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));
            tblEdge.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));
            tblEdge.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));

            _lblDil = new Label { Text = "Dilate (0-8px):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _numDilate = new NumericUpDown { Minimum = 0, Maximum = 8, Value = 0, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 4, 2) };
            _numDilate.ValueChanged += (s, e) => OnEdgeParamsChanged();

            _lblFea = new Label { Text = "Feather (0-8):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
            _numFeather = new NumericUpDown { Minimum = 0, Maximum = 8, Value = 0, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 0, 2) };
            _numFeather.ValueChanged += (s, e) => OnEdgeParamsChanged();

            _lblVarOff = new Label { Text = "Kaydır (X / Y):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _numVarDx = new NumericUpDown { Minimum = -0.5m, Maximum = 0.5m, DecimalPlaces = 4, Increment = 0.002m, Value = 0m, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 4, 2) };
            _numVarDy = new NumericUpDown { Minimum = -0.5m, Maximum = 0.5m, DecimalPlaces = 4, Increment = 0.002m, Value = 0m, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 4, 2) };
            _numVarDx.ValueChanged += (s, e) => OnVariantFineTuneChanged();
            _numVarDy.ValueChanged += (s, e) => OnVariantFineTuneChanged();

            _lblVarSc = new Label { Text = "Ölçek (X / Y):", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _numVarSx = new NumericUpDown { Minimum = 0.25m, Maximum = 4.0m, DecimalPlaces = 2, Increment = 0.01m, Value = 1.0m, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 4, 2) };
            _numVarSy = new NumericUpDown { Minimum = 0.25m, Maximum = 4.0m, DecimalPlaces = 2, Increment = 0.01m, Value = 1.0m, Dock = DockStyle.Fill, Margin = new Padding(2, 3, 4, 2) };
            _numVarSx.ValueChanged += (s, e) => OnVariantFineTuneChanged();
            _numVarSy.ValueChanged += (s, e) => OnVariantFineTuneChanged();

            _btnResetTf = new Button { Text = "Sıfırla", Dock = DockStyle.Fill, Margin = new Padding(2, 2, 0, 2) };
            _btnResetTf.Click += (s, e) => OnResetVariantFineTune();

            tblEdge.Controls.Add(_lblDil, 0, 0);
            tblEdge.Controls.Add(_numDilate, 1, 0);
            tblEdge.Controls.Add(_lblFea, 2, 0);
            tblEdge.Controls.Add(_numFeather, 3, 0);

            tblEdge.Controls.Add(_lblVarOff, 0, 1);
            tblEdge.Controls.Add(_numVarDx, 1, 1);
            tblEdge.Controls.Add(_numVarDy, 2, 1);

            tblEdge.Controls.Add(_lblVarSc, 0, 2);
            tblEdge.Controls.Add(_numVarSx, 1, 2);
            tblEdge.Controls.Add(_numVarSy, 2, 2);
            tblEdge.Controls.Add(_btnResetTf, 3, 2);

            _grpEdge.Controls.Add(tblEdge);

            // 3. Batch Mode Buttons GroupBox
            _grpBatch = new GroupBox
            {
                Text = "Toplu İşlem (Seçili Grup ve Klasör Modu)",
                Dock = DockStyle.Top,
                Height = 84,
                Padding = new Padding(6, 4, 6, 4)
            };

            var tblBatch = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            tblBatch.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tblBatch.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            tblBatch.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _btnBatchGroup = new Button
            {
                Text = "Seçili Gruba Uygula (A, B, C Tüm Varyantlar)",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 0, 2)
            };
            _btnBatchGroup.Click += (s, e) => OnBatchSelectedGroup();

            _btnBatchFolder = new Button
            {
                Text = "Klasördeki Tüm Gruplara Şablon Uygula...",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 1)
            };
            _btnBatchFolder.Click += (s, e) => OnBatchAllGroups();

            tblBatch.Controls.Add(_btnBatchGroup, 0, 0);
            tblBatch.Controls.Add(_btnBatchFolder, 0, 1);
            _grpBatch.Controls.Add(tblBatch);

            // 4. Template Manager Panel (DockStyle.Fill so it scales proportionally with screen height)
            _grpTemplates = new GroupBox
            {
                Text = "Maske Şablon Yöneticisi (templates/*.json)",
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 4, 6, 6)
            };

            var tplTopTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 56,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0)
            };
            tplTopTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tplTopTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102f));
            tplTopTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            tplTopTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

            _txtNewTemplateName = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 4, 2) };
            _toolTip.SetToolTip(_txtNewTemplateName, "Yeni şablon adı (örn. polis sol kol patch, göğüs rozeti)");

            _btnSaveTpl = new Button { Text = "Şablon Kaydet", Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 2) };
            _btnSaveTpl.Click += (s, e) => OnSaveTemplateClicked();

            var pnlSearchRow = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
            _lblSearch = new Label { Text = "Ara:", Dock = DockStyle.Left, Width = 36, TextAlign = ContentAlignment.MiddleLeft };
            _txtSearchTemplates = new TextBox { Dock = DockStyle.Fill };
            _txtSearchTemplates.TextChanged += (s, e) => RefreshTemplateList();
            pnlSearchRow.Controls.Add(_txtSearchTemplates);
            pnlSearchRow.Controls.Add(_lblSearch);
            _txtSearchTemplates.BringToFront();

            tplTopTable.Controls.Add(_txtNewTemplateName, 0, 0);
            tplTopTable.Controls.Add(_btnSaveTpl, 1, 0);
            tplTopTable.Controls.Add(pnlSearchRow, 0, 1);
            tplTopTable.SetColumnSpan(pnlSearchRow, 2);

            var tplBottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 116, Padding = new Padding(0, 4, 0, 0) };

            var pnlThumbRow = new Panel { Dock = DockStyle.Top, Height = 54 };
            _picTemplateThumb = new PictureBox
            {
                Dock = DockStyle.Left,
                Width = 54,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            _lblTemplateMeta = new Label
            {
                Text = "Listeden bir şablona tıklayarak\naktif gruba tek tıkla yükleyin.",
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 2, 0, 0),
                ForeColor = DarkTheme.TextMuted
            };
            pnlThumbRow.Controls.Add(_lblTemplateMeta);
            pnlThumbRow.Controls.Add(_picTemplateThumb);
            _lblTemplateMeta.BringToFront();

            var tblTplBtns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                Margin = new Padding(0, 4, 0, 0)
            };
            tblTplBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tblTplBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tblTplBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tblTplBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            tblTplBtns.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            tblTplBtns.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _btnTplLoad = new Button { Text = "Yükle", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 2, 1) };
            _btnTplRen = new Button { Text = "Adlandır", Dock = DockStyle.Fill, Margin = new Padding(2, 2, 2, 1) };
            _btnTplDup = new Button { Text = "Çoğalt", Dock = DockStyle.Fill, Margin = new Padding(2, 2, 2, 1) };
            _btnTplDel = new Button { Text = "Sil", Dock = DockStyle.Fill, Margin = new Padding(2, 2, 0, 1) };

            _btnTplImp = new Button { Text = "İçe Aktar (.json)", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 2, 0) };
            _btnTplExp = new Button { Text = "Dışa Aktar (.json)", Dock = DockStyle.Fill, Margin = new Padding(2, 2, 0, 0) };

            _btnTplLoad.Click += (s, e) => OnLoadSelectedTemplate();
            _btnTplRen.Click += (s, e) => OnRenameTemplate();
            _btnTplDup.Click += (s, e) => OnDuplicateTemplate();
            _btnTplDel.Click += (s, e) => OnDeleteTemplate();
            _btnTplImp.Click += (s, e) => OnImportTemplate();
            _btnTplExp.Click += (s, e) => OnExportTemplate();

            tblTplBtns.Controls.Add(_btnTplLoad, 0, 0);
            tblTplBtns.Controls.Add(_btnTplRen, 1, 0);
            tblTplBtns.Controls.Add(_btnTplDup, 2, 0);
            tblTplBtns.Controls.Add(_btnTplDel, 3, 0);

            tblTplBtns.Controls.Add(_btnTplImp, 0, 1);
            tblTplBtns.SetColumnSpan(_btnTplImp, 2);
            tblTplBtns.Controls.Add(_btnTplExp, 2, 1);
            tblTplBtns.SetColumnSpan(_btnTplExp, 2);

            tplBottomPanel.Controls.Add(tblTplBtns);
            tplBottomPanel.Controls.Add(pnlThumbRow);
            tblTplBtns.BringToFront();

            _listTemplates = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _listTemplates.SelectedIndexChanged += (s, e) => OnTemplateListSelected();

            _grpTemplates.Controls.Add(_listTemplates);
            _grpTemplates.Controls.Add(tplBottomPanel);
            _grpTemplates.Controls.Add(tplTopTable);
            _listTemplates.BringToFront();

            // Stack all 4 groups directly inside rightPanel so they are all visible at once
            // and _grpTemplates grows/shrinks proportionally with window height
            rightPanel.Controls.Add(_grpTemplates); // DockStyle.Fill
            rightPanel.Controls.Add(_grpBatch);     // DockStyle.Top
            rightPanel.Controls.Add(_grpEdge);      // DockStyle.Top
            rightPanel.Controls.Add(_grpFill);      // DockStyle.Top
            _grpFill.SendToBack();
            _grpEdge.BringToFront();
            _grpBatch.BringToFront();
            _grpTemplates.BringToFront();

            // Assemble panels into workAreaPanel
            workAreaPanel.Controls.Add(centerPanel);   // DockStyle.Fill
            workAreaPanel.Controls.Add(rightSplitter); // DockStyle.Right
            workAreaPanel.Controls.Add(rightPanel);    // DockStyle.Right
            workAreaPanel.Controls.Add(leftSplitter);  // DockStyle.Left
            workAreaPanel.Controls.Add(leftPanel);     // DockStyle.Left

            leftPanel.BringToFront();
            leftSplitter.BringToFront();
            rightPanel.BringToFront();
            rightSplitter.BringToFront();
            centerPanel.SendToBack();

            // ================= BOTTOM LOG & PROGRESS PANEL =================
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 125, Padding = new Padding(4, 2, 4, 2) };
            var bottomSplitter = new Splitter { Dock = DockStyle.Bottom, Height = 5, MinSize = 65, MinExtra = 250 };

            var progPanel = new Panel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(0, 0, 0, 2) };

            _lblProgStatus = new Label
            {
                Text = "İşlem Durumu: Beklemede",
                Dock = DockStyle.Left,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 4, 8, 0)
            };

            _btnClearLog = new Button
            {
                Text = "Logu Temizle",
                Dock = DockStyle.Right,
                Width = 96
            };
            DarkTheme.StyleButton(_btnClearLog);
            _btnClearLog.Click += (s, e) => _txtLog.Clear();

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill
            };

            progPanel.Controls.Add(_progressBar);
            progPanel.Controls.Add(_lblProgStatus);
            progPanel.Controls.Add(_btnClearLog);
            _progressBar.BringToFront();

            _txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = DarkTheme.FontMono,
                BackColor = Color.White,
                ForeColor = DarkTheme.TextPrimary
            };

            bottomPanel.Controls.Add(_txtLog);
            bottomPanel.Controls.Add(progPanel);
            _txtLog.BringToFront();

            // Assemble into Form Controls in strict docking z-order
            Controls.Clear();
            Controls.Add(workAreaPanel);  // DockStyle.Fill
            Controls.Add(bottomSplitter); // DockStyle.Bottom
            Controls.Add(bottomPanel);    // DockStyle.Bottom
            Controls.Add(_statusStrip);   // DockStyle.Bottom
            Controls.Add(_topContainer);  // DockStyle.Top

            _topContainer.BringToFront();
            _statusStrip.BringToFront();
            bottomPanel.BringToFront();
            bottomSplitter.BringToFront();
            workAreaPanel.SendToBack();

            Resize += (s, e) =>
            {
                if (WindowState != FormWindowState.Minimized && ClientSize.Height > 450)
                {
                    int targetBottomH = Math.Max(95, Math.Min(165, (int)(ClientSize.Height * 0.15)));
                    if (Math.Abs(bottomPanel.Height - targetBottomH) > 4)
                        bottomPanel.Height = targetBottomH;
                }
            };
        }

        // ------------------------------------------------------------------
        // Logging Helper
        // ------------------------------------------------------------------
        public void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.AppendText($"[{ts}] {message}{Environment.NewLine}");
        }

        // ------------------------------------------------------------------
        // Folder Selection & Slot Grouping
        // ------------------------------------------------------------------
        private void OnSelectFolder()
        {
            using (var fbd = new FolderBrowserDialog
            {
                Description = "GTA5 / FiveM Kıyafet YTD Klasörünü Seçin",
                SelectedPath = _config.LastOpenedFolder
            })
            {
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    LoadFolder(fbd.SelectedPath);
                }
            }
        }

        private void LoadFolder(string folderPath)
        {
            _currentFolder = folderPath;
            _config.LastOpenedFolder = folderPath;
            _config.Save();

            AppendLog("Klasör taranıyor: " + folderPath);
            _slotGroups = SlotGrouper.ScanFolderForSlotGroups(folderPath, true);
            _treeGroups.Nodes.Clear();
            _groupYtdCache.Clear();
            _importedOverrides.Clear();

            int totalFiles = _slotGroups.Sum(g => g.Variants.Count);
            AppendLog($"Tarama tamamlandı: {_slotGroups.Count} slot ailesi grubu, toplam {totalFiles} adet YTD dosyası bulundu.");

            for (int i = 0; i < _slotGroups.Count; i++)
            {
                var g = _slotGroups[i];
                string vars = string.Join(", ", g.VariantLetters.Select(v => v.ToUpperInvariant()));
                var rootNode = new TreeNode($"{g.GroupKey} [{g.Variants.Count}: {vars}]")
                {
                    Tag = new Tuple<string, int, string>("group", i, "")
                };

                foreach (var v in g.SortedVariants)
                {
                    var childNode = new TreeNode($"{v.VariantLetter.ToUpperInvariant()} - {v.FileName}")
                    {
                        Tag = new Tuple<string, int, string>("variant", i, v.VariantLetter)
                    };
                    rootNode.Nodes.Add(childNode);
                }
                _treeGroups.Nodes.Add(rootNode);
            }

            if (_treeGroups.Nodes.Count > 0)
            {
                _treeGroups.Nodes[0].Expand();
                _treeGroups.SelectedNode = _treeGroups.Nodes[0];
            }
        }

        private GroupMaskState GetOrCreateGroupMask(string groupKey)
        {
            if (!_groupMasks.TryGetValue(groupKey, out GroupMaskState ms))
            {
                ms = new GroupMaskState(
                    groupKey,
                    _config.NormGridResolution,
                    (int)_numDilate.Value,
                    (int)_numFeather.Value);
                _groupMasks[groupKey] = ms;
            }
            return ms;
        }

        private void OnTreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is Tuple<string, int, string> tag)
            {
                int grpIdx = tag.Item2;
                if (grpIdx >= 0 && grpIdx < _slotGroups.Count)
                {
                    var grp = _slotGroups[grpIdx];
                    string targetVar = (tag.Item1 == "variant" && !string.IsNullOrEmpty(tag.Item3))
                        ? tag.Item3
                        : grp.VariantLetters[0];
                    ActivateSlotGroup(grp, targetVar);
                }
            }
        }

        private void ActivateSlotGroup(SlotGroup group, string initialVariantLetter)
        {
            bool groupChanged = (_activeGroup == null) ||
                                !string.Equals(_activeGroup.GroupKey, group.GroupKey, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(_activeGroup.Folder, group.Folder, StringComparison.OrdinalIgnoreCase);

            if (groupChanged)
            {
                _activeGroup = group;
                _groupYtdCache.Clear();
                _importedOverrides.Clear();
                _referencePatchRgba = null;

                _tabVariants.SelectedIndexChanged -= OnVariantTabChanged;
                _tabVariants.TabPages.Clear();
                _cmbDonorVariant.Items.Clear();

                foreach (var v in group.SortedVariants)
                {
                    var page = new TabPage($"Varyant {v.VariantLetter.ToUpperInvariant()} ({v.Stem})")
                    {
                        Tag = v.VariantLetter
                    };
                    _tabVariants.TabPages.Add(page);
                    _cmbDonorVariant.Items.Add(v.VariantLetter.ToUpperInvariant() + " - " + v.FileName);
                }
                if (_cmbDonorVariant.Items.Count > 0)
                    _cmbDonorVariant.SelectedIndex = 0;

                _tabVariants.SelectedIndexChanged += OnVariantTabChanged;

                var ms = GetOrCreateGroupMask(group.GroupKey);
                _numDilate.Value = ms.DilatePx;
                _numFeather.Value = ms.FeatherPx;
                _lblGroupBadge.Text = $"Grup: {group.GroupKey} ({group.Variants.Count} varyant)";
            }

            for (int i = 0; i < _tabVariants.TabPages.Count; i++)
            {
                if (string.Equals(Convert.ToString(_tabVariants.TabPages[i].Tag), initialVariantLetter, StringComparison.OrdinalIgnoreCase))
                {
                    if (_tabVariants.SelectedIndex != i)
                        _tabVariants.SelectedIndex = i;
                    else
                        LoadActiveVariantTab(initialVariantLetter, groupChanged);
                    break;
                }
            }

            if (groupChanged)
                RunGroupAlignmentCheck();
        }

        private void OnVariantTabChanged(object sender, EventArgs e)
        {
            if (_tabVariants.SelectedTab == null || _activeGroup == null)
                return;
            string varLetter = Convert.ToString(_tabVariants.SelectedTab.Tag);
            LoadActiveVariantTab(varLetter, false);
        }

        private List<YtdTextureEntry> GetCachedOrLoadYtd(VariantEntry varEntry)
        {
            string k = varEntry.VariantLetter.ToLowerInvariant();
            if (!_groupYtdCache.TryGetValue(k, out List<YtdTextureEntry> entries))
            {
                try
                {
                    entries = _ytdService.LoadYtd(varEntry.FilePath, true);
                    _groupYtdCache[k] = entries;
                }
                catch (Exception ex)
                {
                    AppendLog($"[HATA] {varEntry.FileName} okunamadı: {ex.Message}");
                    MessageBox.Show(this, $"{varEntry.FileName} okunamadı:\n\n{ex.Message}", "YTD Okuma Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return new List<YtdTextureEntry>();
                }
            }
            return entries;
        }

        private void LoadActiveVariantTab(string varLetter, bool resetView)
        {
            if (_activeGroup == null || !_activeGroup.Variants.TryGetValue(varLetter, out VariantEntry varEntry))
                return;

            _activeVariant = varEntry;
            var entries = GetCachedOrLoadYtd(varEntry);

            _listTextures.Items.Clear();
            foreach (var tex in entries)
            {
                SlotGrouper.IsNormalOrSpecTexture(tex.Name, varEntry.Stem, out string kind);
                string badge = kind == "normal" ? " [Normal _n]" : (kind == "spec" ? " [Specular _s]" : " [Diffuse]");
                _listTextures.Items.Add($"{tex.Name}{badge} ({tex.Width}x{tex.Height}, {tex.FormatStr}, Mip:{tex.MipLevels})");
            }

            if (_activeTextureIndex >= entries.Count)
                _activeTextureIndex = 0;
            if (entries.Count > 0)
                _listTextures.SelectedIndex = _activeTextureIndex;

            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            var vt = ms.GetVariantTransform(varLetter);
            _suppressFineTuneEvents = true;
            _numVarDx.Value = (decimal)Math.Max(-0.5f, Math.Min(0.5f, vt.OffsetU));
            _numVarDy.Value = (decimal)Math.Max(-0.5f, Math.Min(0.5f, vt.OffsetV));
            _numVarSx.Value = (decimal)Math.Max(0.25f, Math.Min(4.0f, vt.ScaleX));
            _numVarSy.Value = (decimal)Math.Max(0.25f, Math.Min(4.0f, vt.ScaleY));
            _suppressFineTuneEvents = false;

            DisplayActiveTexture(resetView);
        }

        private void GetCurrentBaseRgba(out byte[] rgba, out int w, out int h, out YtdTextureEntry texEntry)
        {
            rgba = null; w = 0; h = 0; texEntry = null;
            if (_activeGroup == null || _activeVariant == null) return;
            var entries = GetCachedOrLoadYtd(_activeVariant);
            if (entries.Count == 0 || _activeTextureIndex < 0 || _activeTextureIndex >= entries.Count) return;

            texEntry = entries[_activeTextureIndex];
            string key = _activeVariant.VariantLetter.ToLowerInvariant() + "|" + texEntry.Name.ToLowerInvariant();
            if (_importedOverrides.TryGetValue(key, out ImportedOverrideEntry imp))
            {
                rgba = imp.Rgba;
                w = imp.Width;
                h = imp.Height;
                return;
            }

            rgba = texEntry.RgbaCache;
            w = texEntry.Width;
            h = texEntry.Height;
        }

        private void DisplayActiveTexture(bool resetView)
        {
            GetCurrentBaseRgba(out byte[] baseRgba, out int w, out int h, out YtdTextureEntry tex);
            if (baseRgba == null || tex == null || _activeGroup == null || _activeVariant == null)
            {
                _canvas.SetContent(null, null, 0, 0, null, "a");
                return;
            }

            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            _statusSize.Text = $"Boyut: {w} x {h} | Format: {tex.FormatStr} ({(tex.IsSrgb ? "sRGB" : "Linear")}) | Mipmap: {tex.MipLevels}";

            byte[] procRgba = ComputeProcessedRgbaForCurrent(baseRgba, w, h, tex, ms);
            _canvas.SetContent(
                baseRgba,
                procRgba,
                w,
                h,
                ms,
                _activeVariant.VariantLetter,
                _referencePatchRgba,
                resetView);
        }

        private void RunGroupAlignmentCheck()
        {
            if (_activeGroup == null)
            {
                _lblAlignmentBanner.Visible = false;
                return;
            }

            var sizes = new Dictionary<string, Size>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in _activeGroup.SortedVariants)
            {
                var entries = GetCachedOrLoadYtd(v);
                if (entries.Count > 0)
                    sizes[v.VariantLetter] = new Size(entries[0].Width, entries[0].Height);
            }

            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            if (sizes.Count > 0 && !ms.HasAnyMask())
            {
                var first = sizes.Values.First();
                ms.RefWidth = first.Width;
                ms.RefHeight = first.Height;
            }

            var warnings = GroupMaskState.CheckGroupAlignment(sizes, ms.RefWidth, ms.RefHeight);
            if (warnings.Count > 0)
            {
                string msg = "Hizalama Kontrolü: " + string.Join(" | ", warnings.Select(w => w.Message));
                _lblAlignmentBanner.Text = msg;
                _lblAlignmentBanner.Visible = true;
                foreach (var w in warnings)
                    AppendLog("[HİZALAMA UYARISI] " + w.Message);
            }
            else
            {
                _lblAlignmentBanner.Visible = false;
            }
        }

        // ------------------------------------------------------------------
        // Fill / Inpaint Preview
        // ------------------------------------------------------------------
        private FillSettings BuildCurrentFillSettings()
        {
            string[] methodCodes = { "telea", "ns", "clone", "solid", "donor", "transparent" };
            int mIdx = Math.Max(0, Math.Min(methodCodes.Length - 1, _cmbFillMethod.SelectedIndex));
            string method = methodCodes[mIdx];
            string alphaMode = "preserve";
            if (_cmbAlphaMode.SelectedIndex == 1) alphaMode = "opaque";
            else if (_cmbAlphaMode.SelectedIndex == 2 || method == "transparent") alphaMode = "transparent";

            var fs = new FillSettings
            {
                Method = method,
                InpaintRadius = 5,
                AlphaMode = alphaMode
            };

            if (_activeGroup != null)
            {
                var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
                fs.HasCloneSource = ms.HasCloneSource;
                fs.CloneSourceU = ms.CloneSourceU;
                fs.CloneSourceV = ms.CloneSourceV;

                if (method == "donor" && _cmbDonorVariant.SelectedIndex >= 0 && _cmbDonorVariant.SelectedIndex < _activeGroup.SortedVariants.Count)
                {
                    var donorVar = _activeGroup.SortedVariants[_cmbDonorVariant.SelectedIndex];
                    var donorEntries = GetCachedOrLoadYtd(donorVar);
                    if (donorEntries.Count > 0)
                    {
                        var dTex = donorEntries[Math.Min(_activeTextureIndex, donorEntries.Count - 1)];
                        fs.DonorRgba = dTex.RgbaCache;
                        fs.DonorWidth = dTex.Width;
                        fs.DonorHeight = dTex.Height;
                        fs.DonorVariantLetter = donorVar.VariantLetter;
                    }
                }
            }
            return fs;
        }

        private byte[] ComputeProcessedRgbaForCurrent(byte[] baseRgba, int w, int h, YtdTextureEntry tex, GroupMaskState ms)
        {
            if (!ms.HasAnyMask() || _activeVariant == null)
                return baseRgba;

            var entries = GetCachedOrLoadYtd(_activeVariant);
            bool isNs = SlotGrouper.IsNormalOrSpecTexture(tex.Name, _activeVariant.Stem, out _);
            if (isNs && !_chkApplyNormSpec.Checked && entries.Count > 1)
                return baseRgba;

            ms.GetVariantProcessedMasks(_activeVariant.VariantLetter, w, h, out byte[] rawBin, out byte[] dilatedBin, out float[] softAlpha);
            if (!dilatedBin.Any(b => b > 0))
                return baseRgba;

            if (_activeGroup != null)
            {
                var firstVar = _activeGroup.SortedVariants[0];
                var firstEntries = GetCachedOrLoadYtd(firstVar);
                if (firstEntries.Count > 0 && firstEntries[0].RgbaCache != null &&
                    firstEntries[0].Width == w && firstEntries[0].Height == h)
                {
                    byte[] refSrc = firstEntries[0].RgbaCache;
                    byte[] patch = new byte[w * h * 4];
                    for (int i = 0; i < w * h; i++)
                    {
                        if (dilatedBin[i] > 0)
                        {
                            int p = i * 4;
                            patch[p] = refSrc[p];
                            patch[p + 1] = refSrc[p + 1];
                            patch[p + 2] = refSrc[p + 2];
                            patch[p + 3] = 255;
                        }
                    }
                    _referencePatchRgba = patch;
                }
            }

            var fs = BuildCurrentFillSettings();
            return InpaintEngine.ApplyFillToRgba(baseRgba, w, h, rawBin, dilatedBin, softAlpha, fs);
        }

        private void SchedulePreviewUpdate()
        {
            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Start();
        }

        private void RecomputeLivePreview()
        {
            GetCurrentBaseRgba(out byte[] baseRgba, out int w, out int h, out YtdTextureEntry tex);
            if (baseRgba == null || tex == null || _activeGroup == null) return;
            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            byte[] proc = ComputeProcessedRgbaForCurrent(baseRgba, w, h, tex, ms);
            _canvas.UpdateProcessedPreview(proc, _referencePatchRgba);
        }

        // ------------------------------------------------------------------
        // Canvas & Edge Event Handlers
        // ------------------------------------------------------------------
        private void OnCanvasCursorCoordsChanged(object sender, CursorCoordsEventArgs e)
        {
            _statusCoords.Text = $"Piksel: ({e.PixelX}, {e.PixelY}) | Normalize: ({e.NormU:F4}, {e.NormV:F4}) | Çözünürlük: {e.ImageWidth}x{e.ImageHeight}";
        }

        private void OnCanvasMaskModified(object sender, EventArgs e)
        {
            if (_activeGroup != null && _activeVariant != null)
            {
                var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
                var vt = ms.GetVariantTransform(_activeVariant.VariantLetter);
                _suppressFineTuneEvents = true;
                _numVarDx.Value = (decimal)Math.Max(-0.5f, Math.Min(0.5f, vt.OffsetU));
                _numVarDy.Value = (decimal)Math.Max(-0.5f, Math.Min(0.5f, vt.OffsetV));
                _suppressFineTuneEvents = false;
            }
            SchedulePreviewUpdate();
        }

        private void OnCanvasCloneSourcePicked(object sender, PointF uv)
        {
            _statusNote.Text = $"Klon kaynak noktası seçildi: Normalize ({uv.X:F3}, {uv.Y:F3})";
            SchedulePreviewUpdate();
        }

        private void OnEdgeParamsChanged()
        {
            if (_activeGroup == null) return;
            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            ms.DilatePx = (int)_numDilate.Value;
            ms.FeatherPx = (int)_numFeather.Value;
            _canvas.RebuildMaskOverlayBitmap();
            SchedulePreviewUpdate();
        }

        private void OnVariantFineTuneChanged()
        {
            if (_suppressFineTuneEvents || _activeGroup == null || _activeVariant == null) return;
            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            var vt = ms.GetVariantTransform(_activeVariant.VariantLetter);
            vt.OffsetU = (float)_numVarDx.Value;
            vt.OffsetV = (float)_numVarDy.Value;
            vt.ScaleX = (float)_numVarSx.Value;
            vt.ScaleY = (float)_numVarSy.Value;
            _canvas.RebuildMaskOverlayBitmap();
            SchedulePreviewUpdate();
        }

        private void OnResetVariantFineTune()
        {
            if (_activeGroup == null || _activeVariant == null) return;
            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            var vt = ms.GetVariantTransform(_activeVariant.VariantLetter);
            vt.OffsetU = 0f; vt.OffsetV = 0f; vt.ScaleX = 1f; vt.ScaleY = 1f;
            _suppressFineTuneEvents = true;
            _numVarDx.Value = 0m;
            _numVarDy.Value = 0m;
            _numVarSx.Value = 1m;
            _numVarSy.Value = 1m;
            _suppressFineTuneEvents = false;
            _canvas.RebuildMaskOverlayBitmap();
            SchedulePreviewUpdate();
        }

        private void OnFillMethodChanged(object sender, EventArgs e)
        {
            int idx = _cmbFillMethod.SelectedIndex;
            _btnPickCloneSrc.Visible = (idx == 2);
            _pnlDonorRow.Visible = (idx == 4);
            if (idx == 5) // Şeffaflaştır / Sil (Alfa = 0)
            {
                _cmbAlphaMode.SelectedIndex = 2;
            }
            SchedulePreviewUpdate();
        }

        private void OnUndo()
        {
            if (_activeGroup == null) return;
            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            if (ms.Undo())
            {
                _canvas.RebuildMaskOverlayBitmap();
                SchedulePreviewUpdate();
                _statusNote.Text = "Geri alındı (Undo)";
            }
        }

        private void OnRedo()
        {
            if (_activeGroup == null) return;
            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            if (ms.Redo())
            {
                _canvas.RebuildMaskOverlayBitmap();
                SchedulePreviewUpdate();
                _statusNote.Text = "Yinelendi (Redo)";
            }
        }

        private void OnClearMask()
        {
            if (_activeGroup == null) return;
            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            ms.ClearMask();
            _canvas.RebuildMaskOverlayBitmap();
            SchedulePreviewUpdate();
            _statusNote.Text = "Aktif grubun maskesi temizlendi.";
        }

        // ------------------------------------------------------------------
        // Template Manager Actions
        // ------------------------------------------------------------------
        private void RefreshTemplateList()
        {
            _currentTemplateItems = _templateManager.ListTemplates(_txtSearchTemplates?.Text ?? "");
            _listTemplates.Items.Clear();
            foreach (var t in _currentTemplateItems)
            {
                _listTemplates.Items.Add($"{t.Name} [Ref: {t.RefWidth}x{t.RefHeight}, D:{t.Dilate} F:{t.Feather}]");
            }
        }

        private MaskTemplateInfo GetSelectedTemplateInfo()
        {
            int idx = _listTemplates.SelectedIndex;
            if (idx >= 0 && idx < _currentTemplateItems.Count)
                return _currentTemplateItems[idx];
            return null;
        }

        private void OnTemplateListSelected()
        {
            var info = GetSelectedTemplateInfo();
            if (info == null) return;

            if (!string.IsNullOrEmpty(info.Dto?.thumbnail_png_b64))
            {
                try
                {
                    byte[] pngBytes = Convert.FromBase64String(info.Dto.thumbnail_png_b64);
                    using (var ms = new MemoryStream(pngBytes))
                    {
                        var old = _picTemplateThumb.Image;
                        _picTemplateThumb.Image = new Bitmap(Image.FromStream(ms));
                        old?.Dispose();
                    }
                }
                catch { }
            }

            _lblTemplateMeta.Text = $"{info.Name}\nTarih: {info.CreatedAt}\nRef: {info.RefWidth}x{info.RefHeight} | Dilate:{info.Dilate} Feather:{info.Feather}";

            // Single-click loads template into active group
            if (_activeGroup != null)
            {
                OnLoadSelectedTemplate();
            }
        }

        private void OnLoadSelectedTemplate()
        {
            var info = GetSelectedTemplateInfo();
            if (info == null) return;
            if (_activeGroup == null)
            {
                MessageBox.Show(this, "Şablonu yüklemek için önce sol panelden bir slot grubu seçin.", "Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            _templateManager.ApplyTemplateToMaskState(info, ms);
            _numDilate.Value = ms.DilatePx;
            _numFeather.Value = ms.FeatherPx;
            _canvas.RebuildMaskOverlayBitmap();
            SchedulePreviewUpdate();
            RunGroupAlignmentCheck();
            AppendLog($"Şablon yüklendi: '{info.Name}' -> Grup '{_activeGroup.GroupKey}'");
            _statusNote.Text = "Şablon yüklendi: " + info.Name;
        }

        private void OnSaveTemplateClicked()
        {
            if (_activeGroup == null)
            {
                MessageBox.Show(this, "Lütfen önce bir slot grubu seçin ve maske çizin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            if (!ms.HasAnyMask())
            {
                MessageBox.Show(this, "Şablon olarak kaydetmek için önce tuval üzerinde bir maske alanı çizin.", "Maske Boş", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string name = _txtNewTemplateName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                name = PromptDialog.ShowTextPrompt(this, "Maske Şablonu Kaydet", "Şablon adı (örn. polis sol kol patch, göğüs rozeti):", "polis sol kol patch");
                if (string.IsNullOrWhiteSpace(name)) return;
            }

            GetCurrentBaseRgba(out byte[] baseRgba, out int w, out int h, out _);
            var info = _templateManager.SaveTemplate(name, ms, baseRgba, w, h);
            _txtNewTemplateName.Clear();
            RefreshTemplateList();
            AppendLog($"Maske şablonu kaydedildi: '{info.Name}' ({Path.GetFileName(info.FilePath)})");
            _statusNote.Text = "Şablon kaydedildi: " + info.Name;
        }

        private void OnRenameTemplate()
        {
            var info = GetSelectedTemplateInfo();
            if (info == null) return;
            string newName = PromptDialog.ShowTextPrompt(this, "Şablonu Yeniden Adlandır", "Yeni şablon adı:", info.Name);
            if (!string.IsNullOrWhiteSpace(newName))
            {
                _templateManager.RenameTemplate(info, newName);
                RefreshTemplateList();
            }
        }

        private void OnDuplicateTemplate()
        {
            var info = GetSelectedTemplateInfo();
            if (info == null) return;
            _templateManager.DuplicateTemplate(info);
            RefreshTemplateList();
        }

        private void OnDeleteTemplate()
        {
            var info = GetSelectedTemplateInfo();
            if (info == null) return;
            if (MessageBox.Show(this, $"'{info.Name}' şablonunu silmek istediğinize emin misiniz?", "Şablonu Sil", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _templateManager.DeleteTemplate(info);
                RefreshTemplateList();
            }
        }

        private void OnImportTemplate()
        {
            using (var ofd = new OpenFileDialog { Filter = "JSON Şablon Dosyası (*.json)|*.json", Multiselect = true })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (string f in ofd.FileNames)
                    {
                        try { _templateManager.ImportTemplate(f); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, "İçe Aktarma Hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    }
                    RefreshTemplateList();
                }
            }
        }

        private void OnExportTemplate()
        {
            var info = GetSelectedTemplateInfo();
            if (info == null) return;
            using (var sfd = new SaveFileDialog { Filter = "JSON Şablon Dosyası (*.json)|*.json", FileName = Path.GetFileName(info.FilePath) })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    _templateManager.ExportTemplate(info, sfd.FileName);
                    MessageBox.Show(this, "Şablon dışa aktarıldı:\n" + sfd.FileName, "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        // ------------------------------------------------------------------
        // Import Texture (PNG or DDS DXT5)
        // ------------------------------------------------------------------
        private void OnImportTexture()
        {
            if (_activeGroup == null || _activeVariant == null)
            {
                MessageBox.Show(this, "İçe aktarmak için önce bir varyant ve doku seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var entries = GetCachedOrLoadYtd(_activeVariant);
            if (entries.Count == 0 || _activeTextureIndex < 0 || _activeTextureIndex >= entries.Count) return;
            var curTex = entries[_activeTextureIndex];

            using (var ofd = new OpenFileDialog { Filter = "Desteklenen Görseller (*.png;*.dds)|*.png;*.dds|PNG (*.png)|*.png|DDS DXT5 (*.dds)|*.dds" })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                string ext = Path.GetExtension(ofd.FileName).ToLowerInvariant();
                byte[] impRgba;
                int impW, impH;

                try
                {
                    if (ext == ".dds")
                    {
                        byte[] ddsBytes = File.ReadAllBytes(ofd.FileName);
                        impRgba = _ddsCodec.DecodeDdsToRgba(ddsBytes, out DdsMetadata meta);
                        impW = meta.Width;
                        impH = meta.Height;
                    }
                    else if (ext == ".png")
                    {
                        using (var fs = new FileStream(ofd.FileName, FileMode.Open, FileAccess.Read))
                        using (var bmp = new Bitmap(fs))
                        {
                            impW = bmp.Width;
                            impH = bmp.Height;
                            impRgba = DarkTheme.BitmapToStraightRgba(bmp);
                        }
                    }
                    else
                    {
                        MessageBox.Show(this, "Sadece .png veya .dds (DXT5) dosyaları kabul edilir.", "Geçersiz Format", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Görsel okunamadı:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                int origW = curTex.Width;
                int origH = curTex.Height;

                if (impW != origW || impH != origH || (impW % 4 != 0) || (impH % 4 != 0))
                {
                    using (var dlg = new ImportOptionsForm(origW, origH, impW, impH))
                    {
                        if (dlg.ShowDialog(this) != DialogResult.OK)
                            return;

                        if (dlg.SelectedMode == "scale_preserve")
                        {
                            impRgba = ResizeRgbaPreserveAspect(impRgba, impW, impH, origW, origH);
                            impW = origW;
                            impH = origH;
                        }
                        else if (dlg.SelectedMode == "scale_stretch")
                        {
                            impRgba = ResizeRgbaExact(impRgba, impW, impH, origW, origH);
                            impW = origW;
                            impH = origH;
                        }
                        else
                        {
                            int fw = dlg.FixedNewSize.Width;
                            int fh = dlg.FixedNewSize.Height;
                            if (impW != fw || impH != fh)
                            {
                                impRgba = ResizeRgbaExact(impRgba, impW, impH, fw, fh);
                                impW = fw;
                                impH = fh;
                            }
                        }
                    }
                }

                string key = _activeVariant.VariantLetter.ToLowerInvariant() + "|" + curTex.Name.ToLowerInvariant();
                _importedOverrides[key] = new ImportedOverrideEntry { Rgba = impRgba, Width = impW, Height = impH };
                AppendLog($"Görsel içe aktarıldı (Alfa korundu): {Path.GetFileName(ofd.FileName)} -> {curTex.Name} ({impW}x{impH})");
                DisplayActiveTexture(false);
            }
        }

        private static byte[] ResizeRgbaExact(byte[] srcRgba, int srcW, int srcH, int dstW, int dstH)
        {
            byte[] dst = new byte[dstW * dstH * 4];
            for (int y = 0; y < dstH; y++)
            {
                float fy = (float)y * (srcH - 1) / Math.Max(1, dstH - 1);
                int y0 = (int)Math.Floor(fy);
                int y1 = Math.Min(srcH - 1, y0 + 1);
                float wy = fy - y0;

                for (int x = 0; x < dstW; x++)
                {
                    float fx = (float)x * (srcW - 1) / Math.Max(1, dstW - 1);
                    int x0 = (int)Math.Floor(fx);
                    int x1 = Math.Min(srcW - 1, x0 + 1);
                    float wx = fx - x0;

                    int p00 = (y0 * srcW + x0) * 4;
                    int p10 = (y0 * srcW + x1) * 4;
                    int p01 = (y1 * srcW + x0) * 4;
                    int p11 = (y1 * srcW + x1) * 4;
                    int dstIdx = (y * dstW + x) * 4;

                    for (int c = 0; c < 4; c++)
                    {
                        float top = srcRgba[p00 + c] * (1f - wx) + srcRgba[p10 + c] * wx;
                        float bot = srcRgba[p01 + c] * (1f - wx) + srcRgba[p11 + c] * wx;
                        dst[dstIdx + c] = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(top * (1f - wy) + bot * wy)));
                    }
                }
            }
            return dst;
        }

        private static byte[] ResizeRgbaPreserveAspect(byte[] srcRgba, int srcW, int srcH, int dstW, int dstH)
        {
            float scale = Math.Min((float)dstW / Math.Max(1, srcW), (float)dstH / Math.Max(1, srcH));
            int nw = Math.Max(1, (int)Math.Round(srcW * scale));
            int nh = Math.Max(1, (int)Math.Round(srcH * scale));
            byte[] scaled = ResizeRgbaExact(srcRgba, srcW, srcH, nw, nh);

            byte[] canvas = new byte[dstW * dstH * 4];
            int offX = (dstW - nw) / 2;
            int offY = (dstH - nh) / 2;
            for (int y = 0; y < nh; y++)
            {
                for (int x = 0; x < nw; x++)
                {
                    int s = (y * nw + x) * 4;
                    int d = ((offY + y) * dstW + (offX + x)) * 4;
                    canvas[d] = scaled[s];
                    canvas[d + 1] = scaled[s + 1];
                    canvas[d + 2] = scaled[s + 2];
                    canvas[d + 3] = scaled[s + 3];
                }
            }
            return canvas;
        }

        // ------------------------------------------------------------------
        // Save, Batch Save, and Save as New Slot
        // ------------------------------------------------------------------
        private static string ResolveOutputYtdPath(string sourceYtdPath, string targetFileName, bool overwriteOriginal, string outputSubfolder)
        {
            string parentDir = Path.GetDirectoryName(sourceYtdPath) ?? "";
            if (overwriteOriginal)
                return Path.Combine(parentDir, targetFileName);
            string outDir = Path.Combine(parentDir, outputSubfolder);
            Directory.CreateDirectory(outDir);
            return Path.Combine(outDir, targetFileName);
        }

        private bool ConfirmOverwriteIfExists(IEnumerable<string> targetPaths)
        {
            var existing = targetPaths.Where(File.Exists).ToList();
            if (existing.Count == 0) return true;

            string listStr = string.Join("\n", existing.Take(8).Select(p => "• " + p));
            if (existing.Count > 8)
                listStr += $"\n... ve {existing.Count - 8} dosya daha";

            return MessageBox.Show(
                this,
                $"Aşağıdaki hedef dosya(lar) zaten mevcut:\n\n{listStr}\n\nÜstüne yazmak istediğinize emin misiniz? (Orijinal dosyalar için otomatik .bak yedeği alınır)",
                "Hedef Dosya Zaten Mevcut",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void OnSaveClicked()
        {
            if (_activeGroup == null || _activeVariant == null)
            {
                MessageBox.Show(this, "Kaydetmek için önce bir slot grubu seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<VariantEntry> variants;
            if (_activeGroup.Variants.Count > 1)
            {
                var dr = MessageBox.Show(
                    this,
                    $"Seçili gruptaki ({_activeGroup.GroupKey}) TÜM varyantları ({_activeGroup.Variants.Count} dosya) kaydetmek için 'Evet',\nsadece aktif sekmeyi ({_activeVariant.VariantLetter.ToUpperInvariant()}) kaydetmek için 'Hayır' seçin.",
                    "Kaydetme Kapsamı",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Cancel) return;
                variants = (dr == DialogResult.Yes) ? _activeGroup.SortedVariants : new List<VariantEntry> { _activeVariant };
            }
            else
            {
                variants = new List<VariantEntry> { _activeVariant };
            }

            RunSaveTaskAsync(variants, false, null, null, false);
        }

        private void OnBatchSelectedGroup()
        {
            if (_activeGroup == null)
            {
                MessageBox.Show(this, "Lütfen önce sol panelden bir slot grubu seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RunSaveTaskAsync(_activeGroup.SortedVariants, false, null, null, false);
        }

        private void OnSaveNewSlotClicked()
        {
            if (_activeGroup == null || _activeVariant == null)
            {
                MessageBox.Show(this, "Yeni slot oluşturmak için önce bir varyant seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            GetCurrentBaseRgba(out byte[] baseRgba, out int w, out int h, out YtdTextureEntry tex);
            if (baseRgba == null || tex == null) return;

            var ms = GetOrCreateGroupMask(_activeGroup.GroupKey);
            byte[] cleaned = ComputeProcessedRgbaForCurrent(baseRgba, w, h, tex, ms);

            using (var dlg = new NewSlotForm(_activeGroup, _activeVariant, cleaned, w, h, tex.Name))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                string newLetter = dlg.GetNewLetter();
                HsbSettings hsb = dlg.GetHsbSettings();
                bool alsoCleanOrig = (dlg.OriginalDisposition == 2);

                RunSaveTaskAsync(
                    new List<VariantEntry> { _activeVariant },
                    true,
                    newLetter,
                    hsb,
                    alsoCleanOrig);
            }
        }

        private async void RunSaveTaskAsync(
            List<VariantEntry> variants,
            bool isNewSlot,
            string newSlotLetter,
            HsbSettings newSlotHsb,
            bool alsoCleanOriginal)
        {
            if (_activeGroup == null) return;

            bool overwriteOrig = _config.OverwriteOriginal;
            string outSub = _config.OutputSubfolderName;

            var targetMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in variants)
            {
                string fname = (isNewSlot && !string.IsNullOrEmpty(newSlotLetter))
                    ? (_activeGroup.BuildVariantStem(newSlotLetter, v) + ".ytd")
                    : v.FileName;
                targetMap[v.VariantLetter] = ResolveOutputYtdPath(v.FilePath, fname, overwriteOrig, outSub);
            }

            var checkList = targetMap.Values.ToList();
            if (isNewSlot && alsoCleanOriginal && variants.Count > 0)
                checkList.Add(variants[0].FilePath);

            if (!ConfirmOverwriteIfExists(checkList))
                return;

            var maskState = GetOrCreateGroupMask(_activeGroup.GroupKey);
            var fillSettings = BuildCurrentFillSettings();
            bool applyNs = _chkApplyNormSpec.Checked;
            var overridesSnapshot = new Dictionary<string, ImportedOverrideEntry>(_importedOverrides, StringComparer.OrdinalIgnoreCase);

            SetUiBusy(true);
            int totalSteps = variants.Count + (alsoCleanOriginal ? 1 : 0);
            int step = 0;
            bool allOk = true;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var v in variants)
                    {
                        step++;
                        string outPath = targetMap[v.VariantLetter];
                        UpdateProgressUi(step, totalSteps, "İşleniyor: " + Path.GetFileName(outPath));
                        AppendLog($"▶ [{step}/{totalSteps}] Kaydediliyor: {v.FileName} -> {outPath}");

                        bool ok = ProcessAndSaveSingleYtd(
                            v,
                            outPath,
                            maskState,
                            fillSettings,
                            applyNs,
                            overridesSnapshot,
                            isNewSlot ? newSlotLetter : null,
                            isNewSlot ? newSlotHsb : null,
                            true);
                        if (!ok) allOk = false;
                    }

                    if (isNewSlot && alsoCleanOriginal && variants.Count > 0)
                    {
                        step++;
                        var srcVar = variants[0];
                        string origOut = srcVar.FilePath;
                        UpdateProgressUi(step, totalSteps, "Orijinal güncelleniyor (.bak yedekli): " + srcVar.FileName);
                        AppendLog($"▶ [{step}/{totalSteps}] Orijinal dosya temizlenmiş haliyle güncelleniyor (.bak yedeği alınarak): {origOut}");

                        string bakPath = origOut + ".bak";
                        File.Copy(origOut, bakPath, true);
                        AppendLog("  • Orijinal yedeği alındı: " + Path.GetFileName(bakPath));

                        bool ok2 = ProcessAndSaveSingleYtd(
                            srcVar,
                            origOut,
                            maskState,
                            fillSettings,
                            applyNs,
                            overridesSnapshot,
                            null,
                            null,
                            false);
                        if (!ok2) allOk = false;
                    }
                });

                SetUiBusy(false);
                _progressBar.Value = allOk ? 100 : 0;

                if (isNewSlot && allOk)
                {
                    MessageBox.Show(
                        this,
                        "Yeni slot başarıyla oluşturuldu ve doğrulandı.\n\nHATIRLATMA:\n.meta / component texture sayısını güncellemeyi unutma.",
                        "Yeni Slot Oluşturuldu",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    if (!string.IsNullOrEmpty(_currentFolder))
                        LoadFolder(_currentFolder);
                }
                else if (allOk)
                {
                    MessageBox.Show(this, "Kayıt ve YTD doğrulaması başarıyla tamamlandı.", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(this, "Bazı dosyalar kaydedilirken veya doğrulanırken hata oluştu. Lütfen işlem logunu inceleyin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                SetUiBusy(false);
                AppendLog("[KRİTİK HATA] " + ex.Message);
                MessageBox.Show(this, "İşlem hatası:\n" + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ProcessAndSaveSingleYtd(
            VariantEntry variant,
            string outputYtdPath,
            GroupMaskState maskState,
            FillSettings fillSettings,
            bool applyToNormalSpec,
            Dictionary<string, ImportedOverrideEntry> overridesMap,
            string newVariantLetter,
            HsbSettings hsbSettings,
            bool createBackupIfOverwrite)
        {
            var entries = _ytdService.LoadYtd(variant.FilePath, true);
            if (entries.Count == 0)
            {
                AppendLog($"  [HATA] {variant.FileName} içinde hiç doku bulunamadı.");
                return false;
            }

            var updatePlans = new List<TextureUpdatePlan>();
            var expectedSpecs = new List<ExpectedTextureSpec>();
            bool hasMask = maskState.HasAnyMask();
            bool hsbActive = hsbSettings != null && hsbSettings.IsActive;

            foreach (var entry in entries)
            {
                string origName = entry.Name;
                string newName = !string.IsNullOrEmpty(newVariantLetter)
                    ? SlotGrouper.ReplaceVariantLetterInName(origName, variant.VariantLetter, newVariantLetter)
                    : origName;

                bool isNs = SlotGrouper.IsNormalOrSpecTexture(origName, variant.Stem, out _);
                string ovKey = variant.VariantLetter.ToLowerInvariant() + "|" + origName.ToLowerInvariant();
                ImportedOverrideEntry imp = null;
                if (overridesMap != null)
                    overridesMap.TryGetValue(ovKey, out imp);

                bool shouldApplyMask = hasMask && (!isNs || applyToNormalSpec || entries.Count == 1);
                bool shouldModify = (imp != null) || shouldApplyMask || (hsbActive && !isNs);

                if (!shouldModify)
                {
                    updatePlans.Add(new TextureUpdatePlan
                    {
                        OriginalName = origName,
                        NewName = newName,
                        ReplacementDdsBytes = null,
                        PreserveFormatFlags = true
                    });
                    expectedSpecs.Add(new ExpectedTextureSpec
                    {
                        ExpectedName = newName,
                        ExpectedWidth = entry.Width,
                        ExpectedHeight = entry.Height,
                        WasModified = false,
                        OriginalRgba = entry.RgbaCache
                    });
                    AppendLog($"  • '{origName}' dokusuna dokunulmadı (orijinal sıkıştırılmış baytları korundu).");
                    continue;
                }

                byte[] baseRgba = imp != null ? (byte[])imp.Rgba.Clone() : (byte[])entry.RgbaCache.Clone();
                int tw = imp != null ? imp.Width : entry.Width;
                int th = imp != null ? imp.Height : entry.Height;

                byte[] workingRgba = baseRgba;
                byte[] footprintMask = null;

                if (shouldApplyMask)
                {
                    maskState.GetVariantProcessedMasks(variant.VariantLetter, tw, th, out byte[] rawBin, out byte[] dilatedBin, out float[] softAlpha);
                    if (dilatedBin.Any(b => b > 0))
                    {
                        workingRgba = InpaintEngine.ApplyFillToRgba(workingRgba, tw, th, rawBin, dilatedBin, softAlpha, fillSettings);
                        footprintMask = dilatedBin;
                    }
                }

                if (hsbActive && !isNs)
                {
                    workingRgba = InpaintEngine.ApplyHsbAdjustment(workingRgba, tw, th, hsbSettings);
                }

                bool canBlockPatch = (imp == null) && !(hsbActive && !isNs) && (footprintMask != null) && (tw == entry.Width) && (th == entry.Height);

                byte[] encodedDds = _ddsCodec.EncodeRgbaToDxt5Dds(
                    workingRgba,
                    tw,
                    th,
                    entry.IsSrgb,
                    canBlockPatch ? entry.RawDdsBytes : null,
                    canBlockPatch ? footprintMask : null,
                    out DdsEncodeStats stats);

                if (stats.BlockPatched)
                {
                    AppendLog($"  • '{newName}' DXT5 encode edildi (4x4 Blok Koruma: {stats.TouchedBlocks}/{stats.TotalBlocks} blok güncellendi, kalan bloklar orijinal baytlarıyla korundu).");
                }
                else
                {
                    AppendLog($"  • '{newName}' yüksek kalite DXT5 (BC3_UNORM + Mipmap) olarak encode edildi.");
                }

                updatePlans.Add(new TextureUpdatePlan
                {
                    OriginalName = origName,
                    NewName = newName,
                    ReplacementDdsBytes = encodedDds,
                    PreserveFormatFlags = true
                });

                expectedSpecs.Add(new ExpectedTextureSpec
                {
                    ExpectedName = newName,
                    ExpectedWidth = tw,
                    ExpectedHeight = th,
                    WasModified = true,
                    OriginalRgba = baseRgba,
                    ModifiedFootprintMask = footprintMask,
                    HsbApplied = (hsbActive && !isNs)
                });
            }

            _ytdService.SaveYtd(variant.FilePath, outputYtdPath, updatePlans, createBackupIfOverwrite);

            var verifyRes = Verifier.VerifySavedYtd(_ytdService, outputYtdPath, expectedSpecs);
            AppendLog("  ✓ " + verifyRes.Summary);

            if (!verifyRes.Passed)
            {
                string invalidPath = outputYtdPath + ".gecersiz";
                try
                {
                    if (File.Exists(invalidPath)) File.Delete(invalidPath);
                    if (File.Exists(outputYtdPath)) File.Move(outputYtdPath, invalidPath);
                    AppendLog("  [HATA] Doğrulama başarısız olduğu için dosya geçersiz işaretlendi: " + Path.GetFileName(invalidPath));
                }
                catch { }
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Batch Mode 2: Apply Saved Template(s) across ALL Groups in Folder
        // ------------------------------------------------------------------
        private async void OnBatchAllGroups()
        {
            if (_slotGroups.Count == 0)
            {
                MessageBox.Show(this, "Lütfen önce sol panelden bir YTD klasörü açın.", "Klasör Açılmadı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var templates = _templateManager.ListTemplates();
            if (templates.Count == 0)
            {
                MessageBox.Show(this, "Tüm gruplara toplu uygulamak için önce sağdaki Şablon Yöneticisi'nden en az bir maske şablonu kaydedin.", "Şablon Yok", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<MaskTemplateInfo> selectedTemplates;
            using (var dlg = new BatchAllGroupsForm(templates, _slotGroups))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
                selectedTemplates = dlg.GetSelectedTemplates();
            }

            var fillSettings = BuildCurrentFillSettings();
            bool applyNs = _chkApplyNormSpec.Checked;
            bool overwriteOrig = _config.OverwriteOriginal;
            string outSub = _config.OutputSubfolderName;

            int totalFiles = _slotGroups.Sum(g => g.Variants.Count);
            int succeeded = 0;
            int failed = 0;
            var details = new List<string>();

            SetUiBusy(true);
            AppendLog($"=== TOPLU İŞLEM BAŞLADI: {_slotGroups.Count} grup ({totalFiles} YTD) ===");

            await Task.Run(() =>
            {
                var combinedMask = new GroupMaskState("batch_combined", 1024);
                int maxDil = 0, maxFea = 0;
                foreach (var tpl in selectedTemplates)
                {
                    var tmp = new GroupMaskState("tmp", 1024);
                    _templateManager.ApplyTemplateToMaskState(tpl, tmp);
                    for (int i = 0; i < combinedMask.NormGrid.Length; i++)
                    {
                        if (tmp.NormGrid[i] > combinedMask.NormGrid[i])
                            combinedMask.NormGrid[i] = tmp.NormGrid[i];
                    }
                    maxDil = Math.Max(maxDil, tmp.DilatePx);
                    maxFea = Math.Max(maxFea, tmp.FeatherPx);
                }
                combinedMask.DilatePx = maxDil;
                combinedMask.FeatherPx = maxFea;

                int fileIdx = 0;
                foreach (var grp in _slotGroups)
                {
                    foreach (var v in grp.SortedVariants)
                    {
                        fileIdx++;
                        UpdateProgressUi(fileIdx, totalFiles, $"[{fileIdx}/{totalFiles}] {v.FileName}");
                        try
                        {
                            string outPath = ResolveOutputYtdPath(v.FilePath, v.FileName, overwriteOrig, outSub);
                            bool ok = ProcessAndSaveSingleYtd(
                                v,
                                outPath,
                                combinedMask,
                                fillSettings,
                                applyNs,
                                null,
                                null,
                                null,
                                true);

                            if (ok)
                            {
                                succeeded++;
                                details.Add($"[BAŞARILI] {v.FileName} -> {outPath}");
                            }
                            else
                            {
                                failed++;
                                details.Add($"[DOĞRULAMA HATASI] {v.FileName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            string err = $"[HATA] {v.FileName}: {ex.Message}";
                            AppendLog(err);
                            details.Add(err);
                        }
                    }
                }
            });

            SetUiBusy(false);
            _progressBar.Value = 100;
            string summaryHeader = $"Toplam Grup: {_slotGroups.Count} | Hedeflenen YTD: {totalFiles} | Başarılı: {succeeded} | Hatalı: {failed}";
            _lblProgStatus.Text = summaryHeader;
            AppendLog("=== TOPLU İŞLEM TAMAMLANDI: " + summaryHeader + " ===");

            using (var rep = new ReportForm("Toplu İşlem Özet Raporu", summaryHeader, details))
            {
                rep.ShowDialog(this);
            }
        }

        private void UpdateProgressUi(int current, int total, string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, int, string>(UpdateProgressUi), current, total, text);
                return;
            }
            int pct = Math.Min(100, Math.Max(0, (int)Math.Round(current * 100.0 / Math.Max(1, total))));
            _progressBar.Value = pct;
            _lblProgStatus.Text = text;
        }

        private void SetUiBusy(bool busy)
        {
            _btnOpenFolder.Enabled = !busy;
            _btnBatchGroup.Enabled = !busy;
            _btnBatchFolder.Enabled = !busy;
            BtnToolSave.Enabled = !busy;
            BtnToolSaveNewSlot.Enabled = !busy;
        }

        // ------------------------------------------------------------------
        // Multi-Language Switcher (TR / EN / DE)
        // ------------------------------------------------------------------
        public void ApplyLanguage(string lang)
        {
            if (string.IsNullOrEmpty(lang)) lang = "tr";
            Loc.Lang = lang.ToLowerInvariant();
            _config.Language = Loc.Lang;
            _config.Save();

            // Menu checkmarks
            if (_mLangTr != null) _mLangTr.Checked = (Loc.Lang == "tr");
            if (_mLangEn != null) _mLangEn.Checked = (Loc.Lang == "en");
            if (_mLangDe != null) _mLangDe.Checked = (Loc.Lang == "de");

            // Form Title
            Text = Loc.T(
                "YTD Editor - GTA5 / FiveM Kıyafet Doku ve Patch Düzenleyici",
                "YTD Editor - GTA5 / FiveM Clothing Texture & Patch Editor",
                "YTD Editor - GTA5 / FiveM Kleidungs-Textur & Patch Editor");

            // Main Menu
            if (_mFile != null) _mFile.Text = Loc.T("Dosya", "File", "Datei");
            if (_miFileOpen != null) _miFileOpen.Text = Loc.T("Klasör Seç...", "Select Folder...", "Ordner auswählen...");
            if (_miFileImport != null) _miFileImport.Text = Loc.T("Görsel İçe Aktar (PNG / DDS DXT5)...", "Import Image (PNG / DDS DXT5)...", "Bild importieren (PNG / DDS DXT5)...");
            if (_miFileSave != null) _miFileSave.Text = Loc.T("Kaydet (Seçili Varyant / Grup)", "Save (Selected Variant / Group)", "Speichern (Ausgewählte Variante / Gruppe)");
            if (_miFileSaveNew != null) _miFileSaveNew.Text = Loc.T("Yeni Slot Olarak Kaydet...", "Save as New Slot...", "Als neuen Slot speichern...");
            if (_miFileExit != null) _miFileExit.Text = Loc.T("Çıkış", "Exit", "Beenden");

            if (_mEdit != null) _mEdit.Text = Loc.T("Düzenle", "Edit", "Bearbeiten");
            if (_miEditUndo != null) _miEditUndo.Text = Loc.T("Geri Al (Ctrl+Z)", "Undo (Ctrl+Z)", "Rückgängig (Strg+Z)");
            if (_miEditRedo != null) _miEditRedo.Text = Loc.T("Yinele (Ctrl+Y)", "Redo (Ctrl+Y)", "Wiederholen (Strg+Y)");
            if (_miEditClear != null) _miEditClear.Text = Loc.T("Maskeyi Temizle (Delete)", "Clear Mask (Delete)", "Maske löschen (Entf)");

            if (_mBatch != null) _mBatch.Text = Loc.T("Toplu İşlem", "Batch Process", "Stapelverarbeitung");
            if (_miBatchGroup != null) _miBatchGroup.Text = Loc.T("Seçili Gruba Toplu Uygula (A, B, C...)", "Batch Apply to Selected Group (A, B, C...)", "Stapelweise auf Gruppe anwenden (A, B, C...)");
            if (_miBatchFolder != null) _miBatchFolder.Text = Loc.T("Klasördeki Tüm Gruplara Şablon Uygula...", "Apply Template to All Groups in Folder...", "Vorlage auf alle Ordnergruppen anwenden...");

            if (_mSettings != null) _mSettings.Text = Loc.T("Ayarlar", "Settings", "Einstellungen");
            if (_mSelfCheck != null) _mSelfCheck.Text = Loc.T("Kendi Kendini Kontrol Et", "Self-Diagnostics", "Selbsttest");
            if (_mLangMenu != null) _mLangMenu.Text = Loc.T("🌐 Dil", "🌐 Language", "🌐 Sprache");

            // Toolbar Tooltips
            if (_btnToolRect != null) _toolTip.SetToolTip(_btnToolRect, Loc.T(
                "Kare / Dikdörtgen Seçim Aracı (Kısayol: R)\nSürükleyerek keskin kare/dikdörtgen temizleme alanı belirler.\nShift: Tam kare | Sağ tık sürükle: Maskeden siler.",
                "Rectangle / Square Selection Tool (Shortcut: R)\nDrag to define sharp rectangle/square cleaning area.\nShift: Constrain square | Right drag: Erase from mask.",
                "Rechteck / Quadrat Auswahlwerkzeug (Kürzel: R)\nZiehen, um scharfen rechteckigen/quadratischen Bereich festzulegen.\nShift: Quadrat | Rechtsklick-Ziehen: Aus Maske löschen."));

            if (_btnToolCircle != null) _toolTip.SetToolTip(_btnToolCircle, Loc.T(
                "Daire / Elips Seçim Aracı (Kısayol: C)\nSürükleyerek keskin daire/elips temizleme alanı belirler.\nShift: Tam daire | Sağ tık sürükle: Maskeden siler.",
                "Circle / Ellipse Selection Tool (Shortcut: C)\nDrag to define sharp circle/ellipse cleaning area.\nShift: Constrain circle | Right drag: Erase from mask.",
                "Kreis / Ellipse Auswahlwerkzeug (Kürzel: C)\nZiehen, um scharfen runden/elliptischen Bereich festzulegen.\nShift: Kreis | Rechtsklick-Ziehen: Aus Maske löschen."));

            if (_btnToolTriangle != null) _toolTip.SetToolTip(_btnToolTriangle, Loc.T(
                "Üçgen Seçim Aracı (Kısayol: T)\nSürükleyerek keskin üçgen temizleme alanı belirler.\nSağ tık sürükle: Maskeden siler.",
                "Triangle Selection Tool (Shortcut: T)\nDrag to define sharp triangle cleaning area.\nRight drag: Erase from mask.",
                "Dreieck Auswahlwerkzeug (Kürzel: T)\nZiehen, um scharfen dreieckigen Bereich festzulegen.\nRechtsklick-Ziehen: Aus Maske löschen."));

            if (_btnToolBrush != null) _toolTip.SetToolTip(_btnToolBrush, Loc.T(
                "Fırça Aracı (Kısayol: B)\nSol tık: Keskin maske boyar | Sağ tık: Maske siler.",
                "Brush Tool (Shortcut: B)\nLeft click: Paint sharp mask | Right click: Erase mask.",
                "Pinselwerkzeug (Kürzel: B)\nLinksklick: Scharfe Maske zeichnen | Rechtsklick: Maske löschen."));

            if (_btnToolEraser != null) _toolTip.SetToolTip(_btnToolEraser, Loc.T(
                "Silgi Aracı (Kısayol: E)\nMaskelenmiş alanı keskin olarak siler.",
                "Eraser Tool (Shortcut: E)\nErases masked areas sharply.",
                "Radiererwerkzeug (Kürzel: E)\nLöscht maskierte Bereiche scharfkantig."));

            if (_btnToolMove != null) _toolTip.SetToolTip(_btnToolMove, Loc.T(
                "Maskeyi Taşı (Kısayol: M)\nMaskeyi sürükleyerek veya ok tuşlarıyla kaydırır.\nShift + Ok: 10px adımla kaydırır.\nAlt basılıyken: Yalnızca aktif sekmedeki varyantı kaydırır.",
                "Move Mask Tool (Shortcut: M)\nDrag or use arrow keys to offset mask.\nShift + Arrow: 10px step.\nAlt key: Offset only the active variant.",
                "Maske verschieben (Kürzel: M)\nZiehen oder Pfeiltasten zum Verschieben der Maske.\nShift + Pfeil: 10px Schritt.\nAlt-Taste: Nur aktive Variante verschieben."));

            if (_btnToolClonePick != null) _toolTip.SetToolTip(_btnToolClonePick, Loc.T(
                "Klon Kaynak Noktası Seç\nKlonlama doldurma yöntemi için tuval üzerinden kaynak doku noktası seçer.",
                "Pick Clone Source Point\nPick source texture point on canvas for cloning fill mode.",
                "Klonquellpunkt wählen\nWählt Quelltexturpunkt auf der Leinwand für den Klonmodus."));

            if (_lblSzTag != null)
            {
                _lblSzTag.Text = Loc.T("Boyut:", "Size:", "Größe:");
                _toolTip.SetToolTip(_lblSzTag, Loc.T("Fırça ve silgi yarıçapı (1 - 150 piksel)", "Brush and eraser radius (1 - 150 pixels)", "Pinsel- und Radiererradius (1 - 150 Pixel)"));
            }
            if (_sliderBrushSize != null) _toolTip.SetToolTip(_sliderBrushSize, Loc.T("Fırça Boyutu: 1 - 150 piksel", "Brush Size: 1 - 150 pixels", "Pinselgröße: 1 - 150 Pixel"));

            if (_lblOpTag != null)
            {
                _lblOpTag.Text = Loc.T("Opak:", "Opacity:", "Deckk.:");
                _toolTip.SetToolTip(_lblOpTag, Loc.T("Tuval üzerindeki maske önizleme opaklığı (%0 - %100)", "Mask preview opacity on canvas (0% - 100%)", "Maskenvorschau-Deckkraft auf der Leinwand (0% - 100%)"));
            }
            if (_sliderMaskOpacity != null) _toolTip.SetToolTip(_sliderMaskOpacity, Loc.T("Maske Opaklığı: %0 - %100", "Mask Opacity: 0% - 100%", "Maskendeckkraft: 0% - 100%"));

            if (_btnMaskColor != null) _toolTip.SetToolTip(_btnMaskColor, Loc.T(
                "Maske Rengi Seç\nTuval üzerindeki yarı saydam maske rengini değiştirir.",
                "Pick Mask Color\nChange translucent mask color on canvas.",
                "Maskenfarbe wählen\nÄndert die halbtransparente Maskenfarbe auf der Leinwand."));

            if (_btnToolSplit != null) _toolTip.SetToolTip(_btnToolSplit, Loc.T(
                "Önce / Sonra Karşılaştırma Sürgüsü (Split View)\nOrijinal ile temizlenmiş sonucu karşılaştırmak için ortadaki çizgiyi sürükleyin.",
                "Before / After Comparison Slider (Split View)\nDrag center line to compare original vs cleaned result.",
                "Vorher / Nachher Vergleichsschieberegler (Split View)\nMittellinie ziehen, um Original und bereinigtes Ergebnis zu vergleichen."));

            if (_btnToolAlignOverlay != null) _toolTip.SetToolTip(_btnToolAlignOverlay, Loc.T(
                "Hizalama Kontrol Overlay\nReferans varyantın patch bölgesini yarı saydam olarak diğer varyantların üstünde gösterir.",
                "Alignment Check Overlay\nShows reference variant's patch region translucent over others.",
                "Ausrichtungs-Prüfungs-Overlay\nZeigt den Patch-Bereich der Referenzvariante halbtransparent über anderen an."));

            if (_chkToolChecker != null) _toolTip.SetToolTip(_chkToolChecker, Loc.T(
                "Dama Arka Planı Göster / Gizle\nŞeffaf bölgelerin altında dama desenini gösterir.",
                "Show / Hide Checkerboard Background\nDisplays checkerboard under transparent regions.",
                "Schachbrett-Hintergrund Ein / Aus\nZeigt Schachbrettmuster unter transparenten Bereichen an."));

            if (_chkToolAlpha != null) _toolTip.SetToolTip(_chkToolAlpha, Loc.T(
                "Alfa Kanalını Göster / Gizle\nDokunun şeffaflık (alfa) kanalını önizlemede açar veya kapatır.",
                "Show / Hide Alpha Channel\nToggles texture transparency (alpha) in preview.",
                "Alpha-Kanal Ein / Aus\nSchaltet Textur-Transparenz (Alpha) in der Vorschau um."));

            if (_btnToolZoomOut != null) _toolTip.SetToolTip(_btnToolZoomOut, Loc.T("Uzaklaş (Zoom Out - Fare Tekerleği)", "Zoom Out (Mouse Wheel)", "Verkleinern (Zoom Out - Mausrad)"));
            if (_btnToolZoomIn != null) _toolTip.SetToolTip(_btnToolZoomIn, Loc.T("Yakınlaş (Zoom In - Fare Tekerleği)", "Zoom In (Mouse Wheel)", "Vergrößern (Zoom In - Mausrad)"));
            if (_btnToolZoomFit != null) _toolTip.SetToolTip(_btnToolZoomFit, Loc.T("Ekrana Sığdır (Fit)", "Fit to Screen", "Einpassen (Fit)"));
            if (_btnToolZoom1to1 != null) _toolTip.SetToolTip(_btnToolZoom1to1, Loc.T("Gerçek Boyut (1:1 Piksel)", "Actual Size (1:1 Pixel)", "Tatsächliche Größe (1:1 Pixel)"));

            if (_btnToolUndo != null) _toolTip.SetToolTip(_btnToolUndo, Loc.T("Geri Al (Ctrl+Z)", "Undo (Ctrl+Z)", "Rückgängig (Strg+Z)"));
            if (_btnToolRedo != null) _toolTip.SetToolTip(_btnToolRedo, Loc.T("Yinele (Ctrl+Y)", "Redo (Ctrl+Y)", "Wiederholen (Strg+Y)"));
            if (_btnToolClear != null) _toolTip.SetToolTip(_btnToolClear, Loc.T("Maskeyi Temizle (Delete)\nAktif grubun maskesini sıfırlar.", "Clear Mask (Delete)\nResets the active group's mask.", "Maske löschen (Entf)\nSetzt die Maske der aktiven Gruppe zurück."));

            if (BtnToolSave != null)
            {
                BtnToolSave.Text = Loc.T("Kaydet", "Save", "Speichern");
                _toolTip.SetToolTip(BtnToolSave, Loc.T(
                    "Kaydet (Ctrl+S)\nSeçili varyantı veya gruptaki tüm varyantları DDS DXT5 (BC3_UNORM) olarak kaydeder.",
                    "Save (Ctrl+S)\nSaves selected variant or all variants as DDS DXT5 (BC3_UNORM).",
                    "Speichern (Strg+S)\nSpeichert die ausgewählte Variante oder alle als DDS DXT5 (BC3_UNORM)."));
            }

            if (BtnToolSaveNewSlot != null)
            {
                BtnToolSaveNewSlot.Text = Loc.T("Yeni Slot", "New Slot", "Neuer Slot");
                _toolTip.SetToolTip(BtnToolSaveNewSlot, Loc.T(
                    "Yeni Slot Olarak Kaydet\nKaynak varyantı kopyalayıp sıradaki boş harfle (örn. d) yeni slot oluşturur.",
                    "Save as New Slot\nCopies source variant with next available letter (e.g. d).",
                    "Als neuen Slot speichern\nKopiert die Quellvariante mit dem nächsten freien Buchstaben (z.B. d)."));
            }

            // Left Panel
            if (_btnOpenFolder != null) _btnOpenFolder.Text = Loc.T(" Klasör Seç (YTD Dosyalarını Tara)", " Select Folder (Scan YTD Files)", " Ordner auswählen (YTD scannen)");
            if (_grpSlots != null) _grpSlots.Text = Loc.T("Slot Aileleri ve Varyantlar (000_a/b/c...)", "Slot Families & Variants (000_a/b/c...)", "Slot-Familien und Varianten (000_a/b/c...)");
            if (_grpTexs != null) _grpTexs.Text = Loc.T("YTD İçindeki Dokular (Textures)", "Textures inside YTD", "Texturen in YTD");
            if (_chkApplyNormSpec != null) _chkApplyNormSpec.Text = Loc.T("Maskeyi _n ve _s dokulara da uygula", "Apply mask to _n and _s textures too", "Maske auch auf _n und _s Texturen anwenden");
            if (_btnImportTex != null) _btnImportTex.Text = Loc.T(" Görsel İçe Aktar (PNG / DDS DXT5)", " Import Image (PNG / DDS DXT5)", " Bild importieren (PNG / DDS DXT5)");

            // Right Panel
            if (_grpFill != null) _grpFill.Text = Loc.T("Doldurma Yöntemi ve Alfa Seçeneği", "Fill Method & Alpha Option", "Füllmethode & Alpha-Option");
            if (_lblMeth != null) _lblMeth.Text = Loc.T("Yöntem:", "Method:", "Methode:");
            if (_lblAlpha != null) _lblAlpha.Text = Loc.T("Alfa Modu:", "Alpha Mode:", "Alpha-Modus:");

            if (_cmbFillMethod != null)
            {
                int fillIdx = _cmbFillMethod.SelectedIndex;
                _cmbFillMethod.Items.Clear();
                _cmbFillMethod.Items.AddRange(new object[]
                {
                    Loc.T("Inpaint - Telea (Varsayılan)", "Inpaint - Telea (Default)", "Inpaint - Telea (Standard)"),
                    Loc.T("Inpaint - Navier-Stokes", "Inpaint - Navier-Stokes", "Inpaint - Navier-Stokes"),
                    Loc.T("Klonlama (Kaynak Noktası Seç)", "Cloning (Pick Source Point)", "Klonen (Quellpunkt wählen)"),
                    Loc.T("Düz Renk (Çevre Ortalaması)", "Solid Color (Surrounding Average)", "Einheitsfarbe (Umgebungsdurchschnitt)"),
                    Loc.T("Donor Kopyala (Temiz Varyant)", "Copy Donor (Clean Variant)", "Donor kopieren (Saubere Variante)"),
                    Loc.T("Şeffaflaştır / Sil (Alfa = 0, Transparan)", "Make Transparent / Delete (Alpha = 0)", "Transparent machen / Löschen (Alpha = 0)")
                });
                _cmbFillMethod.SelectedIndex = (fillIdx >= 0 && fillIdx < _cmbFillMethod.Items.Count) ? fillIdx : 0;
            }

            if (_cmbAlphaMode != null)
            {
                int alphaIdx = _cmbAlphaMode.SelectedIndex;
                _cmbAlphaMode.Items.Clear();
                _cmbAlphaMode.Items.AddRange(new object[]
                {
                    Loc.T("Alfa Korunsun (Orijinal)", "Preserve Alpha (Original)", "Alpha beibehalten (Original)"),
                    Loc.T("Opak Yap (Alfa = 255)", "Make Opaque (Alpha = 255)", "Deckend machen (Alpha = 255)"),
                    Loc.T("Tamamen Şeffaf Yap (Alfa = 0)", "Completely Transparent (Alpha = 0)", "Vollständig transparent (Alpha = 0)")
                });
                _cmbAlphaMode.SelectedIndex = (alphaIdx >= 0 && alphaIdx < _cmbAlphaMode.Items.Count) ? alphaIdx : 0;
            }

            if (_btnPickCloneSrc != null) _btnPickCloneSrc.Text = Loc.T("Tuvalden Klon Kaynak Noktası Seç", "Pick Clone Source Point from Canvas", "Klonquellpunkt auf Leinwand wählen");
            if (_lblDonor != null) _lblDonor.Text = Loc.T("Donor:", "Donor:", "Donor:");

            if (_grpEdge != null) _grpEdge.Text = Loc.T("Maske Kenarı ve Varyant İnce Ayarı", "Mask Edge & Variant Fine-Tuning", "Maskenrand & Varianten-Feinabstimmung");
            if (_lblDil != null) _lblDil.Text = Loc.T("Dilate (0-8px):", "Dilate (0-8px):", "Dilate (0-8px):");
            if (_lblFea != null) _lblFea.Text = Loc.T("Feather (0-8):", "Feather (0-8):", "Feather (0-8):");
            if (_lblVarOff != null) _lblVarOff.Text = Loc.T("Kaydır (X / Y):", "Offset (X / Y):", "Verschieben (X / Y):");
            if (_lblVarSc != null) _lblVarSc.Text = Loc.T("Ölçek (X / Y):", "Scale (X / Y):", "Skalierung (X / Y):");
            if (_btnResetTf != null) _btnResetTf.Text = Loc.T("Sıfırla", "Reset", "Zurücksetzen");

            if (_grpBatch != null) _grpBatch.Text = Loc.T("Toplu İşlem (Seçili Grup ve Klasör Modu)", "Batch Processing (Selected Group & Folder)", "Stapelverarbeitung (Gruppe & Ordner)");
            if (_btnBatchGroup != null) _btnBatchGroup.Text = Loc.T("Seçili Gruba Uygula (A, B, C Tüm Varyantlar)", "Apply to Selected Group (All A, B, C Variants)", "Auf Gruppe anwenden (Alle A, B, C Varianten)");
            if (_btnBatchFolder != null) _btnBatchFolder.Text = Loc.T("Klasördeki Tüm Gruplara Şablon Uygula...", "Apply Template to All Groups in Folder...", "Vorlage auf alle Ordnergruppen anwenden...");

            if (_grpTemplates != null) _grpTemplates.Text = Loc.T("Maske Şablon Yöneticisi (templates/*.json)", "Mask Template Manager (templates/*.json)", "Maskenvorlagen-Manager (templates/*.json)");
            if (_txtNewTemplateName != null) _toolTip.SetToolTip(_txtNewTemplateName, Loc.T("Yeni şablon adı (örn. polis sol kol patch, göğüs rozeti)", "New template name (e.g. police left sleeve patch)", "Neuer Vorlagenname (z.B. Polizei Ärmelpatch)"));
            if (_btnSaveTpl != null) _btnSaveTpl.Text = Loc.T("Şablon Kaydet", "Save Template", "Vorlage speichern");
            if (_lblSearch != null) _lblSearch.Text = Loc.T("Ara:", "Search:", "Suchen:");

            if (_btnTplLoad != null) _btnTplLoad.Text = Loc.T("Yükle", "Load", "Laden");
            if (_btnTplRen != null) _btnTplRen.Text = Loc.T("Adlandır", "Rename", "Umbenennen");
            if (_btnTplDup != null) _btnTplDup.Text = Loc.T("Çoğalt", "Duplicate", "Duplizieren");
            if (_btnTplDel != null) _btnTplDel.Text = Loc.T("Sil", "Delete", "Löschen");
            if (_btnTplImp != null) _btnTplImp.Text = Loc.T("İçe Aktar (.json)", "Import (.json)", "Importieren (.json)");
            if (_btnTplExp != null) _btnTplExp.Text = Loc.T("Dışa Aktar (.json)", "Export (.json)", "Exportieren (.json)");

            // Bottom Panel
            if (_lblProgStatus != null) _lblProgStatus.Text = Loc.T("İşlem Durumu: Beklemede", "Progress Status: Idle", "Status: Bereit");
            if (_btnClearLog != null) _btnClearLog.Text = Loc.T("Logu Temizle", "Clear Log", "Protokoll löschen");
            if (_statusNote != null) _statusNote.Text = Loc.T("Hazır", "Ready", "Bereit");

            if (_canvas != null)
                _canvas.Invalidate();
        }

        // ------------------------------------------------------------------
        // Settings & Self-Check ("Kendi Kendini Kontrol Et")
        // ------------------------------------------------------------------
        private void OnOpenSettings()
        {
            using (var dlg = new SettingsForm(_config))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    AppendLog("Ayarlar güncellendi.");
                }
            }
        }

        private void OnRunSelfCheck()
        {
            AppendLog("Kendi Kendini Kontrol Et (Self-Check) başlatıldı...");
            bool ok = Verifier.RunSelfDiagnostics(_config, _ddsCodec, _ytdService, out List<string> lines);
            foreach (string l in lines)
                AppendLog(l);

            string head = ok
                ? "Tüm Çekirdek Kontroller (Maske, Doldurma, Alfa ve 4x4 DXT5 Blok Koruma) Başarıyla Geçti!"
                : "Bazı Kontrollerde Uyarı veya Hata Var!";

            using (var rep = new ReportForm("Kendi Kendini Kontrol Et - Sistem Doğrulama Raporu", head, lines))
            {
                rep.ShowDialog(this);
            }
        }
    }
}
