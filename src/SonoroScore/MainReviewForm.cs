using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using SonoroScore.Scanner;

namespace SonoroScore;

public partial class MainReviewForm : Form
{
    private readonly List<EchoReviewItem> _allItems = [];
    private readonly List<EchoReviewItem> _filteredItems = [];
    private int _currentIndex = -1;
    private bool _focusPanelCrop = true;
    private bool _isLoading = false;
    private EchoCatalogEntry[] _catalog = [];

    // UI Colors (Modern Dark Theme)
    private static readonly Color BgDark      = Color.FromArgb(24, 25, 32);
    private static readonly Color BgCard      = Color.FromArgb(34, 36, 48);
    private static readonly Color BgInput     = Color.FromArgb(44, 46, 62);
    private static readonly Color FgPrimary   = Color.FromArgb(240, 242, 245);
    private static readonly Color FgSecondary = Color.FromArgb(160, 165, 185);
    private static readonly Color AccentPurple= Color.FromArgb(130, 100, 250);
    private static readonly Color AccentGreen = Color.FromArgb(46, 204, 113);
    private static readonly Color AccentAmber = Color.FromArgb(241, 196, 15);
    private static readonly Color AccentRed   = Color.FromArgb(231, 76, 60);

    // Controls
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripLabel _sessionInfoLabel = null!;
    private SplitContainer _mainSplit = null!;
    private SplitContainer _contentSplit = null!;
    private ListView _echoListView = null!;
    private TextBox _searchBox = null!;
    private ComboBox _filterCombo = null!;
    private PictureBox _screenshotBox = null!;
    private Label _cropModeLabel = null!;
    private Button _toggleCropBtn = null!;

    // Editor Controls
    private ComboBox _nameCombo = null!;
    private ComboBox _costCombo = null!;
    private ComboBox _rarityCombo = null!;
    private NumericUpDown _levelNumeric = null!;
    private ComboBox _sonataCombo = null!;
    private ComboBox _mainStatKeyCombo = null!;
    private TextBox _mainStatValueBox = null!;
    private Label _nameConfLabel = null!;

    // Substats Controls (5 rows)
    private readonly CheckBox[] _subActiveChecks = new CheckBox[5];
    private readonly ComboBox[] _subKeyCombos = new ComboBox[5];
    private readonly TextBox[] _subValueBoxes = new TextBox[5];
    private readonly Label[] _subGradeLabels = new Label[5];
    private readonly Button[] _subSnapButtons = new Button[5];

    // Evidence & Navigation
    private TextBox _rawEvidenceBox = null!;
    private Label _indexLabel = null!;
    private Button _prevBtn = null!;
    private Button _nextBtn = null!;
    private Button _verifyBtn = null!;

    private string? _currentSessionPath;

