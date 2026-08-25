using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace OwlUsageTray;

internal sealed class SettingsChangedEventArgs : EventArgs
{
    public int AcrylicOpacityPercent { get; init; }
    public int BlurStrength { get; init; }
    public int RefreshSeconds { get; init; }
    public bool ResetWidgetSize { get; init; }
}

internal sealed class SettingsForm : Form
{
    private const int CornerRadius = 24;
    private static readonly RectangleF CloseBounds = new(515, 16, 28, 28);
    private static readonly RectangleF OpacityTrack = new(42, 136, 476, 6);
    private static readonly RectangleF BlurTrack = new(42, 235, 476, 6);
    private static readonly RectangleF RefreshTrack = new(42, 340, 372, 6);
    private static readonly RectangleF RefreshInputBounds = new(438, 321, 86, 30);
    private static readonly RectangleF ReloginBounds = new(22, 386, 160, 42);
    private static readonly RectangleF WebBounds = new(200, 386, 160, 42);
    private static readonly RectangleF DefaultsBounds = new(378, 386, 160, 42);

    private int _opacityPercent;
    private int _blurStrength;
    private int _refreshSeconds;
    private SliderTarget _draggingSlider;
    private bool _editingRefresh;
    private string _refreshInputText = "";
    private bool _previewMode;
    private bool _acrylicApplied;

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    public event EventHandler? ReloginRequested;
    public event EventHandler? OpenWebRequested;

    public SettingsForm(AppSettings settings)
    {
        Text = "Codex 设置";
        ClientSize = new Size(560, 474);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        BackColor = Color.FromArgb(55, 66, 82);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);
        DoubleBuffered = true;
        KeyPreview = true;

        _opacityPercent = Math.Clamp(settings.AcrylicOpacityPercent, 20, 95);
        _blurStrength = Math.Clamp(settings.BlurStrength, 0, 100);
        _refreshSeconds = Math.Clamp(settings.RefreshSeconds, 10, 300);

