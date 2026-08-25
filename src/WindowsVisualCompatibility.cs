using System.Runtime.InteropServices;

namespace OwlUsageTray;

internal sealed record VisualCompatibilityReport
{
    public string WindowsVersion { get; init; } = "";
    public bool IsWindows10 { get; init; }
    public bool IsWindows11OrLater { get; init; }
    public bool DesktopCompositionEnabled { get; init; }
    public bool NativeAccentAvailable { get; init; }
    public bool HighContrastEnabled { get; init; }
    public string PreferredBackdrop { get; init; } = "";
    public string FallbackChain { get; init; } = "";
}

internal static class AcrylicWindow
{
    private const int WindowCompositionAttributeAccentPolicy = 19;
    private const int AccentEnableTransparentGradient = 2;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const uint DwmBlurBehindEnable = 0x1;
    private const uint DwmBlurBehindRegion = 0x2;

    private static readonly IntPtr User32Module;
    private static readonly SetWindowCompositionAttributeDelegate? SetAccentPolicy;

    static AcrylicWindow()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (!NativeLibrary.TryLoad("user32.dll", out User32Module) ||
                !NativeLibrary.TryGetExport(
                    User32Module,
                    "SetWindowCompositionAttribute",
                    out var procedure))
            {
                return;
            }

            SetAccentPolicy = Marshal.GetDelegateForFunctionPointer<SetWindowCompositionAttributeDelegate>(procedure);
        }
        catch
        {
            SetAccentPolicy = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Enable;

        public IntPtr BlurRegion;

        [MarshalAs(UnmanagedType.Bool)]
        public bool TransitionOnMaximized;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetWindowCompositionAttributeDelegate(
        IntPtr window,
        ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmIsCompositionEnabled(
        [MarshalAs(UnmanagedType.Bool)] out bool enabled);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmEnableBlurBehindWindow(
        IntPtr window,
        ref DwmBlurBehind blurBehind);

    public static bool TryEnable(IntPtr window, int opacityPercent, int blurStrength)
    {
        if (window == IntPtr.Zero ||
            !OperatingSystem.IsWindowsVersionAtLeast(10) ||
            SystemInformation.HighContrast ||
            SetAccentPolicy is null ||
            !IsCompositionEnabled())
        {
            return false;
        }

        var safeOpacity = Math.Clamp(opacityPercent, 20, 95);
        var safeBlur = Math.Clamp(blurStrength, 0, 100);
        var alpha = (int)Math.Round(safeOpacity / 100D * 255D);
        var tint = unchecked((int)((uint)alpha << 24 | 0x00605045U));

        // Acrylic state 4 arrived in Windows 10 1803. On older Windows 10
        // builds, or when a display/driver rejects it, progressively fall back
        // to BlurBehind and finally a transparent tint. The form's GDI paint
        // remains the last fallback when every composition mode fails.
        if (safeBlur >= 50 && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134) &&
            TryApply(window, AccentEnableAcrylicBlurBehind, tint))
        {
            return true;
        }

        if (safeBlur > 0 && TryApply(window, AccentEnableBlurBehind, tint))
        {
            return true;
        }

        return TryApply(window, AccentEnableTransparentGradient, tint);
    }

    public static VisualCompatibilityReport GetCompatibilityReport()
    {
        var windows10 = OperatingSystem.IsWindowsVersionAtLeast(10) &&
                        !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        var windows11 = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        var composition = IsCompositionEnabled();
        var highContrast = SystemInformation.HighContrast;
        var accentAvailable = SetAccentPolicy is not null;
        var acrylicAvailable = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134) &&
                               composition && accentAvailable && !highContrast;

        return new VisualCompatibilityReport
        {
            WindowsVersion = Environment.OSVersion.Version.ToString(),
            IsWindows10 = windows10,
            IsWindows11OrLater = windows11,
            DesktopCompositionEnabled = composition,
            NativeAccentAvailable = accentAvailable,
            HighContrastEnabled = highContrast,
            PreferredBackdrop = acrylicAvailable ? "Acrylic" : accentAvailable && composition ? "BlurBehind" : "GDI gradient",
            FallbackChain = "Acrylic -> BlurBehind -> transparent tint -> GDI gradient"
        };
    }

    public static bool TrySetBlurRegion(IntPtr window, IntPtr region, bool enabled)
    {
        if (window == IntPtr.Zero ||
            !OperatingSystem.IsWindowsVersionAtLeast(6) ||
            !IsCompositionEnabled())
        {
            return false;
        }

        var blurBehind = new DwmBlurBehind
        {
            Flags = enabled
                ? DwmBlurBehindEnable | DwmBlurBehindRegion
                : DwmBlurBehindEnable,
            Enable = enabled,
            BlurRegion = enabled ? region : IntPtr.Zero,
            TransitionOnMaximized = false
        };

        try
        {
            return DwmEnableBlurBehindWindow(window, ref blurBehind) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryApply(IntPtr window, int accentState, int tint)
    {
        if (SetAccentPolicy is null) return false;

        var accent = new AccentPolicy
        {
            AccentState = accentState,
            AccentFlags = 0,
            GradientColor = tint,
            AnimationId = 0
        };
        var accentSize = Marshal.SizeOf<AccentPolicy>();
        var accentPointer = Marshal.AllocHGlobal(accentSize);
        try
        {
            Marshal.StructureToPtr(accent, accentPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttributeAccentPolicy,
                Data = accentPointer,
                SizeOfData = accentSize
            };
            return SetAccentPolicy(window, ref data) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(accentPointer);
        }
    }

    private static bool IsCompositionEnabled()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6)) return false;
        try
        {
            return DwmIsCompositionEnabled(out var enabled) == 0 && enabled;
        }
        catch
        {
            return false;
        }
    }
}
