using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SonoroScore.Scanner;

namespace SonoroScore;

/// <summary>
/// Visual scan-area config: shows a sample echo panel crop with every scan
/// region as a dashed box. Select a region, then drag its box with the mouse
/// (move inside, resize on borders), type Left/Top/Right/Bottom (panel-relative
/// 0–1), or nudge with the arrow buttons (1 panel-pixel per press).
/// Save writes regions.override.json next to the exe; Reset restores defaults.
/// </summary>
public sealed class RegionConfigDialog : Form
{
    private readonly Bitmap _panel;
    private Dictionary<string, RectangleF> _working = new();
    private string _selected = "SubstatsBlock";
    private bool _syncing;

    // Drag state (relative units)
    private bool _dragging;
    private DragEdge _dragEdge;
    private float _grabDX, _grabDY;

    [Flags]
    private enum DragEdge { None = 0, Move = 1, L = 2, T = 4, R = 8, B = 16 }

    private static readonly Color BgDark = Color.FromArgb(24, 25, 32);
    private static readonly Color BgCard = Color.FromArgb(34, 36, 48);
    private static readonly Color BgInput = Color.FromArgb(44, 46, 62);
    private static readonly Color FgPrimary = Color.FromArgb(240, 242, 245);
    private static readonly Color FgSecondary = Color.FromArgb(160, 165, 185);
    private static readonly Color AccentPurple = Color.FromArgb(130, 100, 250);
    private static readonly Color AccentAmber = Color.FromArgb(241, 196, 15);
    private static readonly Color AccentGreen = Color.FromArgb(46, 204, 113);

    private ListBox _regionList = null!;
    private PictureBox _view = null!;
    private NumericUpDown _numL = null!, _numT = null!, _numR = null!, _numB = null!;
    private Label _pixelLabel = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private SplitContainer _split = null!;
    private SplitContainer _content = null!;

    public RegionConfigDialog(Bitmap panelSample)
    {
        _panel = new Bitmap(panelSample);
        foreach (var (k, v) in EchoRegions.Snapshot())
            _working[k] = v;

        Text = "Scan Area Config — drag boxes, type sides, or nudge" +
               (EchoRegions.HasOverrides ? " (overrides active)" : " (defaults)");
        Size = new Size(1020, 720);
        MinimumSize = new Size(860, 600);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = BgDark;
        ForeColor = FgPrimary;

        BuildLayout();
        SelectRegion(_selected);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _panel.Dispose();
        base.Dispose(disposing);
    }

