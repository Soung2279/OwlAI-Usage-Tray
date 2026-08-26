namespace OwlUsageTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var compatibilityIndex = Array.FindIndex(
            args,
            argument => argument.Equals("--compat-report", StringComparison.OrdinalIgnoreCase));
        if (compatibilityIndex >= 0 && compatibilityIndex + 1 < args.Length)
        {
            return WriteCompatibilityReport(args[compatibilityIndex + 1]);
        }

        var previewIndex = Array.FindIndex(
            args,
            argument => argument.Equals("--render-preview", StringComparison.OrdinalIgnoreCase));
        if (previewIndex >= 0 && previewIndex + 1 < args.Length)
        {
            var previewWidth = previewIndex + 2 < args.Length &&
                               int.TryParse(args[previewIndex + 2], out var requestedWidth)
                ? requestedWidth
                : WidgetForm.DefaultWidgetWidth;
            var previewHeight = previewIndex + 3 < args.Length &&
                                int.TryParse(args[previewIndex + 3], out var requestedHeight)
                ? requestedHeight
                : WidgetForm.DefaultWidgetHeight;
            var minimalMode = previewIndex + 4 < args.Length &&
                              args[previewIndex + 4].Equals(
                                  "minimal",
                                  StringComparison.OrdinalIgnoreCase);
            return RenderPreview(args[previewIndex + 1], previewWidth, previewHeight, minimalMode);
        }

        var settingsPreviewIndex = Array.FindIndex(
            args,
            argument => argument.Equals("--render-settings-preview", StringComparison.OrdinalIgnoreCase));
        if (settingsPreviewIndex >= 0 && settingsPreviewIndex + 1 < args.Length)
        {
            return RenderSettingsPreview(args[settingsPreviewIndex + 1]);
        }

        Application.Run(new TrayApplicationContext());
        return 0;
    }

    private static int WriteCompatibilityReport(string outputPath)
    {
        try
        {
            var report = AcrylicWindow.GetCompatibilityReport();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            File.WriteAllText(
                outputPath,
                System.Text.Json.JsonSerializer.Serialize(
                    report,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int RenderPreview(string outputPath, int width, int height, bool minimalMode)
    {
        try
        {
            using var widget = new WidgetForm
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-2000, -2000)
            };
            widget.UpdateUsage(
                new ProgressResponse
                {
                    Progress = new UsageProgress
                    {
                        GroupName = "Codex Mini New 迷你套餐",
                        ExpiresAt = DateTimeOffset.Now.AddDays(11),
                        ExpiresInDays = 11,
                        Weekly = new UsagePeriod { UsedUsd = 18.95m, LimitUsd = 115m, RemainingUsd = 96.05m, Percentage = 16.48m, ResetsAt = DateTimeOffset.Now.AddDays(5) },
                        Monthly = new UsagePeriod
                        {
                            UsedUsd = 450m,
                            LimitUsd = 450m,
                            RemainingUsd = 0m,
                            Percentage = 100m,
                            ResetReturnUsedUsd = 39.84m,
                            ResetReturnAmountUsd = 164.64m,
                            ResetsAt = DateTimeOffset.Now.AddDays(10)
                        }
                    }
                },
                DateTimeOffset.Now);
            widget.ApplySavedSize(width, height);
            widget.ApplyMinimalMode(minimalMode);
            widget.SetPreviewMode();
            widget.ApplyAlwaysOnTop(alwaysOnTop: true);
            widget.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(widget.ClientSize.Width, widget.ClientSize.Height);
            widget.DrawToBitmap(bitmap, widget.ClientRectangle);
            ClearRoundedCorners(
                bitmap,
                (int)Math.Round(24D * widget.ClientSize.Height / WidgetForm.DefaultWidgetHeight));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            widget.Hide();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static void ClearRoundedCorners(Bitmap bitmap, int radius)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var inMiddleBand = x >= radius && x < bitmap.Width - radius ||
                                   y >= radius && y < bitmap.Height - radius;
                if (inMiddleBand) continue;

                var centerX = x < radius ? radius : bitmap.Width - radius - 1;
                var centerY = y < radius ? radius : bitmap.Height - radius - 1;
                var deltaX = x - centerX;
                var deltaY = y - centerY;
                if (deltaX * deltaX + deltaY * deltaY > radius * radius)
                {
                    bitmap.SetPixel(x, y, Color.Transparent);
                }
            }
        }
    }

    private static int RenderSettingsPreview(string outputPath)
    {
        try
        {
            using var settings = new SettingsForm(new AppSettings())
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-2000, -2000)
            };
            settings.SetPreviewMode();
            settings.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(settings.ClientSize.Width, settings.ClientSize.Height);
            settings.DrawToBitmap(bitmap, settings.ClientRectangle);
            ClearRoundedCorners(bitmap, 24);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            settings.Hide();
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