        MouseDown += HandleMouseDown;
        MouseMove += HandleMouseMove;
        MouseUp += HandleMouseUp;
        MouseLeave += (_, _) =>
        {
            if (_draggingSlider == SliderTarget.None) Cursor = Cursors.Default;
        };
    }

    public void SetPreviewMode()
    {
        _previewMode = true;
        _acrylicApplied = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyAcrylic();
        ApplyRoundedRegion();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyAcrylic();
        ApplyRoundedRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (_acrylicApplied && !_previewMode) return;
        using var background = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(112, 125, 143),
            Color.FromArgb(55, 68, 86),
            LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillRectangle(background, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var borderPen = new Pen(Color.FromArgb(90, 225, 235, 245), 1.2F);
        graphics.DrawRoundedRectangle(
            borderPen,
            new RectangleF(0.8F, 0.8F, Width - 2.1F, Height - 2.1F),
            CornerRadius);

        DrawText(graphics, "Codex 设置", 18F, FontStyle.Bold, Color.White, 28, 20);
        DrawText(graphics, "显示与用量刷新", 8.5F, FontStyle.Regular, Muted(165), 29, 51);
        DrawCloseButton(graphics);

        DrawSettingPanel(graphics, new RectangleF(20, 75, 520, 88));
        DrawSettingPanel(graphics, new RectangleF(20, 174, 520, 88));
        DrawSettingPanel(graphics, new RectangleF(20, 273, 520, 96));

        DrawText(graphics, "背景透明度", 10F, FontStyle.Bold, Color.White, 34, 85);
        DrawText(graphics, "调整亚克力底板的透光程度", 8F, FontStyle.Regular, Muted(150), 34, 111);
        DrawRightText(graphics, $"{_opacityPercent}%", 9F, FontStyle.Bold, Color.FromArgb(220, 235, 242, 250), 524, 87);
        DrawSlider(graphics, OpacityTrack, _opacityPercent, 20, 95);

        DrawText(graphics, "模糊强度", 10F, FontStyle.Bold, Color.White, 34, 184);
        DrawText(graphics, "控制背景模糊层级，0 为不模糊", 8F, FontStyle.Regular, Muted(150), 34, 210);
        DrawRightText(graphics, $"{_blurStrength}%", 9F, FontStyle.Bold, Color.FromArgb(220, 235, 242, 250), 524, 186);
        DrawSlider(graphics, BlurTrack, _blurStrength, 0, 100);

        DrawText(graphics, "刷新频率", 10F, FontStyle.Bold, Color.White, 34, 283);
        DrawText(graphics, "范围 10 秒至 5 分钟", 8F, FontStyle.Regular, Muted(150), 34, 309);
        DrawSlider(graphics, RefreshTrack, _refreshSeconds, 10, 300);
        DrawRefreshInput(graphics);
        DrawText(graphics, "秒", 8.5F, FontStyle.Regular, Muted(170), 528, 327);

        DrawButton(graphics, ReloginBounds, "重新登录", accent: false);
        DrawButton(graphics, WebBounds, "前往网页", accent: true);
        DrawButton(graphics, DefaultsBounds, "恢复默认设置", accent: false);
        DrawText(graphics, "更改会自动保存", 8F, FontStyle.Regular, Muted(125), 22, 447);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!_editingRefresh || !char.IsDigit(e.KeyChar)) return;
        if (_refreshInputText.Length < 3)
        {
            _refreshInputText += e.KeyChar;
            Invalidate(Rectangle.Ceiling(RefreshInputBounds));
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_editingRefresh) return;

        if (e.KeyCode == Keys.Back)
        {
            if (_refreshInputText.Length > 0) _refreshInputText = _refreshInputText[..^1];
            Invalidate(Rectangle.Ceiling(RefreshInputBounds));
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            CommitRefreshInput();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _editingRefresh = false;
            _refreshInputText = _refreshSeconds.ToString();
            Invalidate(Rectangle.Ceiling(RefreshInputBounds));
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Up or Keys.Down)
        {
            var delta = e.KeyCode == Keys.Up ? 10 : -10;
            SetRefreshSeconds(_refreshSeconds + delta);
            _refreshInputText = _refreshSeconds.ToString();
            e.Handled = true;
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        CommitRefreshInput();
        base.OnDeactivate(e);
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_editingRefresh && !RefreshInputBounds.Contains(e.X, e.Y)) CommitRefreshInput();

        if (CloseBounds.Contains(e.X, e.Y))
        {
            Close();
            return;
        }
        if (ReloginBounds.Contains(e.X, e.Y))
        {
            ReloginRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (WebBounds.Contains(e.X, e.Y))
        {
            OpenWebRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (DefaultsBounds.Contains(e.X, e.Y))
        {
            RestoreDefaults();
            return;
        }
        if (RefreshInputBounds.Contains(e.X, e.Y))
        {
            _editingRefresh = true;
            _refreshInputText = _refreshSeconds.ToString();
            Focus();
            Invalidate(Rectangle.Ceiling(RefreshInputBounds));
            return;
        }
        if (HitSlider(OpacityTrack, e.Location))
        {
            BeginSliderDrag(SliderTarget.Opacity, e.X);
            return;
        }
        if (HitSlider(BlurTrack, e.Location))
        {
            BeginSliderDrag(SliderTarget.Blur, e.X);
            return;
        }
        if (HitSlider(RefreshTrack, e.Location))
        {
            BeginSliderDrag(SliderTarget.Refresh, e.X);
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0);
    }

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggingSlider != SliderTarget.None)
        {
            UpdateSlider(_draggingSlider, e.X);
            return;
        }

        Cursor = IsInteractivePoint(e.Location) ? Cursors.Hand : Cursors.SizeAll;
    }

    private void HandleMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _draggingSlider = SliderTarget.None;
        Capture = false;
    }

    private void BeginSliderDrag(SliderTarget target, int pointerX)
    {
        _draggingSlider = target;
        Capture = true;
        UpdateSlider(target, pointerX);
    }

    private void UpdateSlider(SliderTarget target, int pointerX)
    {
        var track = target switch
        {
            SliderTarget.Opacity => OpacityTrack,
            SliderTarget.Blur => BlurTrack,
            _ => RefreshTrack
        };
        var ratio = Math.Clamp((pointerX - track.Left) / track.Width, 0F, 1F);

        switch (target)
        {
            case SliderTarget.Opacity:
                _opacityPercent = 20 + (int)Math.Round(75 * ratio);
                ApplyAcrylic();
                break;
            case SliderTarget.Blur:
                _blurStrength = (int)Math.Round(100 * ratio);
                ApplyAcrylic();
                break;
            case SliderTarget.Refresh:
                _refreshSeconds = 10 + (int)Math.Round(290 * ratio);
                _refreshInputText = _refreshSeconds.ToString();
                break;
        }

        RaiseSettingsChanged();
        Invalidate();
    }

    private void SetRefreshSeconds(int value)
    {
        _refreshSeconds = Math.Clamp(value, 10, 300);
        RaiseSettingsChanged();
        Invalidate();
    }

    private void CommitRefreshInput()
    {
        if (!_editingRefresh) return;
        _editingRefresh = false;
        if (int.TryParse(_refreshInputText, out var value)) SetRefreshSeconds(value);
        _refreshInputText = _refreshSeconds.ToString();
        Invalidate(Rectangle.Ceiling(RefreshInputBounds));
    }

    private void RestoreDefaults()
    {
        _opacityPercent = AppSettings.DefaultAcrylicOpacityPercent;
        _blurStrength = AppSettings.DefaultBlurStrength;
        _refreshSeconds = AppSettings.DefaultRefreshSeconds;
        _refreshInputText = _refreshSeconds.ToString();
        _editingRefresh = false;
        ApplyAcrylic();
        RaiseSettingsChanged(resetWidgetSize: true);
        Invalidate();
    }

    private void RaiseSettingsChanged(bool resetWidgetSize = false)
    {
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs
        {
            AcrylicOpacityPercent = _opacityPercent,
            BlurStrength = _blurStrength,
            RefreshSeconds = _refreshSeconds,
            ResetWidgetSize = resetWidgetSize
        });
    }

    private void ApplyAcrylic()
    {
        if (!IsHandleCreated || _previewMode) return;
        _acrylicApplied = AcrylicWindow.TryEnable(Handle, _opacityPercent, _blurStrength);
        ApplyRoundedRegion();
        Invalidate();
    }

    private void ApplyRoundedRegion()
    {
        if (!IsHandleCreated) return;

        using (var path = GraphicsExtensions.CreateRoundedRectangle(
                   new RectangleF(0, 0, Width, Height),
                   CornerRadius))
        {
            var managedRegion = new Region(path);
            Region?.Dispose();
            Region = managedRegion;
        }

        var nativeRegion = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, CornerRadius * 2, CornerRadius * 2);
        if (nativeRegion != IntPtr.Zero && SetWindowRgn(Handle, nativeRegion, true) == 0)
        {
            DeleteObject(nativeRegion);
        }
    }

    private static void DrawSettingPanel(Graphics graphics, RectangleF bounds)
    {
        using var brush = new SolidBrush(Color.FromArgb(24, 232, 239, 248));
        using var pen = new Pen(Color.FromArgb(34, 225, 235, 245), 1F);
        graphics.FillRoundedRectangle(brush, bounds, 16);
        graphics.DrawRoundedRectangle(pen, bounds, 16);
    }

    private static void DrawSlider(Graphics graphics, RectangleF track, int value, int minimum, int maximum)
    {
        using var trackBrush = new SolidBrush(Color.FromArgb(68, 225, 233, 243));
        graphics.FillRoundedRectangle(trackBrush, track, 3);
        var ratio = (value - minimum) / (float)(maximum - minimum);
        var centerX = track.Left + track.Width * ratio;
        var fill = new RectangleF(track.Left, track.Top, Math.Max(4, centerX - track.Left), track.Height);
        using var fillBrush = new SolidBrush(Color.FromArgb(96, 165, 250));
        graphics.FillRoundedRectangle(fillBrush, fill, 3);
        using var haloBrush = new SolidBrush(Color.FromArgb(55, 96, 165, 250));
        using var thumbBrush = new SolidBrush(Color.FromArgb(232, 242, 251));
        graphics.FillEllipse(haloBrush, centerX - 10, track.Top - 7, 20, 20);
        graphics.FillEllipse(thumbBrush, centerX - 6, track.Top - 3, 12, 12);
    }

    private void DrawRefreshInput(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.FromArgb(_editingRefresh ? 78 : 48, 31, 43, 59));
        using var pen = new Pen(
            _editingRefresh ? Color.FromArgb(96, 165, 250) : Color.FromArgb(72, 226, 235, 246),
            _editingRefresh ? 1.5F : 1F);
        graphics.FillRoundedRectangle(brush, RefreshInputBounds, 8);
        graphics.DrawRoundedRectangle(pen, RefreshInputBounds, 8);
        var text = _editingRefresh ? _refreshInputText : _refreshSeconds.ToString();
        using var font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        var size = graphics.MeasureString(text, font);
        using var textBrush = new SolidBrush(Color.White);
        graphics.DrawString(
            text,
            font,
            textBrush,
            RefreshInputBounds.Left + (RefreshInputBounds.Width - size.Width) / 2,
            RefreshInputBounds.Top + 5);
    }

    private static void DrawButton(Graphics graphics, RectangleF bounds, string text, bool accent)
    {
        using var brush = new SolidBrush(
            accent
                ? Color.FromArgb(195, 59, 130, 246)
                : Color.FromArgb(34, 235, 241, 249));
        using var pen = new Pen(Color.FromArgb(62, 228, 237, 247), 1F);
        graphics.FillRoundedRectangle(brush, bounds, 12);
        graphics.DrawRoundedRectangle(pen, bounds, 12);
        using var font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        var size = graphics.MeasureString(text, font);
        using var textBrush = new SolidBrush(Color.White);
        graphics.DrawString(
            text,
            font,
            textBrush,
            bounds.Left + (bounds.Width - size.Width) / 2,
            bounds.Top + (bounds.Height - size.Height) / 2);
    }

    private static void DrawCloseButton(Graphics graphics)
    {
        using var brush = new SolidBrush(Color.FromArgb(34, 235, 241, 249));
        using var pen = new Pen(Color.FromArgb(62, 228, 237, 247), 1F);
        graphics.FillRoundedRectangle(brush, CloseBounds, 13);
        graphics.DrawRoundedRectangle(pen, CloseBounds, 13);
        DrawText(graphics, "×", 12F, FontStyle.Regular, Color.White, 523, 19);
    }

    private static void DrawText(
        Graphics graphics,
        string text,
        float size,
        FontStyle style,
        Color color,
        float x,
        float y)
    {
        using var font = new Font("Microsoft YaHei UI", size, style);
        using var brush = new SolidBrush(color);
        graphics.DrawString(text, font, brush, x, y);
    }

    private static void DrawRightText(
        Graphics graphics,
        string text,
        float size,
        FontStyle style,
        Color color,
        float right,
        float y)
    {
        using var font = new Font("Segoe UI", size, style);
        using var brush = new SolidBrush(color);
        var width = graphics.MeasureString(text, font).Width;
        graphics.DrawString(text, font, brush, right - width, y);
    }

    private static Color Muted(int alpha) => Color.FromArgb(alpha, 211, 221, 234);

    private static bool HitSlider(RectangleF track, Point point) =>
        new RectangleF(track.Left - 8, track.Top - 11, track.Width + 16, 28).Contains(point.X, point.Y);

    private static bool IsInteractivePoint(Point point) =>
        CloseBounds.Contains(point.X, point.Y) ||
        RefreshInputBounds.Contains(point.X, point.Y) ||
        ReloginBounds.Contains(point.X, point.Y) ||
        WebBounds.Contains(point.X, point.Y) ||
        DefaultsBounds.Contains(point.X, point.Y) ||
        HitSlider(OpacityTrack, point) ||
        HitSlider(BlurTrack, point) ||
        HitSlider(RefreshTrack, point);

    private enum SliderTarget
    {
        None,
        Opacity,
        Blur,
        Refresh
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);
}