    public MainReviewForm()
    {
        InitializeComponents();
        Load += async (s, e) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _statusLabel.Text = "Loading game database (nanoka.cc)...";
            _catalog = await GameDatabase.LoadAsync();
            PopulateDropdowns();
            _statusLabel.Text = $"Database loaded ({_catalog.Length} echoes). Searching for scan sessions...";

            // Auto-discover the latest scan session in publish/AlephalSonata/aleph_images
            string defaultDir = @"C:\Users\Tina_\Documents\PlayingWithRepo\Sonoro-Score\publish\AlephalSonata\aleph_images";
            if (Directory.Exists(defaultDir))
            {
                var sessions = Directory.GetDirectories(defaultDir).OrderByDescending(d => d).ToArray();
                if (sessions.Length > 0)
                {
                    // Check if JSON exists in latest session
                    var jsonFiles = Directory.GetFiles(sessions[0], "test_scan_results_*.json")
                        .Concat(Directory.GetFiles(sessions[0], "scan_results_*.json"))
                        .OrderByDescending(f => f)
                        .ToArray();

                    if (jsonFiles.Length > 0)
                    {
                        await LoadJsonSessionAsync(jsonFiles[0]);
                    }
                    else
                    {
                        await LoadFolderSessionAsync(sessions[0]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Initialization note: {ex.Message}";
        }
    }

    private void PopulateDropdowns()
    {
        // Echo names
        _nameCombo.Items.Clear();
        foreach (var e in _catalog.OrderBy(e => e.Name))
            _nameCombo.Items.Add(e.Name);

        // Stat display names (user friendly)
        string[] statDisplays = StatDisplayNames.AllDisplayNames;
        _mainStatKeyCombo.Items.Clear();
        _mainStatKeyCombo.Items.AddRange(statDisplays);

        for (int i = 0; i < 5; i++)
        {
            _subKeyCombos[i].Items.Clear();
            _subKeyCombos[i].Items.AddRange(statDisplays);
        }

        // Sonatas (all 34 official sonatas from GameDatabase)
        _sonataCombo.Items.Clear();
        _sonataCombo.Items.AddRange(GameDatabase.KnownSonatas);
    }

    private void InitializeComponents()
    {
        Text = "SonoroScore — Echo Review & Verification Studio (SS)";
        Size = new Size(1440, 880);
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgDark;
        ForeColor = FgPrimary;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        // 1. Top ToolStrip
        var toolStrip = new ToolStrip
        {
            BackColor = BgCard,
            ForeColor = FgPrimary,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(8, 4, 8, 4),
            Renderer = new DarkToolStripRenderer()
        };

        var openFolderBtn = new ToolStripButton("📁 Open Folder", null, async (s, e) => await OpenFolderDialogAsync()) { ForeColor = FgPrimary };
        var openJsonBtn   = new ToolStripButton("📄 Load Scan JSON", null, async (s, e) => await OpenJsonDialogAsync()) { ForeColor = FgPrimary };
        var saveJsonBtn   = new ToolStripButton("💾 Save Verified JSON", null, async (s, e) => await SaveVerifiedJsonAsync()) { ForeColor = AccentGreen, Font = new Font(Font, FontStyle.Bold) };
        var rescanBtn     = new ToolStripButton("⚡ Re-Scan Current", null, async (s, e) => await RescanCurrentAsync()) { ForeColor = AccentAmber };
        var tacetBtn      = new ToolStripButton("⬆ Tacet-Lab", null, async (s, e) => await ExportTacetLabAsync()) { ForeColor = FgPrimary, ToolTipText = "1-click export verified echoes to tacet-lab-backup.json" };
        var goodBtn       = new ToolStripButton("⬆ GOOD", null, async (s, e) => await ExportGoodAsync()) { ForeColor = FgPrimary, ToolTipText = "1-click export verified echoes to GOOD format (best-effort bridge)" };
        var areasBtn      = new ToolStripButton("◈ Areas", null, (s, e) => OpenRegionConfig()) { ForeColor = FgPrimary, ToolTipText = "Visual scan-area config: drag region boxes over the current echo panel" };

        _sessionInfoLabel = new ToolStripLabel("No session loaded") { ForeColor = FgSecondary, Alignment = ToolStripItemAlignment.Right };

        toolStrip.Items.AddRange([openFolderBtn, openJsonBtn, new ToolStripSeparator(), saveJsonBtn, new ToolStripSeparator(), rescanBtn, new ToolStripSeparator(), tacetBtn, goodBtn, new ToolStripSeparator(), areasBtn, _sessionInfoLabel]);

        // 2. Status Bar
        var statusStrip = new StatusStrip { BackColor = BgCard, ForeColor = FgSecondary };
        _statusLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusStrip.Items.Add(_statusLabel);

        // 3. Main Split: Left (List 310px) | Right Split (Center Image & Right Editor 430px)
        // NOTE: SplitterDistance is (re)applied in OnLoad once layout exists —
        // assigning it here while containers are still default-sized squeezes panels.
        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 6,
            BackColor = BgDark,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 200,
            Panel2MinSize = 500
        };

        // Left Panel: Search, Filter, ListView
        var leftPanel = CreateLeftPanel();
        _mainSplit.Panel1.Controls.Add(leftPanel);

        // Right Split: Center (Screenshot) | Right (Editor 430px)
        _contentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 6,
            BackColor = BgDark,
            FixedPanel = FixedPanel.Panel2,
            Panel1MinSize = 300,
            Panel2MinSize = 320
        };

        var centerPanel = CreateCenterImagePanel();
        var rightPanel  = CreateRightEditorPanel();

        _contentSplit.Panel1.Controls.Add(centerPanel);
        _contentSplit.Panel2.Controls.Add(rightPanel);
        _mainSplit.Panel2.Controls.Add(_contentSplit);

        Controls.Add(_mainSplit);
        Controls.Add(toolStrip);
        Controls.Add(statusStrip);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Restore previous size + sections, else the 310 / center / 430 defaults.
        // Distances are set here (post-layout), never while containers are unlaid-out.
        try
        {
            var saved = GuiLayout.Load();
            var area = Screen.FromControl(this).WorkingArea;
            if (saved.TryGetValue("mainWidth", out int w) && saved.TryGetValue("mainHeight", out int h))
            {
                w = Math.Clamp(w, MinimumSize.Width, Math.Max(MinimumSize.Width, area.Width));
                h = Math.Clamp(h, MinimumSize.Height, Math.Max(MinimumSize.Height, area.Height));
                Size = new Size(w, h);
                PerformLayout();
            }

            int mainDist = saved.TryGetValue("mainSplit", out int sm) ? sm : 310;
            _mainSplit.SplitterDistance = Math.Clamp(mainDist, _mainSplit.Panel1MinSize,
                Math.Max(_mainSplit.Panel1MinSize, _mainSplit.Width - _mainSplit.Panel2MinSize - _mainSplit.SplitterWidth));

            int contentDist;
            if (saved.TryGetValue("contentSplit", out int sc))
            {
                contentDist = sc;
            }
            else
            {
                contentDist = _contentSplit.Width - 430 - _contentSplit.SplitterWidth; // right editor 430px
            }
            _contentSplit.SplitterDistance = Math.Clamp(contentDist, _contentSplit.Panel1MinSize,
                Math.Max(_contentSplit.Panel1MinSize, _contentSplit.Width - _contentSplit.Panel2MinSize - _contentSplit.SplitterWidth));
        }
        catch { /* keep designer defaults */ }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            GuiLayout.Save(new Dictionary<string, int>
            {
                ["mainWidth"] = Width,
                ["mainHeight"] = Height,
                ["mainSplit"] = _mainSplit.SplitterDistance,
                ["contentSplit"] = _contentSplit.SplitterDistance
            });
        }
        catch { /* layout save is best-effort */ }
        base.OnFormClosing(e);
    }

