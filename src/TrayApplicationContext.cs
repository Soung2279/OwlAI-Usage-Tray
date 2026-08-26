using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace OwlUsageTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SessionStore _sessionStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly OwlApiClient _apiClient;
    private readonly AppSettings _settings;
    private readonly WidgetForm _widget = new();
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private readonly ToolStripMenuItem _minimalModeItem;
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _startupTimer = new();
    private SettingsForm? _settingsForm;
    private bool _refreshing;
    private bool _exiting;
    private bool _suppressAppearanceToggle;
    private Icon? _dynamicIcon;

    public TrayApplicationContext()
    {
        _settings = _settingsStore.Load();
        _apiClient = new OwlApiClient(_sessionStore);

        _alwaysOnTopItem = new ToolStripMenuItem("置顶")
        {
            Checked = _settings.AlwaysOnTop,
            CheckOnClick = true
        };
        _alwaysOnTopItem.CheckedChanged += (_, _) =>
        {
            if (!_suppressAppearanceToggle) SetAlwaysOnTop(_alwaysOnTopItem.Checked);
        };

        _minimalModeItem = new ToolStripMenuItem("极简模式")
        {
            Checked = _settings.MinimalMode,
            CheckOnClick = true
        };
        _minimalModeItem.CheckedChanged += (_, _) =>
        {
            if (!_suppressAppearanceToggle) SetMinimalMode(_minimalModeItem.Checked);
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_alwaysOnTopItem);
        menu.Items.Add(_minimalModeItem);
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add("重新登录", null, async (_, _) => await ReloginAsync());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        _widget.ContextMenuStrip = menu;

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "OwlAI 用量监控",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleWidget(!_widget.Visible);
        };

        _widget.ApplyAlwaysOnTop(_settings.AlwaysOnTop);
        _widget.ApplyMinimalMode(_settings.MinimalMode);
        _widget.ApplySavedSize(_settings.WidgetWidth, _settings.WidgetHeight);
        _widget.ApplyAcrylicSettings(
            _settings.AcrylicOpacityPercent,
            _settings.BlurStrength);
        _widget.ResizeEnd += (_, _) => SaveWidgetBounds();
        _widget.RefreshRequested += async (_, _) =>
            await RefreshUsageAsync(showErrors: true);

        _refreshTimer.Interval = Math.Clamp(_settings.RefreshSeconds, 10, 300) * 1000;
        _refreshTimer.Tick += async (_, _) => await RefreshUsageAsync(showErrors: false);

        _startupTimer.Interval = 150;
        _startupTimer.Tick += async (_, _) =>
        {
            _startupTimer.Stop();
            await InitializeAsync();
        };
        _startupTimer.Start();
    }

    private async Task InitializeAsync()
    {
        if (!_apiClient.HasSession && !ShowLogin())
        {
            ExitApplication();
            return;
        }

        RestoreWidgetLocation();
        if (_settings.WidgetVisible) ToggleWidget(true);
        _refreshTimer.Start();
        await RefreshUsageAsync(showErrors: true);
    }

    private bool ShowLogin()
    {
        using var loginForm = new LoginForm(_apiClient);
        var result = loginForm.ShowDialog();
        return result == DialogResult.OK;
    }

    private async Task RefreshUsageAsync(bool showErrors)
    {
        if (_refreshing || _exiting) return;
        _refreshing = true;
        if (_widget.Visible) _widget.SetLoading();

        try
        {
            var progress = await _apiClient.GetProgressAsync();
            _widget.UpdateUsage(progress, DateTimeOffset.Now);
            UpdateTray(progress.Progress);
        }
        catch (AuthenticationRequiredException)
        {
            _refreshTimer.Stop();
            if (ShowLogin())
            {
                _refreshTimer.Start();
                await RefreshUsageAfterCurrentAsync();
            }
            else if (showErrors)
            {
                _widget.ShowError("登录已失效，请从托盘菜单选择“重新登录”。");
            }
        }
        catch (Exception exception)
        {
            var message = exception is HttpRequestException
                ? "网络或服务器暂时不可用。"
                : exception.Message;
            _widget.ShowError(message);
            _trayIcon.Text = TrimToolTip($"OwlAI：刷新失败 · {message}");
            if (showErrors && !_widget.Visible)
            {
                _trayIcon.ShowBalloonTip(2500, "OwlAI 用量", message, ToolTipIcon.Warning);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshUsageAfterCurrentAsync()
    {
        _refreshing = false;
        await RefreshUsageAsync(showErrors: true);
    }

    private void UpdateTray(UsageProgress progress)
    {
        var monthly = progress.Monthly;
        var text = $"月 {monthly.UsedUsd:0.00}/{monthly.LimitUsd:0.00} · 剩 {monthly.RemainingUsd:0.00} ({monthly.Percentage:0.0}%)";
        _trayIcon.Text = TrimToolTip(text);

        var newIcon = CreateUsageIcon((double)monthly.Percentage);
        var oldIcon = _dynamicIcon;
        _dynamicIcon = newIcon;
        _trayIcon.Icon = newIcon;
        oldIcon?.Dispose();
    }

    private void ToggleWidget(bool show)
    {
        if (show)
        {
            if (_settings.WidgetX is not null && _settings.WidgetY is not null)
            {
                _widget.Location = new Point(_settings.WidgetX.Value, _settings.WidgetY.Value);
                _widget.Show();
                _widget.BringToFront();
            }
            else
            {
                _widget.ShowNearTray();
            }
        }
        else
        {
            SaveWidgetBounds();
            _widget.Hide();
        }

        _settings.WidgetVisible = show;
        _settingsStore.Save(_settings);
    }

    private void SetAlwaysOnTop(bool enabled)
    {
        _settings.AlwaysOnTop = enabled;
        SyncAppearanceMenu();
        _widget.ApplyAlwaysOnTop(_settings.AlwaysOnTop);
        _settingsStore.Save(_settings);
    }

    private void SetMinimalMode(bool enabled)
    {
        var center = new Point(
            _widget.Left + _widget.Width / 2,
            _widget.Top + _widget.Height / 2);
        _settings.MinimalMode = enabled;
        SyncAppearanceMenu();
        _widget.ApplyMinimalMode(enabled);
        var area = Screen.FromPoint(center).WorkingArea;
        _widget.Location = new Point(
            Math.Clamp(
                center.X - _widget.Width / 2,
                area.Left,
                Math.Max(area.Left, area.Right - _widget.Width)),
            Math.Clamp(
                center.Y - _widget.Height / 2,
                area.Top,
                Math.Max(area.Top, area.Bottom - _widget.Height)));
        _settings.WidgetX = _widget.Left;
        _settings.WidgetY = _widget.Top;
        _settingsStore.Save(_settings);
    }

    private void SyncAppearanceMenu()
    {
        _suppressAppearanceToggle = true;
        _alwaysOnTopItem.Checked = _settings.AlwaysOnTop;
        _minimalModeItem.Checked = _settings.MinimalMode;
        _suppressAppearanceToggle = false;
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (!_settingsForm.Visible) _settingsForm.Show();
            _settingsForm.BringToFront();
            _settingsForm.Activate();
            return;
        }

        var form = new SettingsForm(_settings);
        _settingsForm = form;
        form.SettingsChanged += (_, args) => ApplySettings(args);
        form.ReloginRequested += async (_, _) =>
        {
            form.Close();
            await ReloginAsync();
        };
        form.OpenWebRequested += (_, _) => OpenWebPage();
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_settingsForm, form)) _settingsForm = null;
            form.Dispose();
        };
        form.Show();
        form.Activate();
    }

    private void ApplySettings(SettingsChangedEventArgs args)
    {
        _settings.AcrylicOpacityPercent = Math.Clamp(args.AcrylicOpacityPercent, 20, 95);
        _settings.BlurStrength = Math.Clamp(args.BlurStrength, 0, 100);
        _settings.RefreshSeconds = Math.Clamp(args.RefreshSeconds, 10, 300);
        _widget.ApplyAcrylicSettings(
            _settings.AcrylicOpacityPercent,
            _settings.BlurStrength);
        if (args.ResetWidgetSize)
        {
            ResetWidgetSize();
        }
        _refreshTimer.Interval = _settings.RefreshSeconds * 1000;
        _settingsStore.Save(_settings);
    }

    private void ResetWidgetSize()
    {
        _widget.ApplySavedSize(
            WidgetForm.DefaultWidgetWidth,
            WidgetForm.DefaultWidgetHeight);
        var area = Screen.FromRectangle(new Rectangle(_widget.Location, _widget.Size)).WorkingArea;
        var x = Math.Clamp(
            _widget.Left,
            area.Left,
            Math.Max(area.Left, area.Right - _widget.Width));
        var y = Math.Clamp(
            _widget.Top,
            area.Top,
            Math.Max(area.Top, area.Bottom - _widget.Height));
        _widget.Location = new Point(x, y);
        _settings.WidgetX = x;
        _settings.WidgetY = y;
        _settings.WidgetWidth = WidgetForm.DefaultWidgetWidth;
        _settings.WidgetHeight = WidgetForm.DefaultWidgetHeight;
    }

    private async Task ReloginAsync()
    {
        _apiClient.ClearSession();
        _refreshTimer.Stop();
        if (ShowLogin())
        {
            _refreshTimer.Start();
            await RefreshUsageAsync(showErrors: true);
        }
    }

    private void RestoreWidgetLocation()
    {
        if (_settings.WidgetX is null || _settings.WidgetY is null) return;
        var point = new Point(_settings.WidgetX.Value, _settings.WidgetY.Value);
        var bounds = new Rectangle(point, _widget.Size);
        if (Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(bounds)))
        {
            _widget.StartPosition = FormStartPosition.Manual;
            _widget.Location = point;
        }
        else
        {
            _settings.WidgetX = null;
            _settings.WidgetY = null;
        }
    }

    private void SaveWidgetBounds()
    {
        if (!_widget.Visible) return;
        _settings.WidgetX = _widget.Left;
        _settings.WidgetY = _widget.Top;
        if (!_widget.MinimalMode)
        {
            _settings.WidgetWidth = Math.Clamp(
                _widget.Width,
                (int)Math.Round(WidgetForm.MinimumWidgetWidth * WidgetForm.MinimumWidgetScale),
                (int)Math.Round(WidgetForm.MaximumWidgetWidth * WidgetForm.MaximumWidgetScale));
            _settings.WidgetHeight = Math.Clamp(
                _widget.Height,
                (int)Math.Round(WidgetForm.DefaultWidgetHeight * WidgetForm.MinimumWidgetScale),
                (int)Math.Round(WidgetForm.DefaultWidgetHeight * WidgetForm.MaximumWidgetScale));
        }
        _settingsStore.Save(_settings);
    }

    private static void OpenWebPage()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://api.owlai.tech/subscriptions",
            UseShellExecute = true
        });
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        SaveWidgetBounds();
        _refreshTimer.Stop();
        _startupTimer.Stop();
        _settingsForm?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _dynamicIcon?.Dispose();
        _widget.Dispose();
        _apiClient.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_exiting) ExitApplication();
        base.Dispose(disposing);
    }

    private static string TrimToolTip(string value) =>
        value.Length <= 63 ? value : value[..60] + "…";

    private static Icon CreateUsageIcon(double percentage)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        var color = percentage >= 90
            ? Color.FromArgb(239, 68, 68)
            : percentage >= 70
                ? Color.FromArgb(245, 158, 11)
                : Color.FromArgb(34, 197, 94);
        using var background = new SolidBrush(Color.FromArgb(30, 41, 59));
        graphics.FillEllipse(background, 0, 0, 32, 32);

        var sweep = (float)(Math.Clamp(percentage, 0, 100) / 100D * 360D);
        using var pen = new Pen(color, 5F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        if (sweep > 0) graphics.DrawArc(pen, 2.5F, 2.5F, 27F, 27F, -90, sweep);

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
