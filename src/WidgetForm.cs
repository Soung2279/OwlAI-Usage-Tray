using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace OwlUsageTray;

internal sealed class WidgetForm : Form
{
    private const int CornerRadius = 24;
    public const int DefaultWidgetWidth = 520;
    public const int MinimumWidgetWidth = 300;
    public const int MaximumWidgetWidth = 700;
    public const int DefaultWidgetHeight = 224;
    public const int MinimalWidgetSize = 150;
    public const float MinimumWidgetScale = 0.75F;
    public const float MaximumWidgetScale = 1.50F;
    private const int ResizeBorderWidth = 7;
    private const int CornerResizeZone = 20;
    private const int WindowMessageNcHitTest = 0x0084;
    private const int WindowMessageSizing = 0x0214;
    private const int WindowMessageEnterSizeMove = 0x0231;
    private const int WindowMessageExitSizeMove = 0x0232;
    private const int HitTestClient = 1;
    private const int HitTestLeft = 10;
    private const int HitTestRight = 11;
    private const int HitTestTop = 12;
    private const int HitTestTopLeft = 13;
    private const int HitTestTopRight = 14;
    private const int HitTestBottom = 15;
    private const int HitTestBottomLeft = 16;
    private const int HitTestBottomRight = 17;
    private const int SizingLeft = 1;
    private const int SizingRight = 2;
    private const int SizingTop = 3;
    private const int SizingTopLeft = 4;
    private const int SizingTopRight = 5;
    private const int SizingBottom = 6;
    private const int SizingBottomLeft = 7;
    private const int SizingBottomRight = 8;
    private ProgressResponse? _response;
    private DateTimeOffset? _updatedAt;
    private string? _statusMessage;
    private bool _statusIsError;
    private bool _previewMode;
    private bool _acrylicApplied;
    private bool _minimalMode;
    private int _acrylicOpacityPercent = AppSettings.DefaultAcrylicOpacityPercent;
    private int _blurStrength = AppSettings.DefaultBlurStrength;
    private readonly System.Windows.Forms.Timer _animationTimer = new();
    private readonly TextToggleState _weeklyValueState = new();
    private readonly TextToggleState _monthlyValueState = new();
    private readonly TextToggleState _weeklyResetState = new();
    private readonly TextToggleState _monthlyResetState = new();
    private float _stripeOffset;
    private int _lastResizeHit = HitTestClient;
    private float _resizeLogicalWidth = DefaultWidgetWidth;
    private float _resizeAspectRatio = DefaultWidgetWidth / (float)DefaultWidgetHeight;
    private Size _normalWidgetSize = new(DefaultWidgetWidth, DefaultWidgetHeight);

    private float UiScale => _minimalMode
        ? ClientSize.Height / (float)DefaultWidgetHeight
        : Math.Clamp(
            ClientSize.Height / (float)DefaultWidgetHeight,
            MinimumWidgetScale,
            MaximumWidgetScale);
    private float LogicalWidth => ClientSize.Width / UiScale;
    private RectangleF WeeklyValueBounds => new(LogicalWidth - 142, 52, 130, 32);
    private RectangleF MonthlyValueBounds => new(LogicalWidth - 142, 126, 130, 32);
    private RectangleF WeeklyResetBounds => new(14, 96, Math.Min(220, LogicalWidth - 28), 29);
    private RectangleF MonthlyResetBounds => new(14, 170, Math.Min(220, LogicalWidth - 28), 29);
    private RectangleF RefreshBounds => new(LogicalWidth - 180, DefaultWidgetHeight - 30, 168, 28);

    public event EventHandler? RefreshRequested;
    public bool MinimalMode => _minimalMode;

    public WidgetForm()
    {
        ClientSize = new Size(DefaultWidgetWidth, DefaultWidgetHeight);
        MinimumSize = new Size(
            (int)Math.Round(MinimumWidgetWidth * MinimumWidgetScale),
            (int)Math.Round(DefaultWidgetHeight * MinimumWidgetScale));
        MaximumSize = new Size(
            (int)Math.Round(MaximumWidgetWidth * MaximumWidgetScale),
            (int)Math.Round(DefaultWidgetHeight * MaximumWidgetScale));
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(55, 66, 82);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        KeyPreview = true;

        MouseDown += DragWindow;
        MouseClick += HandleMouseClick;
        MouseMove += (_, e) => Cursor = IsInteractivePoint(ToLogical(e.Location))
            ? Cursors.Hand
            : Cursors.SizeAll;
        MouseLeave += (_, _) => Cursor = Cursors.Default;
        VisibleChanged += (_, _) => UpdateAnimationTimer();

        _animationTimer.Interval = 33;
        _animationTimer.Tick += (_, _) =>
        {
            if (HasCriticalUsage)
            {
                _stripeOffset = (_stripeOffset + 1.7F * _animationTimer.Interval / 33F) % 22F;
            }
            Invalidate();
            UpdateAnimationTimer();
        };
    }