    // ── Layout ──────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill, SplitterWidth = 6,
            BackColor = BgDark, FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 140
        };

        // Left: region list
        var left = new Panel { Dock = DockStyle.Fill, BackColor = BgCard, Padding = new Padding(8) };
        var listTitle = new Label
        {
            Text = "SCAN REGIONS", Dock = DockStyle.Top, Height = 26,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = AccentPurple
        };
        _regionList = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = BgDark, ForeColor = FgPrimary,
            BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f)
        };
        _regionList.Items.AddRange(EchoRegions.RegionNames.Cast<object>().ToArray());
        _regionList.SelectedIndexChanged += (s, e) =>
        {
            if (_regionList.SelectedItem is string name) SelectRegion(name);
        };
        left.Controls.Add(_regionList);
        left.Controls.Add(listTitle);
        _split.Panel1.Controls.Add(left);

        // Right split: center view + right editor
        _content = new SplitContainer
        {
            Dock = DockStyle.Fill, SplitterWidth = 6,
            BackColor = BgDark, FixedPanel = FixedPanel.Panel2,
            Panel1MinSize = 200, Panel2MinSize = 230
        };

        _view = new PictureBox
        {
            Dock = DockStyle.Fill, BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Cross
        };
        _view.Image = _panel;
        _view.Paint += OnViewPaint;
        _view.MouseDown += OnViewMouseDown;
        _view.MouseMove += OnViewMouseMove;
        _view.MouseUp += OnViewMouseUp;
        _content.Panel1.Controls.Add(_view);

        var editor = new Panel { Dock = DockStyle.Fill, BackColor = BgCard, Padding = new Padding(10), AutoScroll = true };
        BuildEditor(editor);
        _content.Panel2.Controls.Add(editor);

        _split.Panel2.Controls.Add(_content);
        Controls.Add(_split);

        var status = new StatusStrip { BackColor = BgCard, ForeColor = FgSecondary };
        _statusLabel = new ToolStripStatusLabel("Drag a box, type the four sides, or nudge with arrows.") { Spring = true };
        status.Items.Add(_statusLabel);
        Controls.Add(status);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Splitter distances must be applied after layout exists: setting them
        // while the containers still have default size freezes a squeezed panel.
        // Left list 180px, right editor 250px, center takes the rest —
        // unless a saved layout from the previous session exists.
        try
        {
            if (!TryRestoreLayout())
            {
                _split.SplitterDistance = Math.Min(180, _split.Width - _split.Panel2MinSize - _split.SplitterWidth);
                int rightW = Math.Min(250, _content.Width - _content.Panel1MinSize - _content.SplitterWidth);
                _content.SplitterDistance = _content.Width - rightW - _content.SplitterWidth;
            }
        }
        catch { /* keep designer defaults */ }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveLayout();
        base.OnFormClosing(e);
    }

    // ── Layout persistence (remembers previous size + sections) ─────────────

    private static string LayoutPath
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "area_dialog_layout.json");

    private bool TryRestoreLayout()
    {
        string path = LayoutPath;
        if (!File.Exists(path)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            int w = root.GetProperty("width").GetInt32();
            int h = root.GetProperty("height").GetInt32();
            int splitMain = root.GetProperty("splitMain").GetInt32();
            int splitContent = root.GetProperty("splitContent").GetInt32();

            var area = Screen.FromControl(this).WorkingArea;
            w = Math.Clamp(w, MinimumSize.Width, Math.Max(MinimumSize.Width, area.Width));
            h = Math.Clamp(h, MinimumSize.Height, Math.Max(MinimumSize.Height, area.Height));
            Size = new Size(w, h);
            // Layout with the restored size before placing splitters.
            PerformLayout();
            _split.SplitterDistance = Math.Clamp(splitMain, _split.Panel1MinSize,
                Math.Max(_split.Panel1MinSize, _split.Width - 100));
            _content.SplitterDistance = Math.Clamp(splitContent, _content.Panel1MinSize,
                Math.Max(_content.Panel1MinSize, _content.Width - _content.Panel2MinSize));
            return true;
        }
        catch { return false; }
    }

    private void SaveLayout()
    {
        try
        {
            var payload = new
            {
                width = Width,
                height = Height,
                splitMain = _split.SplitterDistance,
                splitContent = _content.SplitterDistance
            };
            File.WriteAllText(LayoutPath,
                System.Text.Json.JsonSerializer.Serialize(payload,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* layout save is best-effort */ }
    }

    private void BuildEditor(Panel editor)
    {
        var title = new Label
        {
            Text = "REGION GEOMETRY", Dock = DockStyle.Top, Height = 26,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = AccentPurple
        };
        editor.Controls.Add(title);

        string[] labels = ["Left:", "Top:", "Right:", "Bottom:"];
        NumericUpDown[] nums = new NumericUpDown[4];
        for (int i = 0; i < 4; i++)
        {
            var lbl = new Label
            {
                Text = labels[i], Dock = DockStyle.Top, Height = 20,
                ForeColor = FgSecondary, Padding = new Padding(0, 4, 0, 0)
            };
            var num = new NumericUpDown
            {
                Dock = DockStyle.Top, Height = 26, DecimalPlaces = 4,
                Minimum = 0, Maximum = 1, Increment = 0.0005m,
                BackColor = BgInput, ForeColor = FgPrimary
            };
            int idx = i;
            num.ValueChanged += (s, e) => OnSideTyped(idx);
            nums[i] = num;
            editor.Controls.Add(num);
            editor.Controls.Add(lbl);
        }
        _numL = nums[0]; _numT = nums[1]; _numR = nums[2]; _numB = nums[3];

        _pixelLabel = new Label
        {
            Dock = DockStyle.Top, Height = 44, ForeColor = AccentGreen,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic), Padding = new Padding(0, 6, 0, 0)
        };
        editor.Controls.Add(_pixelLabel);

        var nudgeTitle = new Label
        {
            Text = "Nudge (1 panel-px / press):", Dock = DockStyle.Top, Height = 24,
            ForeColor = FgSecondary, Padding = new Padding(0, 6, 0, 0)
        };
        editor.Controls.Add(nudgeTitle);

        var pad = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 108, ColumnCount = 3, RowCount = 3,
            BackColor = Color.Transparent
        };
        for (int i = 0; i < 3; i++)
        {
            pad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            pad.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        }
        var btnUp = NudgeButton("↑", 0, -1);
        var btnLeft = NudgeButton("←", -1, 0);
        var btnRight = NudgeButton("→", 1, 0);
        var btnDown = NudgeButton("↓", 0, 1);
        pad.Controls.Add(btnUp, 1, 0);
        pad.Controls.Add(btnLeft, 0, 1);
        pad.Controls.Add(btnRight, 2, 1);
        pad.Controls.Add(btnDown, 1, 2);
        editor.Controls.Add(pad);

        var btnSave = new Button
        {
            Text = "💾 Save Override", Dock = DockStyle.Top, Height = 34,
            BackColor = AccentPurple, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 10, 0, 0)
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += (s, e) => SaveOverrides();
        editor.Controls.Add(btnSave);

        var btnReset = new Button
        {
            Text = "⟲ Reset Defaults", Dock = DockStyle.Top, Height = 30,
            BackColor = BgInput, ForeColor = FgPrimary, FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 6, 0, 0)
        };
        btnReset.FlatAppearance.BorderSize = 0;
        btnReset.Click += (s, e) => ResetDefaults();
        editor.Controls.Add(btnReset);

        var hint = new Label
        {
            Text = "Saved to regions.override.json next to the exe. The scanner picks it up on next scan.",
            Dock = DockStyle.Top, Height = 60, ForeColor = FgSecondary,
            Font = new Font("Segoe UI", 8.5f), Padding = new Padding(0, 8, 0, 0)
        };
        editor.Controls.Add(hint);
    }

    private Button NudgeButton(string text, int dx, int dy)
    {
        var b = new Button
        {
            Text = text, Dock = DockStyle.Fill, BackColor = BgInput,
            ForeColor = FgPrimary, FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold)
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (s, e) => Nudge(dx, dy);
        return b;
    }

    // ── Selection + sync ────────────────────────────────────────────────────

    private void SelectRegion(string name)
    {
        _selected = name;
        _regionList.SelectedItem = name;
        PushToInputs();
        _view.Invalidate();
        _statusLabel.Text = $"Selected: {name} — drag its box, type sides, or nudge.";
    }

    private void PushToInputs()
    {
        _syncing = true;
        try
        {
            var r = _working[_selected];
            _numL.Value = ClampDec((decimal)r.X);
            _numT.Value = ClampDec((decimal)r.Y);
            _numR.Value = ClampDec((decimal)(r.X + r.Width));
            _numB.Value = ClampDec((decimal)(r.Y + r.Height));
            UpdatePixelLabel();
        }
        finally { _syncing = false; }
    }

    private static decimal ClampDec(decimal v) => Math.Max(0m, Math.Min(1m, v));

    private void OnSideTyped(int idx)
    {
        if (_syncing) return;
        float[] sides = [(float)_numL.Value, (float)_numT.Value, (float)_numR.Value, (float)_numB.Value];
        float l = sides[0], t = sides[1], r = sides[2], b = sides[3];
        if (r <= l + 0.005f) r = Math.Min(1f, l + 0.005f);
        if (b <= t + 0.005f) b = Math.Min(1f, t + 0.005f);
        _working[_selected] = new RectangleF(l, t, r - l, b - t);
        PushToInputs(); // re-normalize display
        _view.Invalidate();
    }

    private void Nudge(int dx, int dy)
    {
        var r = _working[_selected];
        float sx = 1f / Math.Max(1, _panel.Width);
        float sy = 1f / Math.Max(1, _panel.Height);
        float nx = Math.Clamp(r.X + dx * sx, 0f, 1f - r.Width);
        float ny = Math.Clamp(r.Y + dy * sy, 0f, 1f - r.Height);
        _working[_selected] = new RectangleF(nx, ny, r.Width, r.Height);
        PushToInputs();
        _view.Invalidate();
    }

    private void UpdatePixelLabel()
    {
        var r = _working[_selected];
        var px = EchoRegions.ToPixels(r, _panel.Width, _panel.Height);
        _pixelLabel.Text = $"{_selected}: {px.Width}×{px.Height} px @ ({px.X},{px.Y})\r\npanel {_panel.Width}×{_panel.Height} px";
    }

    // ── Zoom mapping (PictureBoxSizeMode.Zoom) ──────────────────────────────

    private float ZoomScale()
    {
        var c = _view.ClientSize;
        if (_panel.Width <= 0 || _panel.Height <= 0 || c.Width <= 0 || c.Height <= 0) return 1f;
        return Math.Min(c.Width / (float)_panel.Width, c.Height / (float)_panel.Height);
    }

    private PointF ClientToImage(Point p)
    {
        float s = ZoomScale();
        float dw = _panel.Width * s, dh = _panel.Height * s;
        float ox = (_view.ClientSize.Width - dw) / 2f;
        float oy = (_view.ClientSize.Height - dh) / 2f;
        return new PointF((p.X - ox) / s, (p.Y - oy) / s);
    }

    private RectangleF ImageToClient(RectangleF imgPx)
    {
        float s = ZoomScale();
        float dw = _panel.Width * s, dh = _panel.Height * s;
        float ox = (_view.ClientSize.Width - dw) / 2f;
        float oy = (_view.ClientSize.Height - dh) / 2f;
        return new RectangleF(ox + imgPx.X * s, oy + imgPx.Y * s, imgPx.Width * s, imgPx.Height * s);
    }

    private RectangleF SelectedImagePx()
    {
        var r = _working[_selected];
        return new RectangleF(r.X * _panel.Width, r.Y * _panel.Height,
                              r.Width * _panel.Width, r.Height * _panel.Height);
    }

    // ── Paint: dashed boxes ─────────────────────────────────────────────────

    private void OnViewPaint(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 8f, FontStyle.Bold);

        // Big zones first, selected last (topmost).
        var order = EchoRegions.RegionNames.Where(n => n != _selected).Append(_selected);
        foreach (string name in order)
        {
            if (!_working.TryGetValue(name, out var r)) continue;
            var imgPx = new RectangleF(r.X * _panel.Width, r.Y * _panel.Height,
                                       r.Width * _panel.Width, r.Height * _panel.Height);
            var c = ImageToClient(imgPx);
            bool sel = name == _selected;
            using var pen = new Pen(sel ? AccentAmber : AccentPurple, sel ? 2.5f : 1.5f)
            {
                DashStyle = DashStyle.Dash
            };
            e.Graphics.DrawRectangle(pen, c.X, c.Y, c.Width, c.Height);
            using var brush = new SolidBrush(sel ? AccentAmber : AccentPurple);
            e.Graphics.DrawString(name, font, brush, c.X + 3, c.Y + 2);
        }
    }

    // ── Mouse: select / move / resize ───────────────────────────────────────

    private string? HitRegion(PointF imgPx)
    {
        // Topmost = reverse paint order (selected is last, so check it first).
        var order = new[] { _selected }.Concat(EchoRegions.RegionNames.Where(n => n != _selected));
        foreach (string name in order)
        {
            if (!_working.TryGetValue(name, out var r)) continue;
            var box = new RectangleF(r.X * _panel.Width, r.Y * _panel.Height,
                                     r.Width * _panel.Width, r.Height * _panel.Height);
            if (box.Contains(imgPx)) return name;
        }
        return null;
    }

    private void OnViewMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var img = ClientToImage(e.Location);

        // Click on another box selects it (drag starts on next press).
        string? hit = HitRegion(img);
        if (hit != null && hit != _selected)
        {
            SelectRegion(hit);
            return;
        }

        var box = SelectedImagePx();
        if (!box.Contains(img)) return;

        float edgeTol = 7f / ZoomScale(); // 7 client px, in image px
        bool nearL = Math.Abs(img.X - box.Left) <= edgeTol;
        bool nearR = Math.Abs(img.X - box.Right) <= edgeTol;
        bool nearT = Math.Abs(img.Y - box.Top) <= edgeTol;
        bool nearB = Math.Abs(img.Y - box.Bottom) <= edgeTol;

        _dragEdge = DragEdge.Move;
        if (nearL) _dragEdge |= DragEdge.L;
        if (nearR) _dragEdge |= DragEdge.R;
        if (nearT) _dragEdge |= DragEdge.T;
        if (nearB) _dragEdge |= DragEdge.B;
        if (_dragEdge != DragEdge.Move)
            _dragEdge &= ~DragEdge.Move; // pure resize (drop Move flag)

        // Grab offset in relative units (for move).
        var r = _working[_selected];
        _grabDX = img.X / _panel.Width - r.X;
        _grabDY = img.Y / _panel.Height - r.Y;

        _dragging = true;
        _view.Capture = true;
    }

    private void OnViewMouseMove(object? sender, MouseEventArgs e)
    {
        var img = ClientToImage(e.Location);
        if (!_dragging)
        {
            // Hover cursor feedback on the selected box.
            var box = SelectedImagePx();
            if (box.Contains(img))
            {
                float edgeTol = 7f / ZoomScale();
                bool edge = Math.Abs(img.X - box.Left) <= edgeTol ||
                            Math.Abs(img.X - box.Right) <= edgeTol ||
                            Math.Abs(img.Y - box.Top) <= edgeTol ||
                            Math.Abs(img.Y - box.Bottom) <= edgeTol;
                _view.Cursor = edge ? Cursors.SizeNWSE : Cursors.SizeAll;
            }
            else _view.Cursor = Cursors.Cross;
            return;
        }

        var r = _working[_selected];
        float cx = img.X / _panel.Width, cy = img.Y / _panel.Height;

        if (_dragEdge.HasFlag(DragEdge.Move))
        {
            float nx = Math.Clamp(cx - _grabDX, 0f, 1f - r.Width);
            float ny = Math.Clamp(cy - _grabDY, 0f, 1f - r.Height);
            r = new RectangleF(nx, ny, r.Width, r.Height);
        }
        else
        {
            float l = r.X, t = r.Y, rr = r.X + r.Width, b = r.Y + r.Height;
            if (_dragEdge.HasFlag(DragEdge.L)) l = Math.Min(cx, rr - 0.005f);
            if (_dragEdge.HasFlag(DragEdge.R)) rr = Math.Max(cx, l + 0.005f);
            if (_dragEdge.HasFlag(DragEdge.T)) t = Math.Min(cy, b - 0.005f);
            if (_dragEdge.HasFlag(DragEdge.B)) b = Math.Max(cy, t + 0.005f);
            l = Math.Clamp(l, 0f, 1f); t = Math.Clamp(t, 0f, 1f);
            rr = Math.Clamp(rr, 0f, 1f); b = Math.Clamp(b, 0f, 1f);
            r = new RectangleF(l, t, Math.Max(0.005f, rr - l), Math.Max(0.005f, b - t));
        }

        _working[_selected] = r;
        PushToInputs(); // live: dragging updates the coord fields
        _view.Invalidate();
    }

    private void OnViewMouseUp(object? sender, MouseEventArgs e)
    {
        _dragging = false;
        _dragEdge = DragEdge.None;
        _view.Capture = false;
    }

    // ── Save / reset ────────────────────────────────────────────────────────

    private void SaveOverrides()
    {
        try
        {
            EchoRegions.SaveOverrides(new Dictionary<string, RectangleF>(_working));
            Text = "Scan Area Config — overrides active";
            _statusLabel.Text = $"Saved to {EchoRegions.OverrideFileName} — scanner picks it up on next scan.";
            MessageBox.Show($"Area config saved to:\n{EchoRegions.OverridePath}",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetDefaults()
    {
        EchoRegions.ResetOverrides();
        _working = new Dictionary<string, RectangleF>(EchoRegions.Snapshot());
        Text = "Scan Area Config — defaults";
        SelectRegion(_selected);
        _statusLabel.Text = "Restored compiled defaults.";
    }
}