    private Control CreateLeftPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = BgCard, Padding = new Padding(8) };

        var titleLabel = new Label
        {
            Text = "CAPTURED ECHOES",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = AccentPurple
        };

        // Search & Filter Bar
        var filterPanel = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = BgCard };
        _searchBox = new TextBox
        {
            PlaceholderText = "Search echo or file...",
            Dock = DockStyle.Top,
            BackColor = BgInput,
            ForeColor = FgPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Height = 26
        };
        _searchBox.TextChanged += (s, e) => ApplyFilter();

        _filterCombo = new ComboBox
        {
            Dock = DockStyle.Bottom,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = BgInput,
            ForeColor = FgPrimary,
            FlatStyle = FlatStyle.Flat,
            Height = 26
        };
        _filterCombo.Items.AddRange(["All Echoes", "Complete Only", "Needs Review", "Verified / Edited"]);
        _filterCombo.SelectedIndex = 0;
        _filterCombo.SelectedIndexChanged += (s, e) => ApplyFilter();

        filterPanel.Controls.Add(_searchBox);
        filterPanel.Controls.Add(_filterCombo);

        _echoListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = BgDark,
            ForeColor = FgPrimary,
            BorderStyle = BorderStyle.None
        };
        _echoListView.Columns.Add("Status", 75);
        _echoListView.Columns.Add("Echo Name", 125);
        _echoListView.Columns.Add("Lv", 36);
        _echoListView.Columns.Add("Main Stat", 65);

        _echoListView.SelectedIndexChanged += (s, e) =>
        {
            if (_echoListView.SelectedIndices.Count > 0)
            {
                int filterIdx = _echoListView.SelectedIndices[0];
                if (filterIdx >= 0 && filterIdx < _filteredItems.Count)
                {
                    LoadItem(_filteredItems[filterIdx]);
                }
            }
        };

        panel.Controls.Add(_echoListView);
        panel.Controls.Add(filterPanel);
        panel.Controls.Add(titleLabel);

        return panel;
    }

    private Control CreateCenterImagePanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Padding = new Padding(8) };

        var topBar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = BgCard, Padding = new Padding(6, 4, 6, 4) };
        _cropModeLabel = new Label
        {
            Text = "Viewing: Echo Stats Panel Crop (Enlarged)",
            Dock = DockStyle.Left,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = AccentPurple,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _toggleCropBtn = new Button
        {
            Text = "🖼 Toggle Full Frame",
            Dock = DockStyle.Right,
            Width = 160,
            BackColor = BgInput,
            ForeColor = FgPrimary,
            FlatStyle = FlatStyle.Flat
        };
        _toggleCropBtn.FlatAppearance.BorderSize = 0;
        _toggleCropBtn.Click += (s, e) =>
        {
            _focusPanelCrop = !_focusPanelCrop;
            _cropModeLabel.Text = _focusPanelCrop
                ? "Viewing: Echo Stats Panel Crop (Enlarged)"
                : "Viewing: Full Window Screenshot (1920x1080)";
            _toggleCropBtn.Text = _focusPanelCrop ? "🖼 Toggle Full Frame" : "🔍 Toggle Panel Crop";
            RefreshCurrentImage();
        };

        topBar.Controls.Add(_cropModeLabel);
        topBar.Controls.Add(_toggleCropBtn);

        _screenshotBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            Cursor = Cursors.Hand
        };
        _screenshotBox.Click += (s, e) => _toggleCropBtn.PerformClick();

        panel.Controls.Add(_screenshotBox);
        panel.Controls.Add(topBar);

        return panel;
    }

    private Control CreateRightEditorPanel()
    {
        var scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BgCard, Padding = new Padding(12) };

        // Header Title
        var headerLabel = new Label
        {
            Text = "ECHO DATA REVIEW & EDIT",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = AccentPurple
        };

        _nameConfLabel = new Label
        {
            Text = "Confidence: —",
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = FgSecondary
        };

        // Group 1: Identity Card
        var idGroup = CreateCardGroup("Echo Identity");
        idGroup.Dock = DockStyle.Top;
        idGroup.Height = 175;

        AddLabeledControl(idGroup, "Name:", _nameCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems }, 0);
        _nameCombo.SelectedIndexChanged += (s, e) => OnFieldChanged();

        AddLabeledControl(idGroup, "Cost:", _costCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 1);
        _costCombo.Items.AddRange(["1", "3", "4"]);
        _costCombo.SelectedIndexChanged += (s, e) => OnFieldChanged();

        AddLabeledControl(idGroup, "Rarity:", _rarityCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 2);
        _rarityCombo.Items.AddRange(["5 ★ (Gold)", "4 ★ (Purple)", "3 ★ (Blue)", "2 ★ (Green)"]);
        _rarityCombo.SelectedIndexChanged += (s, e) => OnFieldChanged();

        AddLabeledControl(idGroup, "Level:", _levelNumeric = new NumericUpDown { Minimum = 0, Maximum = 25, Value = 25 }, 3);
        _levelNumeric.ValueChanged += (s, e) => OnFieldChanged();

        AddLabeledControl(idGroup, "Sonata:", _sonataCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 4);
        _sonataCombo.SelectedIndexChanged += (s, e) => OnFieldChanged();

        // Group 2: Main Stat Card
        var mainGroup = CreateCardGroup("Main Stat");
        mainGroup.Dock = DockStyle.Top;
        mainGroup.Height = 95;

        AddLabeledControl(mainGroup, "Stat:", _mainStatKeyCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 0);
        _mainStatKeyCombo.SelectedIndexChanged += (s, e) => OnFieldChanged();

        AddLabeledControl(mainGroup, "Value:", _mainStatValueBox = new TextBox(), 1);
        _mainStatValueBox.TextChanged += (s, e) => OnFieldChanged();

        // Group 3: Substats Card (5 rows in strict top-to-bottom #1 to #5 order)
        var subGroup = CreateCardGroup("Substats (Tuned Rolls)");
        subGroup.Dock = DockStyle.Top;
        subGroup.Height = 225;

        for (int i = 0; i < 5; i++)
        {
            int rowIdx = i;
            int yPos = 24 + i * 38;
            var rowPanel = new Panel
            {
                Location = new Point(8, yPos),
                Size = new Size(395, 34),
                BackColor = Color.Transparent
            };

            _subActiveChecks[i] = new CheckBox { Text = $"#{i + 1}", Location = new Point(0, 4), Size = new Size(46, 24), Checked = true, ForeColor = FgPrimary };
            _subActiveChecks[i].CheckedChanged += (s, e) => OnFieldChanged();

            _subKeyCombos[i] = new ComboBox { Location = new Point(48, 3), Size = new Size(160, 24), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = BgInput, ForeColor = FgPrimary, FlatStyle = FlatStyle.Flat };
            _subKeyCombos[i].SelectedIndexChanged += (s, e) => OnFieldChanged();

            _subValueBoxes[i] = new TextBox { Location = new Point(212, 3), Size = new Size(58, 24), BackColor = BgInput, ForeColor = FgPrimary, BorderStyle = BorderStyle.FixedSingle };
            _subValueBoxes[i].TextChanged += (s, e) =>
            {
                UpdateSubstatGrade(rowIdx);
                OnFieldChanged();
            };

            _subSnapButtons[i] = new Button { Text = "⟳", Location = new Point(274, 3), Size = new Size(26, 24), BackColor = BgInput, ForeColor = AccentAmber, FlatStyle = FlatStyle.Flat };
            _subSnapButtons[i].FlatAppearance.BorderSize = 0;
            _subSnapButtons[i].Click += (s, e) => SnapSubstat(rowIdx);

            _subGradeLabels[i] = new Label { Location = new Point(304, 4), Size = new Size(88, 24), ForeColor = AccentGreen, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Italic) };

            rowPanel.Controls.Add(_subActiveChecks[i]);
            rowPanel.Controls.Add(_subKeyCombos[i]);
            rowPanel.Controls.Add(_subValueBoxes[i]);
            rowPanel.Controls.Add(_subSnapButtons[i]);
            rowPanel.Controls.Add(_subGradeLabels[i]);

            subGroup.Controls.Add(rowPanel);
        }

        // Group 4: OCR Raw Evidence (Collapsible / Preview)
        var evGroup = CreateCardGroup("OCR Raw Evidence (Validation)");
        evGroup.Dock = DockStyle.Top;
        evGroup.Height = 110;
        _rawEvidenceBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BackColor = BgInput,
            ForeColor = FgSecondary,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical
        };
        evGroup.Controls.Add(_rawEvidenceBox);

        // Bottom Nav Bar
        var navBar = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = BgCard, Padding = new Padding(0, 8, 0, 0) };
        _prevBtn = new Button { Text = "◀ Prev (A)", Width = 105, Dock = DockStyle.Left, BackColor = BgInput, ForeColor = FgPrimary, FlatStyle = FlatStyle.Flat };
        _prevBtn.FlatAppearance.BorderSize = 0;
        _prevBtn.Click += (s, e) => NavigatePrev();

        _nextBtn = new Button { Text = "Next (D) ▶", Width = 105, Dock = DockStyle.Right, BackColor = BgInput, ForeColor = FgPrimary, FlatStyle = FlatStyle.Flat };
        _nextBtn.FlatAppearance.BorderSize = 0;
        _nextBtn.Click += (s, e) => NavigateNext();

        _verifyBtn = new Button { Text = "✓ Mark Verified (Space)", Dock = DockStyle.Fill, BackColor = AccentPurple, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font(Font, FontStyle.Bold) };
        _verifyBtn.FlatAppearance.BorderSize = 0;
        _verifyBtn.Click += (s, e) => MarkCurrentVerified();

        _indexLabel = new Label { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleCenter, ForeColor = FgSecondary };

        navBar.Controls.Add(_verifyBtn);
        navBar.Controls.Add(_prevBtn);
        navBar.Controls.Add(_nextBtn);

        // Assemble right editor in reverse dock order
        scrollPanel.Controls.Add(navBar);
        scrollPanel.Controls.Add(_indexLabel);
        scrollPanel.Controls.Add(evGroup);
        scrollPanel.Controls.Add(subGroup);
        scrollPanel.Controls.Add(mainGroup);
        scrollPanel.Controls.Add(idGroup);
        scrollPanel.Controls.Add(_nameConfLabel);
        scrollPanel.Controls.Add(headerLabel);

        return scrollPanel;
    }

    private GroupBox CreateCardGroup(string text)
    {
        var gb = new GroupBox
        {
            Text = text,
            BackColor = BgCard,
            ForeColor = AccentPurple,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Padding = new Padding(8)
        };
        return gb;
    }

    private void AddLabeledControl(Control parent, string labelText, Control ctrl, int row)
    {
        int y = 24 + row * 28;
        var lbl = new Label
        {
            Text = labelText,
            Location = new Point(8, y + 2),
            Size = new Size(60, 22),
            ForeColor = FgSecondary,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular)
        };
        ctrl.Location = new Point(72, y);
        ctrl.Size = new Size(parent.Width - 85, 24);
        ctrl.BackColor = BgInput;
        ctrl.ForeColor = FgPrimary;
        if (ctrl is ComboBox cb) cb.FlatStyle = FlatStyle.Flat;
        if (ctrl is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle;

        parent.Controls.Add(lbl);
        parent.Controls.Add(ctrl);
    }

    // ── Session & File Loading ────────────────────────────────────────────────

    private async Task OpenFolderDialogAsync()
    {
        using var fbd = new FolderBrowserDialog();
        if (fbd.ShowDialog() == DialogResult.OK)
        {
            await LoadFolderSessionAsync(fbd.SelectedPath);
        }
    }

    private async Task OpenJsonDialogAsync()
    {
        using var ofd = new OpenFileDialog { Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*" };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            await LoadJsonSessionAsync(ofd.FileName);
        }
    }

    /// <summary>
    /// Scan-result files produced by the CLI / debugger (NOT verified_echoes_*.json,
    /// which uses the review-studio schema and cannot be re-loaded as a scan session).
    /// </summary>
    private static readonly string[] ScanJsonPatterns = ["test_scan_results_*.json", "scan_results_*.json"];

    private static string? FindLatestScanJson(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return null;
        return ScanJsonPatterns
            .SelectMany(p => Directory.GetFiles(folderPath, p))
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }

    private async Task LoadJsonSessionAsync(string jsonPath)
    {
        try
        {
            _statusLabel.Text = $"Loading {Path.GetFileName(jsonPath)}...";
            string json = await File.ReadAllTextAsync(jsonPath);
            var session = JsonSerializer.Deserialize<ScanSessionResult>(json);

            if (session?.Results == null || session.Results.Count == 0)
            {
                MessageBox.Show("No scan results found in this JSON file.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // ── Resolve the image folder behind this scan ("spawn its images") ──
            // Prefer the recorded session path, then the JSON's own folder.
            string? imageFolder = null;
            if (!string.IsNullOrWhiteSpace(session.SessionPath) && Directory.Exists(session.SessionPath))
                imageFolder = session.SessionPath;
            string? jsonFolder = Path.GetDirectoryName(jsonPath);
            if (imageFolder == null && !string.IsNullOrEmpty(jsonFolder) && Directory.Exists(jsonFolder))
                imageFolder = jsonFolder;

            int missing = session.Results.Count(r => !File.Exists(r.ImagePath));
            if (missing > 0)
            {
                // Stored absolute paths are stale (folder moved?) — ask for the folder once,
                // then relink every result by file name.
                var ask = MessageBox.Show(
                    $"{missing}/{session.Results.Count} scan images not found at their recorded paths.\n\n" +
                    "Locate the image folder for this scan so results stay paired with their screenshots?",
                    "Images Missing", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ask == DialogResult.Yes)
                {
                    using var fbd = new FolderBrowserDialog { SelectedPath = imageFolder ?? jsonFolder ?? "" };
                    if (fbd.ShowDialog() == DialogResult.OK)
                        imageFolder = fbd.SelectedPath;
                }
            }

            _currentSessionPath = imageFolder ?? session.SessionPath;
            _allItems.Clear();
            int relinked = 0, stillMissing = 0;
            foreach (var r in session.Results)
            {
                var record = r;
                if (!File.Exists(record.ImagePath) && imageFolder != null)
                {
                    string candidate = Path.Combine(imageFolder, Path.GetFileName(record.ImagePath));
                    if (File.Exists(candidate))
                    {
                        record = record with { ImagePath = candidate };
                        relinked++;
                    }
                    else stillMissing++;
                }
                else if (!File.Exists(record.ImagePath)) stillMissing++;
                _allItems.Add(EchoReviewItem.FromScanResult(record));
            }

            _sessionInfoLabel.Text = $"{Path.GetFileName(_currentSessionPath)} | {_allItems.Count} Echoes ({Path.GetFileName(jsonPath)})";
            _statusLabel.Text = $"Loaded {_allItems.Count} echoes from {Path.GetFileName(jsonPath)}" +
                                (relinked > 0 ? $" ({relinked} images relinked)" : "") +
                                (stillMissing > 0 ? $" — {stillMissing} images still missing" : "") + ".";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load JSON: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadFolderSessionAsync(string folderPath)
    {
        // Opening a folder loads the images AND the latest scan with them —
        // results stay paired with their screenshots instead of PENDING_SCAN stubs.
        string? scanJson = FindLatestScanJson(folderPath);
        if (scanJson != null)
        {
            await LoadJsonSessionAsync(scanJson);
            return;
        }

        _currentSessionPath = folderPath;
        var pngs = Directory.GetFiles(folderPath, "echo_*.png").OrderBy(f => f).ToArray();
        if (pngs.Length == 0)
        {
            MessageBox.Show($"No 'echo_*.png' images found in {folderPath}.", "No Images", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _allItems.Clear();
        foreach (var p in pngs)
        {
            _allItems.Add(new EchoReviewItem
            {
                ImagePath = p,
                EchoName = "PENDING_SCAN",
                Substats = Enumerable.Range(0, 5).Select(_ => new EditableSubstat { IsActive = false, StatKey = "Unknown" }).ToList()
            });
        }

        _sessionInfoLabel.Text = $"{Path.GetFileName(folderPath)} | {_allItems.Count} Images (Pending Scan — no scan JSON found)";
        ApplyFilter();
    }

    private async Task SaveVerifiedJsonAsync()
    {
        if (_allItems.Count == 0) return;

        string targetDir = _currentSessionPath ?? AppDomain.CurrentDomain.BaseDirectory;
        string outPath = Path.Combine(targetDir, $"verified_echoes_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        var payload = new
        {
            ExportedAt = DateTime.UtcNow,
            TotalEchoes = _allItems.Count,
            VerifiedCount = _allItems.Count(i => i.IsVerified),
            EditedCount = _allItems.Count(i => i.IsEdited),
            Echoes = _allItems.Select(i => new
            {
                i.ImageFileName,
                i.EchoName,
                i.Cost,
                i.Rarity,
                i.Level,
                i.Sonata,
                MainStat = new { Key = i.MainStatKey, Display = StatDisplayNames.ToDisplay(i.MainStatKey), Value = i.MainStatValue },
                Substats = i.Substats.Where(s => s.IsActive).Select(s => new { Key = s.StatKey, Display = StatDisplayNames.ToDisplay(s.StatKey), Value = s.Value, s.SnappedValue }).ToList(),
                i.IsVerified,
                i.IsEdited
            })
        };

        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        _statusLabel.Text = $"Saved {payload.VerifiedCount} verified echoes to {Path.GetFileName(outPath)}";
        MessageBox.Show($"Verified echoes exported successfully!\n\nFile saved to:\n{outPath}", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private List<ExportableEcho> CurrentExportModels()
    {
        var models = new List<ExportableEcho>();
        foreach (var item in _allItems)
        {
            if (string.IsNullOrWhiteSpace(item.MainStatKey) ||
                item.MainStatKey.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                continue;
            var subs = item.Substats
                .Where(s => s.IsActive && !string.IsNullOrWhiteSpace(s.StatKey) &&
                            !s.StatKey.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                .Select(s => (s.StatKey, s.Value))
                .ToList();
            models.Add(new ExportableEcho(
                item.EchoName, item.Cost, item.Rarity, item.Level,
                item.Sonata ?? "", item.EquippedBy ?? "",
                item.MainStatKey, item.MainStatValue, subs));
        }
        return models;
    }

    private async Task ExportTacetLabAsync()
    {
        if (_allItems.Count == 0)
        {
            MessageBox.Show("No echoes loaded.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var sfd = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"tacet-lab-backup_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            InitialDirectory = _currentSessionPath ?? AppDomain.CurrentDomain.BaseDirectory
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;
        try
        {
            var models = CurrentExportModels();
            string json = TacetLabExporter.Export(models, out int skipped);
            await File.WriteAllTextAsync(sfd.FileName, json);
            _statusLabel.Text = $"Tacet-Lab backup: {models.Count} echoes ({skipped} skipped).";
            MessageBox.Show($"Tacet-Lab backup exported!\n\n{models.Count} echoes → {sfd.FileName}\n" +
                            $"({skipped} skipped without main stat)\n\nImport in Tacet-Lab via top-bar Export/Restore.",
                            "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ExportGoodAsync()
    {
        if (_allItems.Count == 0)
        {
            MessageBox.Show("No echoes loaded.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using var sfd = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"sonoro-good_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            InitialDirectory = _currentSessionPath ?? AppDomain.CurrentDomain.BaseDirectory
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;
        try
        {
            var models = CurrentExportModels();
            string json = GoodExporter.Export(models, out int skipped);
            await File.WriteAllTextAsync(sfd.FileName, json);
            _statusLabel.Text = $"GOOD export: {models.Count} echoes ({skipped} skipped).";
            MessageBox.Show($"GOOD file exported (best-effort WuWa→GOOD bridge)!\n\n{models.Count} echoes → {sfd.FileName}\n" +
                            "Prefer tacet-lab-backup.json for lossless Tacet-Lab import.",
                            "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Item Binding & Editing ────────────────────────────────────────────────

    private void ApplyFilter()
    {
        string query = _searchBox.Text.Trim().ToLowerInvariant();
        int filterMode = _filterCombo.SelectedIndex;

        _filteredItems.Clear();
        _echoListView.Items.Clear();

        foreach (var item in _allItems)
        {
            bool matchesQuery = string.IsNullOrEmpty(query)
                                || item.EchoName.ToLowerInvariant().Contains(query)
                                || item.ImageFileName.ToLowerInvariant().Contains(query);

            if (!matchesQuery) continue;

            bool matchesFilter = filterMode switch
            {
                1 => item.IsComplete,
                2 => !item.IsComplete,
                3 => item.IsVerified || item.IsEdited,
                _ => true
            };

            if (matchesFilter)
            {
                _filteredItems.Add(item);
                var lvi = new ListViewItem(item.DisplayStatus);
                lvi.SubItems.Add(item.EchoName);
                lvi.SubItems.Add($"+{item.Level}");
                lvi.SubItems.Add(StatDisplayNames.ToDisplay(item.MainStatKey));
                lvi.Tag = item;

                lvi.ForeColor = item.IsVerified ? AccentGreen
                    : item.IsEdited ? AccentAmber
                    : item.Errors.Count > 0 ? AccentRed
                    : item.IsComplete ? FgPrimary : FgSecondary;

                _echoListView.Items.Add(lvi);
            }
        }

        if (_filteredItems.Count > 0)
        {
            _echoListView.Items[0].Selected = true;
        }
        else
        {
            ClearEditor();
        }
    }

    private void LoadItem(EchoReviewItem item)
    {
        _isLoading = true;
        try
        {
            _currentIndex = _filteredItems.IndexOf(item);
            _indexLabel.Text = $"Echo {_currentIndex + 1} of {_filteredItems.Count}";

            // Bind Identity
            _nameCombo.Text = item.EchoName;
            _costCombo.Text = item.Cost.ToString();
            _rarityCombo.SelectedIndex = Math.Clamp(5 - item.Rarity, 0, 3);
            _levelNumeric.Value = Math.Clamp(item.Level, 0, 25);
            _sonataCombo.Text = item.Sonata;

            _nameConfLabel.Text = $"Confidence: {item.NameConfidence:P1} | File: {item.ImageFileName}";

            // Bind Main Stat (convert enum key like "CritRate" to display like "Crit. Rate")
            _mainStatKeyCombo.Text = StatDisplayNames.ToDisplay(item.MainStatKey);
            _mainStatValueBox.Text = item.MainStatValue > 0 ? item.MainStatValue.ToString("0.0#", CultureInfo.InvariantCulture) : "";

            // Bind Substats
            for (int i = 0; i < 5; i++)
            {
                var sub = i < item.Substats.Count ? item.Substats[i] : null;
                if (sub != null && sub.IsActive)
                {
                    _subActiveChecks[i].Checked = true;
                    _subKeyCombos[i].Text = StatDisplayNames.ToDisplay(sub.StatKey);
                    _subValueBoxes[i].Text = sub.Value > 0 ? sub.Value.ToString("0.0#", CultureInfo.InvariantCulture) : "";
                    UpdateSubstatGrade(i);
                }
                else
                {
                    _subActiveChecks[i].Checked = false;
                    _subKeyCombos[i].SelectedIndex = -1;
                    _subValueBoxes[i].Text = "";
                    _subGradeLabels[i].Text = "";
                }
            }

            // Evidence
            _rawEvidenceBox.Text = $"[Name OCR]: {item.RawNameOcr}\r\n[Main OCR]: {item.RawMainStatOcr}\r\n[Substats OCR]:\r\n{item.RawSubstatsOcr}";
            if (item.Warnings.Count > 0)
                _rawEvidenceBox.Text += $"\r\n[Warnings]: {string.Join(", ", item.Warnings)}";

            // Display Image
            RefreshCurrentImage();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void RefreshCurrentImage()
    {
        if (_currentIndex < 0 || _currentIndex >= _filteredItems.Count) return;
        var item = _filteredItems[_currentIndex];

        if (!File.Exists(item.ImagePath)) return;

        try
        {
            using var full = new Bitmap(item.ImagePath);
            if (_focusPanelCrop)
            {
                // Crop right stats panel
                using var panel = EchoRegions.ExtractPanel(full);
                _screenshotBox.Image?.Dispose();
                _screenshotBox.Image = new Bitmap(panel);
            }
            else
            {
                _screenshotBox.Image?.Dispose();
                _screenshotBox.Image = new Bitmap(full);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error rendering image: {ex.Message}";
        }
    }

    private void OnFieldChanged()
    {
        if (_isLoading) return;
        if (_currentIndex < 0 || _currentIndex >= _filteredItems.Count) return;
        var item = _filteredItems[_currentIndex];

        item.IsEdited = true;
        item.EchoName = _nameCombo.Text.Trim();
        if (int.TryParse(_costCombo.Text, out int c)) item.Cost = c;
        item.Rarity = 5 - _rarityCombo.SelectedIndex;
        item.Level = (int)_levelNumeric.Value;
        item.Sonata = _sonataCombo.Text;
        item.MainStatKey = StatDisplayNames.FromDisplay(_mainStatKeyCombo.Text);
        if (float.TryParse(_mainStatValueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float mv))
            item.MainStatValue = mv;

        for (int i = 0; i < 5; i++)
        {
            if (i < item.Substats.Count)
            {
                var sub = item.Substats[i];
                sub.IsActive = _subActiveChecks[i].Checked;
                sub.StatKey = StatDisplayNames.FromDisplay(_subKeyCombos[i].Text);
                if (float.TryParse(_subValueBoxes[i].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float sv))
                    sub.Value = sv;
            }
        }

        // Update list view status
        if (_echoListView.SelectedIndices.Count > 0)
        {
            var lvi = _echoListView.SelectedItems[0];
            lvi.Text = item.DisplayStatus;
            lvi.SubItems[1].Text = item.EchoName;
            lvi.SubItems[3].Text = StatDisplayNames.ToDisplay(item.MainStatKey);
            lvi.ForeColor = item.IsVerified ? AccentGreen : AccentAmber;
        }
    }

    private void UpdateSubstatGrade(int row)
    {
        if (!_subActiveChecks[row].Checked)
        {
            _subGradeLabels[row].Text = "";
            return;
        }

        string rawKey = StatDisplayNames.FromDisplay(_subKeyCombos[row].Text);
        if (Enum.TryParse<StatKey>(rawKey, out var key) &&
            float.TryParse(_subValueBoxes[row].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
        {
            var (snapped, conf) = TunableRolls.Resolve(key, val);
            if (snapped.HasValue)
            {
                bool isExact = MathF.Abs(snapped.Value - val) < 0.05f;
                _subGradeLabels[row].Text = isExact ? "✓ Valid Roll" : $"≈ Snap: {snapped.Value}";
                _subGradeLabels[row].ForeColor = isExact ? AccentGreen : AccentAmber;
            }
            else
            {
                _subGradeLabels[row].Text = "⚠ Non-standard";
                _subGradeLabels[row].ForeColor = AccentRed;
            }
        }
        else
        {
            _subGradeLabels[row].Text = "";
        }
    }

    private void SnapSubstat(int row)
    {
        string rawKey = StatDisplayNames.FromDisplay(_subKeyCombos[row].Text);
        if (Enum.TryParse<StatKey>(rawKey, out var key) &&
            float.TryParse(_subValueBoxes[row].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
        {
            var (snapped, _) = TunableRolls.Resolve(key, val);
            if (snapped.HasValue)
            {
                _subValueBoxes[row].Text = snapped.Value.ToString("0.0#", CultureInfo.InvariantCulture);
            }
        }
    }

    private async Task RescanCurrentAsync()
    {
        if (_currentIndex < 0 || _currentIndex >= _filteredItems.Count) return;
        var item = _filteredItems[_currentIndex];

        _statusLabel.Text = $"Re-scanning {item.ImageFileName}...";
        var recognizer = new EchoRecognizer(_catalog);
        var result = await recognizer.RecognizeAsync(item.ImagePath);

        var updated = EchoReviewItem.FromScanResult(result);
        updated.IsEdited = false;
        updated.IsVerified = false;

        int allIdx = _allItems.IndexOf(item);
        if (allIdx >= 0) _allItems[allIdx] = updated;
        _filteredItems[_currentIndex] = updated;

        LoadItem(updated);
        _statusLabel.Text = $"Re-scan complete for {item.ImageFileName}.";
    }

    private void OpenRegionConfig()
    {
        if (_currentIndex < 0 || _currentIndex >= _filteredItems.Count)
        {
            MessageBox.Show("Load a session first — the area config needs a sample echo panel.",
                "No Sample", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var item = _filteredItems[_currentIndex];
        if (!File.Exists(item.ImagePath))
        {
            MessageBox.Show($"Sample image not found:\n{item.ImagePath}",
                "No Sample", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            using var full = new Bitmap(item.ImagePath);
            using var panel = EchoRegions.ExtractPanel(full);
            using var dlg = new RegionConfigDialog(panel);
            dlg.ShowDialog(this);
            _statusLabel.Text = EchoRegions.HasOverrides
                ? $"Area overrides active ({EchoRegions.OverrideFileName}). Re-scan to apply."
                : "Area config: compiled defaults in effect.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot open area config: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void MarkCurrentVerified()
    {
        if (_currentIndex < 0 || _currentIndex >= _filteredItems.Count) return;
        var item = _filteredItems[_currentIndex];
        item.IsVerified = true;
        _statusLabel.Text = $"Marked {item.ImageFileName} ({item.EchoName}) as verified.";
        OnFieldChanged();
        NavigateNext();
    }

    private void NavigateNext()
    {
        if (_currentIndex < _filteredItems.Count - 1)
        {
            _echoListView.Items[_currentIndex + 1].Selected = true;
            _echoListView.EnsureVisible(_currentIndex + 1);
        }
    }

    private void NavigatePrev()
    {
        if (_currentIndex > 0)
        {
            _echoListView.Items[_currentIndex - 1].Selected = true;
            _echoListView.EnsureVisible(_currentIndex - 1);
        }
    }

    private void ClearEditor()
    {
        _currentIndex = -1;
        _nameCombo.Text = "";
        _costCombo.Text = "";
        _rarityCombo.SelectedIndex = -1;
        _sonataCombo.SelectedIndex = -1;
        _mainStatKeyCombo.SelectedIndex = -1;
        _mainStatValueBox.Text = "";
        _screenshotBox.Image = null;
        for (int i = 0; i < 5; i++)
        {
            _subActiveChecks[i].Checked = false;
            _subKeyCombos[i].SelectedIndex = -1;
            _subValueBoxes[i].Text = "";
            _subGradeLabels[i].Text = "";
        }
        _rawEvidenceBox.Text = "";
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Right || (e.Control && e.KeyCode == Keys.D))
        {
            NavigateNext();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Left || (e.Control && e.KeyCode == Keys.A))
        {
            NavigatePrev();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Space)
        {
            MarkCurrentVerified();
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.S)
        {
            _ = SaveVerifiedJsonAsync();
            e.Handled = true;
        }
    }
}

// ── Custom Dark ToolStrip Renderer ──────────────────────────────────────────

internal class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    public DarkToolStripRenderer() : base(new DarkColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.ForeColor;
        base.OnRenderItemText(e);
    }
}

internal class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(34, 36, 48);
    public override Color MenuBorder => Color.FromArgb(50, 52, 68);
    public override Color MenuItemSelected => Color.FromArgb(60, 62, 82);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 62, 82);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 62, 82);
    public override Color MenuItemBorder => Color.Transparent;
}