    public void ApplyAlwaysOnTop(bool alwaysOnTop)
    {
        TopMost = alwaysOnTop;
        Invalidate();
    }

    public void ApplyMinimalMode(bool enabled)
    {
        if (_minimalMode == enabled) return;

        if (enabled)
        {
            _normalWidgetSize = ClientSize;
        }
        _minimalMode = enabled;
        if (enabled)
        {
            ResetTextToggle(_weeklyValueState);
            ResetTextToggle(_monthlyValueState);
            ResetTextToggle(_weeklyResetState);
            ResetTextToggle(_monthlyResetState);
            Cursor = Cursors.Default;
            MinimumSize = new Size(MinimalWidgetSize, MinimalWidgetSize);
            MaximumSize = new Size(MinimalWidgetSize, MinimalWidgetSize);
            ClientSize = new Size(MinimalWidgetSize, MinimalWidgetSize);
        }
        else
        {
            MaximumSize = new Size(
                (int)Math.Round(MaximumWidgetWidth * MaximumWidgetScale),
                (int)Math.Round(DefaultWidgetHeight * MaximumWidgetScale));
            MinimumSize = new Size(
                (int)Math.Round(MinimumWidgetWidth * MinimumWidgetScale),
                (int)Math.Round(DefaultWidgetHeight * MinimumWidgetScale));
            ClientSize = _normalWidgetSize;
        }
        UpdateAnimationTimer();
        Invalidate();
    }

    public void ApplySavedWidth(int width)
    {
        ApplySavedSize(width, ClientSize.Height);
    }

    public void ApplySavedSize(int width, int height)
    {
        var scale = Math.Clamp(
            height / (float)DefaultWidgetHeight,
            MinimumWidgetScale,
            MaximumWidgetScale);
        var logicalWidth = Math.Clamp(
            width / scale,
            MinimumWidgetWidth,
            MaximumWidgetWidth);
        _normalWidgetSize = new Size(
            (int)Math.Round(logicalWidth * scale),
            (int)Math.Round(DefaultWidgetHeight * scale));
        if (!_minimalMode) ClientSize = _normalWidgetSize;
    }

    public void ApplyAcrylicSettings(int opacityPercent, int blurStrength)
    {
        _acrylicOpacityPercent = Math.Clamp(opacityPercent, 20, 95);
        _blurStrength = Math.Clamp(blurStrength, 0, 100);
        if (IsHandleCreated && !_previewMode)
        {
            _acrylicApplied = AcrylicWindow.TryEnable(
                Handle,
                _acrylicOpacityPercent,
                _blurStrength);
            UpdateRoundedRegion();
            Invalidate();
        }
    }

    public void SetPreviewMode()
    {
        _previewMode = true;
        _acrylicApplied = false;
    }

    public void SetLoading()
    {
        _statusMessage = "正在刷新…";
        _statusIsError = false;
        Invalidate();
    }

    public void UpdateUsage(ProgressResponse response, DateTimeOffset updatedAt)
    {
        _response = response;
        _updatedAt = updatedAt;
        _statusMessage = null;
        _statusIsError = false;
        UpdateAnimationTimer();
        Invalidate();
    }

    public void ShowError(string message)
    {
        _statusMessage = message;
        _statusIsError = true;
        Invalidate();
    }

