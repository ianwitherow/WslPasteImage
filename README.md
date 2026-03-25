# WSL Paste Image

**The problem:** You're working in Windows Terminal with a WSL session — maybe using Claude Code, vim, or any CLI tool that accepts image file paths — and you've just taken a screenshot. In a native Windows app you'd simply Ctrl+V, but WSL terminals don't support pasting image data from the clipboard. So you're stuck saving the file manually, finding the path, converting it to a `/mnt/...` path, and typing it in. Every time.

**The solution:** WslPasteImage runs in the background and listens for a hotkey (default **Alt+V**). When pressed, it:

1. Grabs the image from your clipboard
2. Saves it as a PNG to a temp directory
3. Types the WSL-compatible file path (e.g. `/mnt/c/Temp/WslScreenshots/screenshot_20260325_110742_429.png`) directly into your active window
4. Restores the original image to your clipboard

That's it. One keystroke, and the image path appears at your cursor.

**Who needs this:** Anyone using WSL through Windows Terminal who regularly needs to pass screenshots or clipboard images to CLI tools — especially AI coding assistants like Claude Code that accept image paths for visual context.

## Installation

### Prerequisites

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (to build from source)

### Build and run

```
git clone https://github.com/ianwitherow/WslPasteImage.git
cd WslPasteImage
dotnet run
```

### Publish a standalone exe

Framework-dependent (179 KB, requires .NET 9 runtime):
```
dotnet publish -c Release -r win-x64 --no-self-contained -o C:\Apps\WslPasteImage
```

Self-contained (108 MB, no dependencies):
```
dotnet publish -c Release -r win-x64 --self-contained -o C:\Apps\WslPasteImage
```

Then run `WslPasteImage.exe` — it appears in your system tray.

## Usage

1. Take a screenshot (Win+Shift+S, Print Screen, Snipping Tool, etc.)
2. Switch to your WSL terminal
3. Press **Alt+V**
4. The WSL file path is typed into your terminal

If there's no image on the clipboard, you'll get a brief notification saying so.

## Settings

Right-click the tray icon (or double-click it) to open settings:

| Setting | Default | Description |
|---|---|---|
| **Save Path** | `C:\Temp\WslScreenshots` | Where screenshot PNGs are saved |
| **Hotkey** | Alt+V | The keyboard shortcut that triggers a paste |
| **Windows Terminal only** | Off | Only activate when Windows Terminal is the focused window |
| **Start with Windows** | Off | Launch automatically on login |

Settings are stored in `%AppData%\WslPasteImage\settings.json`.

## Cleanup

Screenshots saved during a session are automatically deleted when the app exits. Leftover files from previous sessions (e.g. after a crash) are cleaned up on the next launch.

## License

MIT
