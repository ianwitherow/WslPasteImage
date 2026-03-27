using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace WslPasteImage;

public class App : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private Settings _settings;
    private readonly List<string> _savedFiles = new();
    private readonly SynchronizationContext _syncContext;

    // Low-level keyboard hook — must be stored as a field to prevent GC.
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private IntPtr _hookId;
    private bool _hotkeyHandled;

    public App()
    {
        // Ensure a WinForms sync context exists before the message loop starts.
        WindowsFormsSynchronizationContext.AutoInstall = true;
        if (SynchronizationContext.Current == null)
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        _syncContext = SynchronizationContext.Current!;

        _settings = Settings.Load();

        Directory.CreateDirectory(_settings.ImageSavePath);
        CleanupOldFiles();

        _trayIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = $"WSL Paste Image ({_settings.FormatHotkey()})",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };
        _trayIcon.DoubleClick += (_, _) => OpenSettings();

        _hookProc = HookCallback;
        _hookId = InstallHook();

        if (_hookId == IntPtr.Zero)
        {
            _trayIcon.ShowBalloonTip(3000, "WSL Paste Image",
                "Failed to install keyboard hook.",
                ToolTipIcon.Error);
        }
        else
        {
            _trayIcon.ShowBalloonTip(2000, "WSL Paste Image",
                $"Running. Press {_settings.FormatHotkey()} to paste clipboard image as WSL path.",
                ToolTipIcon.Info);
        }
    }

    private IntPtr InstallHook()
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        return NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            NativeMethods.GetModuleHandle(module.ModuleName),
            0);
    }

    private void RemoveHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = wParam.ToInt32();
                var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var vk = (Keys)hookStruct.vkCode;

                if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    if (!_hotkeyHandled && vk == _settings.HotkeyKey && ModifiersMatch())
                    {
                        _hotkeyHandled = true;
                        _syncContext.Post(_ => OnHotkeyTriggered(), null);
                        return (IntPtr)1;
                    }
                }
                else if (msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    if (vk == _settings.HotkeyKey)
                    {
                        _hotkeyHandled = false;
                    }
                }
            }
        }
        catch { }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool ModifiersMatch()
    {
        return NativeMethods.IsKeyDown(Keys.Menu) == _settings.HotkeyAlt
            && NativeMethods.IsKeyDown(Keys.ControlKey) == _settings.HotkeyCtrl
            && NativeMethods.IsKeyDown(Keys.ShiftKey) == _settings.HotkeyShift;
    }

    private void OnHotkeyTriggered()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                _trayIcon.ShowBalloonTip(1500, "WSL Paste Image",
                    "No image found on clipboard.", ToolTipIcon.Info);
                return;
            }

            var image = Clipboard.GetImage();
            if (image == null) return;

            Directory.CreateDirectory(_settings.ImageSavePath);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = $"screenshot_{timestamp}.png";
            var filePath = Path.Combine(_settings.ImageSavePath, fileName);

            image.Save(filePath, ImageFormat.Png);

            _savedFiles.Add(filePath);

            var wslPath = _settings.ToWslPath(filePath);
            PastePath(wslPath, image);
        }
        catch (Exception ex)
        {
            _trayIcon.ShowBalloonTip(3000, "WSL Paste Image",
                $"Error: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private static async void PastePath(string text, Image originalImage)
    {
        var cbSize = Marshal.SizeOf<NativeMethods.INPUT>();

        // Wait for the user to physically release all modifier keys.
        for (int i = 0; i < 50; i++)
        {
            bool anyHeld = NativeMethods.IsKeyDown(Keys.Menu)
                        || NativeMethods.IsKeyDown(Keys.ControlKey)
                        || NativeMethods.IsKeyDown(Keys.ShiftKey);
            if (!anyHeld) break;
            await Task.Delay(20);
        }

        // Set clipboard to the path text, then send Ctrl+V to paste.
        Clipboard.SetText(text);

        var pasteInputs = new[]
        {
            NativeMethods.CreateKeyInput((ushort)Keys.ControlKey, false),
            NativeMethods.CreateKeyInput((ushort)Keys.V, false),
            NativeMethods.CreateKeyInput((ushort)Keys.V, true),
            NativeMethods.CreateKeyInput((ushort)Keys.ControlKey, true),
        };

        NativeMethods.SendInput((uint)pasteInputs.Length, pasteInputs, cbSize);

        // Send a trailing space as a separate keystroke (terminals strip
        // trailing whitespace from pasted text, but accept typed spaces).
        await Task.Delay(200);
        var spaceInputs = new[]
        {
            NativeMethods.CreateKeyInput((ushort)Keys.Space, false),
            NativeMethods.CreateKeyInput((ushort)Keys.Space, true),
        };
        NativeMethods.SendInput((uint)spaceInputs.Length, spaceInputs, cbSize);

        // Restore the original image to the clipboard
        await Task.Delay(200);
        try
        {
            Clipboard.SetImage(originalImage);
        }
        catch { }
        finally
        {
            originalImage.Dispose();
        }
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add("Open Screenshot Folder", null, (_, _) =>
        {
            Directory.CreateDirectory(_settings.ImageSavePath);
            Process.Start("explorer.exe", _settings.ImageSavePath);
        });
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        return menu;
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            RemoveHook();
            _settings = form.UpdatedSettings;
            _settings.Save();

            _trayIcon.Text = $"WSL Paste Image ({_settings.FormatHotkey()})";

            _hookId = InstallHook();
            if (_hookId == IntPtr.Zero)
            {
                _trayIcon.ShowBalloonTip(3000, "WSL Paste Image",
                    "Failed to install keyboard hook.",
                    ToolTipIcon.Error);
            }
        }
    }

    private void ExitApp()
    {
        RemoveHook();
        CleanupSessionFiles();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    private void CleanupSessionFiles()
    {
        foreach (var file in _savedFiles)
        {
            try { File.Delete(file); } catch { }
        }
        _savedFiles.Clear();
    }

    private void CleanupOldFiles()
    {
        try
        {
            if (!Directory.Exists(_settings.ImageSavePath)) return;
            foreach (var file in Directory.GetFiles(_settings.ImageSavePath, "screenshot_*.png"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    private static Icon CreateTrayIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var clipBrush = new SolidBrush(Color.FromArgb(70, 130, 180));
        g.FillRoundedRectangle(clipBrush, 4, 6, 24, 24, 3);

        using var clipPen = new Pen(Color.FromArgb(70, 130, 180), 2.5f);
        g.DrawRoundedRectangle(clipPen, 10, 2, 12, 7, 2);

        using var pageBrush = new SolidBrush(Color.White);
        g.FillRectangle(pageBrush, 8, 12, 16, 14);

        using var sunBrush = new SolidBrush(Color.FromArgb(255, 200, 50));
        g.FillEllipse(sunBrush, 10, 13, 5, 5);

        using var mountainBrush = new SolidBrush(Color.FromArgb(76, 175, 80));
        g.FillPolygon(mountainBrush, new Point[] {
            new(8, 26), new(14, 18), new(18, 22), new(22, 17), new(24, 26)
        });

        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemoveHook();
            CleanupSessionFiles();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        using var path = CreateRoundedRect(x, y, w, h, r);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics g, Pen pen, float x, float y, float w, float h, float r)
    {
        using var path = CreateRoundedRect(x, y, w, h, r);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        var d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
