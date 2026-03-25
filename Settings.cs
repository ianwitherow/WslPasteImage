using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WslPasteImage;

public class Settings
{
    public string ImageSavePath { get; set; } = @"C:\Temp\WslScreenshots";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Keys HotkeyKey { get; set; } = Keys.V;

    public bool HotkeyAlt { get; set; } = true;
    public bool HotkeyCtrl { get; set; } = false;
    public bool HotkeyShift { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool WindowsTerminalOnly { get; set; } = false;

    private static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WslPasteImage");

    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch
        {
            // Fall through to defaults
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
        ApplyStartWithWindows();
    }

    public uint GetWin32Modifiers()
    {
        uint mods = NativeMethods.MOD_NOREPEAT;
        if (HotkeyAlt) mods |= NativeMethods.MOD_ALT;
        if (HotkeyCtrl) mods |= NativeMethods.MOD_CONTROL;
        if (HotkeyShift) mods |= NativeMethods.MOD_SHIFT;
        return mods;
    }

    public string FormatHotkey()
    {
        var parts = new List<string>();
        if (HotkeyCtrl) parts.Add("Ctrl");
        if (HotkeyAlt) parts.Add("Alt");
        if (HotkeyShift) parts.Add("Shift");
        parts.Add(HotkeyKey.ToString());
        return string.Join(" + ", parts);
    }

    public string ToWslPath(string windowsPath)
    {
        // C:\Temp\Screenshots\file.png -> /mnt/c/Temp/Screenshots/file.png
        if (windowsPath.Length >= 2 && windowsPath[1] == ':')
        {
            var driveLetter = char.ToLower(windowsPath[0]);
            var rest = windowsPath[2..].Replace('\\', '/');
            return $"/mnt/{driveLetter}{rest}";
        }
        return windowsPath.Replace('\\', '/');
    }

    private void ApplyStartWithWindows()
    {
        const string keyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "WslPasteImage";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyName, true);
            if (key == null) return;

            if (StartWithWindows)
            {
                var exePath = Application.ExecutablePath;
                key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, false);
            }
        }
        catch
        {
            // Registry access may fail
        }
    }
}
