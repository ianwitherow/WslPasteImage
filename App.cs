using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WslPasteImage;

public class App : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private Settings _settings;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly List<string> _savedFiles = new();

    public App()
    {
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

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;

        if (!RegisterHotkey())
        {
            _trayIcon.ShowBalloonTip(3000, "WSL Paste Image",
                $"Failed to register hotkey {_settings.FormatHotkey()}. It may be in use by another application.",
                ToolTipIcon.Error);
        }
        else
        {
            _trayIcon.ShowBalloonTip(2000, "WSL Paste Image",
                $"Running. Press {_settings.FormatHotkey()} to paste clipboard image as WSL path.",
                ToolTipIcon.Info);
        }
    }

    private bool RegisterHotkey()
    {
        return NativeMethods.RegisterHotKey(
            _hotkeyWindow.Handle,
            NativeMethods.HOTKEY_ID,
            _settings.GetWin32Modifiers(),
            (uint)_settings.HotkeyKey);
    }

    private void UnregisterHotkey()
    {
        NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, NativeMethods.HOTKEY_ID);
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_settings.WindowsTerminalOnly && !IsWindowsTerminalFocused())
            return;

        if (!Clipboard.ContainsImage())
        {
            _trayIcon.ShowBalloonTip(1500, "WSL Paste Image",
                "No image found on clipboard.", ToolTipIcon.Info);
            return;
        }

        try
        {
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

        // Send Escape to dismiss any menu bar that Alt may have activated.
        var escInputs = new[]
        {
            NativeMethods.CreateKeyInput((ushort)Keys.Escape, false),
            NativeMethods.CreateKeyInput((ushort)Keys.Escape, true),
        };
        NativeMethods.SendInput((uint)escInputs.Length, escInputs, cbSize);

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

    private static bool IsWindowsTerminalFocused()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        try
        {
            var process = Process.GetProcessById((int)processId);
            var name = process.ProcessName.ToLowerInvariant();
            return name is "windowsterminal" or "wt";
        }
        catch
        {
            return false;
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
            UnregisterHotkey();
            _settings = form.UpdatedSettings;
            _settings.Save();

            _trayIcon.Text = $"WSL Paste Image ({_settings.FormatHotkey()})";

            if (!RegisterHotkey())
            {
                _trayIcon.ShowBalloonTip(3000, "WSL Paste Image",
                    $"Failed to register hotkey {_settings.FormatHotkey()}.",
                    ToolTipIcon.Error);
            }
        }
    }

    private void ExitApp()
    {
        UnregisterHotkey();
        CleanupSessionFiles();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _hotkeyWindow.DestroyHandle();
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

        // Clipboard body
        using var clipBrush = new SolidBrush(Color.FromArgb(70, 130, 180));
        g.FillRoundedRectangle(clipBrush, 4, 6, 24, 24, 3);

        // Clipboard clip
        using var clipPen = new Pen(Color.FromArgb(70, 130, 180), 2.5f);
        g.DrawRoundedRectangle(clipPen, 10, 2, 12, 7, 2);

        // Image icon (mountain/sun)
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
            CleanupSessionFiles();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private class HotkeyWindow : NativeWindow
    {
        public event EventHandler? HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == NativeMethods.HOTKEY_ID)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }
            base.WndProc(ref m);
        }
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