    public void ShowNearTray()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (!Visible)
        {
            Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);
            Show();
        }
        BringToFront();
        Activate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_previewMode)
        {
            _acrylicApplied = AcrylicWindow.TryEnable(
                Handle,
                _acrylicOpacityPercent,
                _blurStrength);
        }
        UpdateRoundedRegion();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_previewMode)
        {
            _acrylicApplied = AcrylicWindow.TryEnable(
                Handle,
                _acrylicOpacityPercent,
                _blurStrength);
        }
        UpdateRoundedRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsHandleCreated) UpdateRoundedRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var backgroundShape = CreateWindowShapePath(
            new RectangleF(0, 0, ClientSize.Width, ClientSize.Height),
            CornerRadius * UiScale);

        if (_acrylicApplied && !_previewMode)
        {
            // AccentFlags stays at zero so DWM contributes only the rounded
            // blur surface. Draw the tint ourselves inside the same geometry;
            // otherwise Windows 10 creates an unclipped rectangular tint.
            var tintAlpha = Math.Clamp(
                (int)Math.Round(_acrylicOpacityPercent / 100D * 255D),
                0,
                255);
            using var tint = new SolidBrush(Color.FromArgb(tintAlpha, 69, 80, 96));
            e.Graphics.FillPath(tint, backgroundShape);
            return;
        }

        using var background = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(121, 132, 148),
            Color.FromArgb(67, 80, 98),
            LinearGradientMode.ForwardDiagonal);
        e.Graphics.FillPath(background, backgroundShape);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.ScaleTransform(UiScale, UiScale);

        using var borderPen = new Pen(Color.FromArgb(90, 225, 235, 245), 1.2F);
        var borderBounds = new RectangleF(
            0.8F,
            0.8F,
            LogicalWidth - 2.1F,
            DefaultWidgetHeight - 2.1F);
        if (_minimalMode) graphics.DrawEllipse(borderPen, borderBounds);
        else graphics.DrawRoundedRectangle(borderPen, borderBounds, CornerRadius);

        if (_minimalMode)
        {
            DrawMinimalMode(graphics);
            return;
        }

        DrawHeader(graphics);

        if (_response is null)
        {
            using var waitingFont = new Font("Microsoft YaHei UI", 10F);
            using var waitingBrush = new SolidBrush(Color.FromArgb(195, 215, 225, 238));
            graphics.DrawString(_statusMessage ?? "正在读取用量…", waitingFont, waitingBrush, 22, 91);
            return;
        }

        DrawUsageSection(
            graphics,
            "本周用量",
            _response.Progress.Weekly,
            58,
            _weeklyValueState,
            _weeklyResetState,
            showResetReturn: false);
        DrawUsageSection(
            graphics,
            "本月用量",
            _response.Progress.Monthly,
            132,
            _monthlyValueState,
            _monthlyResetState,
            showResetReturn: true);

        if (!string.IsNullOrWhiteSpace(_statusMessage))
        {
            using var statusFont = new Font("Microsoft YaHei UI", 8F);
            using var statusBrush = new SolidBrush(
                _statusIsError
                    ? Color.FromArgb(255, 190, 190)
                    : Color.FromArgb(165, 210, 220, 232));
            var size = graphics.MeasureString(_statusMessage, statusFont);
            graphics.DrawString(
                _statusMessage,
                statusFont,
                statusBrush,
                LogicalWidth - size.Width - 20,
                DefaultWidgetHeight - 19);
        }
        else if (_updatedAt is not null)
        {
            using var updatedFont = new Font("Microsoft YaHei UI", 7.5F);
            using var updatedBrush = new SolidBrush(Color.FromArgb(115, 210, 220, 232));
            var text = $"更新于 {_updatedAt.Value.LocalDateTime:HH:mm:ss}";
            var size = graphics.MeasureString(text, updatedFont);
            graphics.DrawString(
                text,
                updatedFont,
                updatedBrush,
                LogicalWidth - size.Width - 20,
                DefaultWidgetHeight - 18);
        }
    }

    private void DrawHeader(Graphics graphics)
    {
        using var titleFont = new Font("Segoe UI", 13F, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.FromArgb(235, 243, 247, 252));
        graphics.DrawString("Codex", titleFont, titleBrush, 20, 14);

        var rawPlanName = _response?.Progress.GroupName ?? "Codex";
        var planName = FormatPlanName(rawPlanName);
        using var planFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        var textSize = graphics.MeasureString(planName, planFont);
        var pillWidth = Math.Clamp(textSize.Width + 24, 74, 210);
        var pillBounds = new RectangleF(LogicalWidth - pillWidth - 19, 13, pillWidth, 29);
        using var pillBrush = new SolidBrush(Color.FromArgb(36, 230, 238, 248));
        using var pillPen = new Pen(Color.FromArgb(82, 229, 237, 247), 1F);
        graphics.FillRoundedRectangle(pillBrush, pillBounds, 14.5F);
        graphics.DrawRoundedRectangle(pillPen, pillBounds, 14.5F);
        using var planBrush = new SolidBrush(Color.FromArgb(225, 239, 245, 252));
        graphics.DrawString(
            planName,
            planFont,
            planBrush,
            pillBounds.Left + (pillBounds.Width - textSize.Width) / 2,
            pillBounds.Top + 5.2F);
    }

    private void DrawMinimalMode(Graphics graphics)
    {
        var centerX = LogicalWidth / 2;
        const float centerY = DefaultWidgetHeight / 2;
        DrawUsageRing(
            graphics,
            _response?.Progress.Weekly,
            new RectangleF(centerX - 102, centerY - 102, 204, 204),
            20F,
            showResetReturn: false);
        DrawUsageRing(
            graphics,
            _response?.Progress.Monthly,
            new RectangleF(centerX - 75, centerY - 75, 150, 150),
            10F,
            showResetReturn: true);

        var weeklyText = _response is null
            ? "--%"
            : FormatUsageValue(_response.Progress.Weekly, showAmount: false);
        var monthlyText = _response is null
            ? "--%"
            : FormatUsageValue(_response.Progress.Monthly, showAmount: false);
        DrawCenteredMinimalText(graphics, weeklyText, centerX, centerY - 18, 16F);
        DrawCenteredMinimalText(graphics, monthlyText, centerX, centerY + 7, 11.5F);
    }

    private static void DrawUsageRing(
        Graphics graphics,
        UsagePeriod? usage,
        RectangleF bounds,
        float thickness,
        bool showResetReturn)
    {
        using var trackPen = new Pen(Color.FromArgb(58, 229, 236, 244), thickness);
        graphics.DrawEllipse(trackPen, bounds);
        if (usage is null) return;

        var hasResetReturn = showResetReturn && usage.ResetReturnAmountUsd > 0M;
        var combinedLimit = usage.LimitUsd + (hasResetReturn ? usage.ResetReturnAmountUsd : 0M);
        if (combinedLimit <= 0M) return;

        var baseRatio = Math.Clamp(usage.UsedUsd / combinedLimit, 0M, 1M);
        var resetRatio = hasResetReturn
            ? Math.Clamp(usage.ResetReturnUsedUsd / combinedLimit, 0M, 1M - baseRatio)
            : 0M;
        DrawRingSegment(
            graphics,
            bounds,
            thickness,
            -90F,
            (float)baseRatio * 360F,
            Color.FromArgb(210, 240, 255, 255));
        DrawRingSegment(
            graphics,
            bounds,
            thickness,
            -90F + (float)baseRatio * 360F,
            (float)resetRatio * 360F,
            Color.FromArgb(225, 163, 182, 193));
    }

    private static void DrawRingSegment(
        Graphics graphics,
        RectangleF bounds,
        float thickness,
        float startAngle,
        float sweepAngle,
        Color color)
    {
        if (sweepAngle <= 0.1F) return;
        using var pen = new Pen(color, thickness)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        if (sweepAngle >= 359.9F) graphics.DrawEllipse(pen, bounds);
        else graphics.DrawArc(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height, startAngle, sweepAngle);
    }

    private static void DrawCenteredMinimalText(
        Graphics graphics,
        string text,
        float centerX,
        float centerY,
        float fontSize)
    {
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold);
        var size = graphics.MeasureString(text, font);
        var x = centerX - size.Width / 2;
        var y = centerY - size.Height / 2;
        using var shadowBrush = new SolidBrush(Color.FromArgb(155, 17, 24, 39));
        using var textBrush = new SolidBrush(Color.FromArgb(245, 248, 250, 252));
        graphics.DrawString(text, font, shadowBrush, x + 1, y + 1);
        graphics.DrawString(text, font, textBrush, x, y);
    }

    private void DrawUsageSection(
        Graphics graphics,
        string label,
        UsagePeriod usage,
        float top,
        TextToggleState valueState,
        TextToggleState resetState,
        bool showResetReturn)
    {
        using var labelFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        using var labelBrush = new SolidBrush(Color.FromArgb(235, 245, 248, 252));
        graphics.DrawString(label, labelFont, labelBrush, 20, top);

        var hasResetReturn = showResetReturn && usage.ResetReturnAmountUsd > 0M;
        if (hasResetReturn)
        {
            using var legendFont = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
            using var legendBrush = new SolidBrush(Color.FromArgb(210, 111, 211, 255));
            using var legendDotBrush = new SolidBrush(Color.FromArgb(72, 187, 255));
            var labelWidth = graphics.MeasureString(label, labelFont).Width;
            graphics.FillEllipse(legendDotBrush, 25 + labelWidth, top + 8, 6, 6);
            graphics.DrawString("重置返额", legendFont, legendBrush, 34 + labelWidth, top + 3.5F);
        }

        var percentageText = FormatUsageValue(usage, valueState.Alternate);
        using var percentFont = new Font("Segoe UI", 10F);
        DrawAnimatedText(
            graphics,
            valueState,
            percentageText,
            percentFont,
            Color.FromArgb(190, 224, 232, 242),
            LogicalWidth - 20,
            top + 1,
            alignRight: true);

        var track = new RectangleF(20, top + 27, Math.Max(1, LogicalWidth - 40), 10);
        DrawUsageBar(graphics, usage, track, showResetReturn);

        using var resetFont = new Font("Microsoft YaHei UI", 8.5F);
        var resetText = FormatResetValue(usage, resetState.Alternate);
        DrawAnimatedText(
            graphics,
            resetState,
            resetText,
            resetFont,
            Color.FromArgb(148, 213, 222, 234),
            20,
            top + 43,
            alignRight: false);
    }

    private void DrawUsageBar(
        Graphics graphics,
        UsagePeriod usage,
        RectangleF track,
        bool showResetReturn)
    {
        var radius = track.Height / 2;
        using var trackBrush = new SolidBrush(Color.FromArgb(58, 229, 236, 244));
        graphics.FillRoundedRectangle(trackBrush, track, radius);

        var hasResetReturn = showResetReturn && usage.ResetReturnAmountUsd > 0M;
        var percentage = Math.Clamp(usage.Percentage, 0M, 100M);
        var combinedLimit = usage.LimitUsd + (hasResetReturn ? usage.ResetReturnAmountUsd : 0M);
        var baseFillRatio = combinedLimit > 0M
            ? Math.Clamp(usage.UsedUsd / combinedLimit, 0M, 1M)
            : 0M;
        var resetFillRatio = combinedLimit > 0M && hasResetReturn
            ? Math.Clamp(usage.ResetReturnUsedUsd / combinedLimit, 0M, 1M - baseFillRatio)
            : 0M;
        var fillWidth = track.Width * (float)baseFillRatio;
        if (fillWidth > 0.5F)
        {
            var fillBounds = new RectangleF(
                track.Left,
                track.Top,
                Math.Max(Math.Min(6F, track.Width), fillWidth),
                track.Height);
            var color = percentage >= 90
                ? Color.FromArgb(255, 82, 88)
                : percentage >= 70
                    ? Color.FromArgb(245, 158, 11)
                    : Color.FromArgb(39, 205, 104);
            using var fillBrush = new SolidBrush(color);
            graphics.FillRoundedRectangle(fillBrush, fillBounds, radius);

            if (percentage >= 90)
            {
                var state = graphics.Save();
                using var clipPath = GraphicsExtensions.CreateRoundedRectangle(fillBounds, radius);
                graphics.SetClip(clipPath);
                using var stripePen = new Pen(Color.FromArgb(100, 255, 235, 235), 5F);
                for (var x = fillBounds.Left - 22 + _stripeOffset; x < fillBounds.Right + 22; x += 22)
                {
                    graphics.DrawLine(
                        stripePen,
                        x,
                        fillBounds.Bottom + 5,
                        x + 18,
                        fillBounds.Top - 5);
                }
                graphics.Restore(state);
            }
        }

        var resetFillWidth = track.Width * (float)resetFillRatio;
        if (resetFillWidth > 0.5F)
        {
            var availableWidth = Math.Max(0F, track.Right - track.Left - fillWidth);
            var resetFillBounds = new RectangleF(
                track.Left + fillWidth,
                track.Top,
                Math.Min(Math.Max(4, resetFillWidth), availableWidth),
                track.Height);
            using var resetFillBrush = new SolidBrush(Color.FromArgb(72, 187, 255));
            graphics.FillRoundedRectangle(resetFillBrush, resetFillBounds, radius);
        }
    }

    private void HandleMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _response is null || _minimalMode) return;
        var point = ToLogical(e.Location);

        if (WeeklyValueBounds.Contains(point.X, point.Y))
        {
            ToggleText(_weeklyValueState, _response.Progress.Weekly, resetText: false);
        }
        else if (MonthlyValueBounds.Contains(point.X, point.Y))
        {
            ToggleText(_monthlyValueState, _response.Progress.Monthly, resetText: false);
        }
        else if (WeeklyResetBounds.Contains(point.X, point.Y))
        {
            ToggleText(_weeklyResetState, _response.Progress.Weekly, resetText: true);
        }
        else if (MonthlyResetBounds.Contains(point.X, point.Y))
        {
            ToggleText(_monthlyResetState, _response.Progress.Monthly, resetText: true);
        }
        else if (RefreshBounds.Contains(point.X, point.Y))
        {
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        UpdateAnimationTimer();
        Invalidate();
    }

    private bool HasCriticalUsage =>
        _response?.Progress.Weekly.Percentage >= 90M ||
        _response?.Progress.Monthly.Percentage >= 90M;

    private bool HasTextAnimation =>
        !_minimalMode && (_weeklyValueState.Animating ||
        _monthlyValueState.Animating ||
        _weeklyResetState.Animating ||
        _monthlyResetState.Animating);

    private static void ResetTextToggle(TextToggleState state)
    {
        state.Alternate = false;
        state.Animating = false;
    }

    private void UpdateAnimationTimer()
    {
        var textAnimation = HasTextAnimation;
        var criticalAnimation = !_minimalMode && HasCriticalUsage;
        _animationTimer.Interval = 33;
        _animationTimer.Enabled = Visible && (textAnimation || criticalAnimation);
    }

    private static void ToggleText(TextToggleState state, UsagePeriod usage, bool resetText)
    {
        var from = resetText
            ? FormatResetValue(usage, state.Alternate)
            : FormatUsageValue(usage, state.Alternate);
        state.Alternate = !state.Alternate;
        var to = resetText
            ? FormatResetValue(usage, state.Alternate)
            : FormatUsageValue(usage, state.Alternate);
        state.Begin(from, to);
    }

    private static string FormatUsageValue(UsagePeriod usage, bool showAmount)
    {
        if (!showAmount)
        {
            var basePercentage = $"{Math.Clamp(usage.Percentage, 0M, 100M):0.#}%";
            if (usage.ResetReturnAmountUsd <= 0M) return basePercentage;

            var resetReturnPercentage = Math.Clamp(
                usage.ResetReturnUsedUsd / usage.ResetReturnAmountUsd * 100M,
                0M,
                100M);
            return $"{basePercentage}(+{resetReturnPercentage:0.#}%)";
        }

        return usage.ResetReturnAmountUsd > 0M
            ? $"{usage.UsedUsd:0.0#}(+{usage.ResetReturnUsedUsd:0.00}) / " +
              $"{usage.LimitUsd:0.0#}(+{usage.ResetReturnAmountUsd:0.00})"
            : $"{usage.UsedUsd:0.00} / {usage.LimitUsd:0.00}";
    }

    private static string FormatResetValue(UsagePeriod usage, bool showRelative)
    {
        if (usage.ResetsAt == default) return "重置时间暂不可用";
        if (!showRelative) return $"{usage.ResetsAt.LocalDateTime:M月d日 HH:mm} 重置";

        var remaining = usage.ResetsAt - DateTimeOffset.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return $"{(int)remaining.TotalDays}天{remaining.Hours}时 后重置";
    }

    private static void DrawAnimatedText(
        Graphics graphics,
        TextToggleState state,
        string currentText,
        Font font,
        Color color,
        float anchorX,
        float y,
        bool alignRight)
    {
        if (!state.Animating)
        {
            DrawText(graphics, currentText, font, color, anchorX, y, alignRight, 1F);
            return;
        }

        var progress = Math.Clamp((Environment.TickCount64 - state.StartedAt) / 320F, 0F, 1F);
        if (progress >= 1F)
        {
            state.Animating = false;
            DrawText(graphics, currentText, font, color, anchorX, y, alignRight, 1F);
        }
        else if (progress < 0.5F)
        {
            DrawText(graphics, state.FromText, font, color, anchorX, y, alignRight, 1F - progress * 2F);
        }
        else
        {
            DrawText(graphics, state.ToText, font, color, anchorX, y, alignRight, (progress - 0.5F) * 2F);
        }
    }

    private static void DrawText(
        Graphics graphics,
        string text,
        Font font,
        Color color,
        float anchorX,
        float y,
        bool alignRight,
        float opacity)
    {
        var alpha = Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
        using var brush = new SolidBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        var x = alignRight ? anchorX - graphics.MeasureString(text, font).Width : anchorX;
        graphics.DrawString(text, font, brush, x, y);
    }

    private bool IsInteractivePoint(PointF point) =>
        !_minimalMode && (WeeklyValueBounds.Contains(point.X, point.Y) ||
        MonthlyValueBounds.Contains(point.X, point.Y) ||
        WeeklyResetBounds.Contains(point.X, point.Y) ||
        MonthlyResetBounds.Contains(point.X, point.Y) ||
        RefreshBounds.Contains(point.X, point.Y));

    private PointF ToLogical(Point point) => new(point.X / UiScale, point.Y / UiScale);

    private static string FormatPlanName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Codex";
        var trimmed = value.Trim();
        var firstCjk = -1;
        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            if (character is >= '\u3400' and <= '\u9fff')
            {
                firstCjk = index;
                break;
            }
        }

        var result = firstCjk > 0 ? trimmed[..firstCjk].Trim() : trimmed;
        return result.Length > 28 ? result[..28].TrimEnd() + "…" : result;
    }

    private void UpdateRoundedRegion()
    {
        var physicalRadius = CornerRadius * UiScale;
        using (var path = CreateWindowShapePath(
                   new RectangleF(0, 0, Width, Height),
                   physicalRadius))
        {
            var managedRegion = new Region(path);
            Region?.Dispose();
            Region = managedRegion;
        }

        var nativeDiameter = (int)Math.Round(physicalRadius * 2);
        var nativeRegion = _minimalMode
            ? CreateEllipticRgn(0, 0, Width + 1, Height + 1)
            : CreateRoundRectRgn(0, 0, Width + 1, Height + 1, nativeDiameter, nativeDiameter);
        if (nativeRegion == IntPtr.Zero) return;

        AcrylicWindow.TrySetBlurRegion(
            Handle,
            nativeRegion,
            _acrylicApplied && _blurStrength > 0);
        if (SetWindowRgn(Handle, nativeRegion, true) == 0)
        {
            DeleteObject(nativeRegion);
        }
    }

    private GraphicsPath CreateWindowShapePath(RectangleF bounds, float radius)
    {
        if (!_minimalMode) return GraphicsExtensions.CreateRoundedRectangle(bounds, radius);

        var path = new GraphicsPath();
        path.AddEllipse(bounds);
        return path;
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || IsInteractivePoint(ToLogical(e.Location))) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0);
    }

    protected override void WndProc(ref Message message)
    {
        if (_minimalMode && message.Msg == WindowMessageSizing)
        {
            base.WndProc(ref message);
            message.Result = (IntPtr)1;
            return;
        }

        if (message.Msg == WindowMessageEnterSizeMove)
        {
            _resizeLogicalWidth = LogicalWidth;
            _resizeAspectRatio = ClientSize.Width / (float)ClientSize.Height;
            base.WndProc(ref message);
            return;
        }

        if (message.Msg == WindowMessageSizing)
        {
            base.WndProc(ref message);
            ApplySizingConstraint(message.WParam.ToInt32(), message.LParam);
            message.Result = (IntPtr)1;
            return;
        }

        if (message.Msg == WindowMessageExitSizeMove)
        {
            _lastResizeHit = HitTestClient;
            base.WndProc(ref message);
            return;
        }

        base.WndProc(ref message);
        if (message.Msg != WindowMessageNcHitTest ||
            message.Result != (IntPtr)HitTestClient ||
            WindowState != FormWindowState.Normal ||
            _minimalMode)
        {
            return;
        }

        var point = PointToClient(Cursor.Position);
        var left = point.X <= ResizeBorderWidth;
        var right = point.X >= ClientSize.Width - ResizeBorderWidth;
        var top = point.Y <= ResizeBorderWidth;
        var bottom = point.Y >= ClientSize.Height - ResizeBorderWidth;
        var nearLeftCorner = point.X <= CornerResizeZone;
        var nearRightCorner = point.X >= ClientSize.Width - CornerResizeZone;
        _lastResizeHit = nearLeftCorner && top
            ? HitTestTopLeft
            : nearRightCorner && top
                ? HitTestTopRight
                : nearLeftCorner && bottom
                    ? HitTestBottomLeft
                    : nearRightCorner && bottom
                        ? HitTestBottomRight
                        : left
                            ? HitTestLeft
                            : right
                                ? HitTestRight
                                : top
                                    ? HitTestTop
                                    : bottom
                                        ? HitTestBottom
                                        : HitTestClient;
        if (_lastResizeHit != HitTestClient)
        {
            message.Result = (IntPtr)_lastResizeHit;
        }
    }

    private void ApplySizingConstraint(int sizingEdge, IntPtr rectanglePointer)
    {
        if (rectanglePointer == IntPtr.Zero) return;
        var rectangle = Marshal.PtrToStructure<NativeRectangle>(rectanglePointer);
        if (sizingEdge is SizingLeft or SizingRight)
        {
            var scale = UiScale;
            var width = Math.Clamp(
                rectangle.Right - rectangle.Left,
                (int)Math.Round(MinimumWidgetWidth * scale),
                (int)Math.Round(MaximumWidgetWidth * scale));
            if (sizingEdge == SizingLeft) rectangle.Left = rectangle.Right - width;
            else rectangle.Right = rectangle.Left + width;
        }
        else if (sizingEdge is SizingTopLeft or SizingTopRight or SizingBottomLeft or SizingBottomRight)
        {
            var width = Math.Clamp(
                rectangle.Right - rectangle.Left,
                (int)Math.Round(_resizeLogicalWidth * MinimumWidgetScale),
                (int)Math.Round(_resizeLogicalWidth * MaximumWidgetScale));
            var height = (int)Math.Round(width / _resizeAspectRatio);
            switch (sizingEdge)
            {
                case SizingTopLeft:
                    rectangle.Left = rectangle.Right - width;
                    rectangle.Top = rectangle.Bottom - height;
                    break;
                case SizingTopRight:
                    rectangle.Right = rectangle.Left + width;
                    rectangle.Top = rectangle.Bottom - height;
                    break;
                case SizingBottomLeft:
                    rectangle.Left = rectangle.Right - width;
                    rectangle.Bottom = rectangle.Top + height;
                    break;
                case SizingBottomRight:
                    rectangle.Right = rectangle.Left + width;
                    rectangle.Bottom = rectangle.Top + height;
                    break;
            }
        }
        else if (sizingEdge is SizingTop or SizingBottom)
        {
            var height = Math.Clamp(
                rectangle.Bottom - rectangle.Top,
                (int)Math.Round(DefaultWidgetHeight * MinimumWidgetScale),
                (int)Math.Round(DefaultWidgetHeight * MaximumWidgetScale));
            var width = (int)Math.Round(height * _resizeAspectRatio);
            var centerX = (rectangle.Left + rectangle.Right) / 2;
            rectangle.Left = centerX - width / 2;
            rectangle.Right = rectangle.Left + width;
            if (sizingEdge == SizingTop) rectangle.Top = rectangle.Bottom - height;
            else rectangle.Bottom = rectangle.Top + height;
        }

        Marshal.StructureToPtr(rectangle, rectanglePointer, false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animationTimer.Dispose();
        base.Dispose(disposing);
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
    private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr graphicsObject);
}

internal sealed class TextToggleState
{
    public bool Alternate { get; set; }
    public bool Animating { get; set; }
    public string FromText { get; private set; } = "";
    public string ToText { get; private set; } = "";
    public long StartedAt { get; private set; }

    public void Begin(string fromText, string toText)
    {
        FromText = fromText;
        ToText = toText;
        StartedAt = Environment.TickCount64;
        Animating = true;
    }
}

internal static class GraphicsExtensions
{
    public static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRoundedRectangle(
        this Graphics graphics,
        Brush brush,
        RectangleF bounds,
        float radius)
    {
        using var path = CreateRoundedRectangle(bounds, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(
        this Graphics graphics,
        Pen pen,
        RectangleF bounds,
        float radius)
    {
        using var path = CreateRoundedRectangle(bounds, radius);
        graphics.DrawPath(pen, path);
    }
}
