using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OwlUsageTray;

internal sealed class SessionStore
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OwlUsageTray");

    private string SessionPath => Path.Combine(_directory, "session.dat");

    public StoredSession? Load()
    {
        try
        {
            if (!File.Exists(SessionPath)) return null;
            var protectedBytes = Convert.FromBase64String(File.ReadAllText(SessionPath));
            var json = Encoding.UTF8.GetString(Dpapi.Unprotect(protectedBytes));
            return JsonSerializer.Deserialize<StoredSession>(json);
        }
        catch
        {
            Delete();
            return null;
        }
    }

    public void Save(StoredSession session)
    {
        Directory.CreateDirectory(_directory);
        var json = JsonSerializer.Serialize(session);
        var protectedBytes = Dpapi.Protect(Encoding.UTF8.GetBytes(json));
        File.WriteAllText(SessionPath, Convert.ToBase64String(protectedBytes));
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(SessionPath)) File.Delete(SessionPath);
        }
        catch
        {
            // A locked file should not prevent the app from signing out in memory.
        }
    }
}

internal sealed class SettingsStore
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OwlUsageTray");

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            // Older versions stored the login email in plaintext settings.
            // Rewrite once without that legacy field during upgrade.
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("Email", out _)) Save(settings);

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal static class Dpapi
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Protect(byte[] data) => Transform(data, protect: true);
    public static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new DataBlob
        {
            Size = data.Length,
            Data = Marshal.AllocHGlobal(data.Length)
        };
        Marshal.Copy(data, 0, input.Data, data.Length);

        try
        {
            DataBlob output;
            var success = protect
                ? CryptProtectData(
                    ref input,
                    "OwlUsageTray session",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output)
                : CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);

            if (!success)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
        }
    }
}
