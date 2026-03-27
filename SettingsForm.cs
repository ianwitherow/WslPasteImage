namespace WslPasteImage;

public class SettingsForm : Form
{
    private readonly TextBox _pathTextBox;
    private readonly TextBox _hotkeyTextBox;
    private readonly CheckBox _altCheckBox;
    private readonly CheckBox _ctrlCheckBox;
    private readonly CheckBox _shiftCheckBox;
    private readonly CheckBox _startWithWindowsCheckBox;
    private readonly Label _hotkeyWarning;
    private Keys _capturedKey;

    public Settings UpdatedSettings { get; private set; }

    public SettingsForm(Settings current)
    {
        UpdatedSettings = current;
        _capturedKey = current.HotkeyKey;

        Text = "WSL Paste Image - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(460, 370);
        Padding = new Padding(12);

        // --- Image Storage ---
        var pathGroup = new GroupBox
        {
            Text = "Image Storage",
            Dock = DockStyle.Top,
            Height = 80,
            Padding = new Padding(8)
        };

        var pathLabel = new Label { Text = "Save Path:", AutoSize = true, Location = new Point(12, 28) };
        _pathTextBox = new TextBox
        {
            Text = current.ImageSavePath,
            Location = new Point(12, 48),
            Width = 340
        };
        var browseBtn = new Button
        {
            Text = "...",
            Location = new Point(358, 47),
            Width = 35,
            Height = 23
        };
        browseBtn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _pathTextBox.Text };
            if (dlg.ShowDialog() == DialogResult.OK)
                _pathTextBox.Text = dlg.SelectedPath;
        };

        pathGroup.Controls.AddRange([pathLabel, _pathTextBox, browseBtn]);

        // --- Hotkey ---
        var hotkeyGroup = new GroupBox
        {
            Text = "Keyboard Shortcut",
            Dock = DockStyle.Top,
            Height = 130,
            Padding = new Padding(8)
        };

        _ctrlCheckBox = new CheckBox { Text = "Ctrl", Checked = current.HotkeyCtrl, Location = new Point(12, 25), AutoSize = true };
        _altCheckBox = new CheckBox { Text = "Alt", Checked = current.HotkeyAlt, Location = new Point(72, 25), AutoSize = true };
        _shiftCheckBox = new CheckBox { Text = "Shift", Checked = current.HotkeyShift, Location = new Point(125, 25), AutoSize = true };

        _ctrlCheckBox.CheckedChanged += (_, _) => UpdateHotkeyWarning();
        _altCheckBox.CheckedChanged += (_, _) => UpdateHotkeyWarning();
        _shiftCheckBox.CheckedChanged += (_, _) => UpdateHotkeyWarning();

        var keyLabel = new Label { Text = "Key:", AutoSize = true, Location = new Point(12, 58) };
        _hotkeyTextBox = new TextBox
        {
            Text = current.HotkeyKey.ToString(),
            ReadOnly = true,
            Location = new Point(50, 55),
            Width = 120,
            BackColor = SystemColors.Window
        };
        _hotkeyTextBox.KeyDown += HotkeyTextBox_KeyDown;

        var hotkeyHint = new Label
        {
            Text = "Click the key box and press the desired key",
            AutoSize = true,
            Location = new Point(12, 85),
            ForeColor = SystemColors.GrayText,
            Font = new Font(Font.FontFamily, 8)
        };

        _hotkeyWarning = new Label
        {
            Text = "Alt+V will override native image paste in non-WSL terminals (e.g. PowerShell)",
            Location = new Point(12, 102),
            AutoSize = true,
            ForeColor = Color.OrangeRed,
            Font = new Font(Font.FontFamily, 8),
            Visible = false
        };

        hotkeyGroup.Controls.AddRange([_ctrlCheckBox, _altCheckBox, _shiftCheckBox,
            keyLabel, _hotkeyTextBox, hotkeyHint, _hotkeyWarning]);

        // --- Options ---
        var optionsGroup = new GroupBox
        {
            Text = "Options",
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(8)
        };

        _startWithWindowsCheckBox = new CheckBox
        {
            Text = "Start with Windows",
            Checked = current.StartWithWindows,
            AutoSize = true,
            Location = new Point(12, 25)
        };

        optionsGroup.Controls.Add(_startWithWindowsCheckBox);

        // --- Buttons ---
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(0, 5, 0, 0)
        };

        var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        var saveBtn = new Button { Text = "Save", Width = 80 };
        saveBtn.Click += SaveBtn_Click;

        buttonPanel.Controls.AddRange([cancelBtn, saveBtn]);

        AcceptButton = saveBtn;
        CancelButton = cancelBtn;

        Controls.AddRange([optionsGroup, hotkeyGroup, pathGroup, buttonPanel]);

        Controls.SetChildIndex(pathGroup, 0);
        Controls.SetChildIndex(hotkeyGroup, 1);
        Controls.SetChildIndex(optionsGroup, 2);
        Controls.SetChildIndex(buttonPanel, 3);

        UpdateHotkeyWarning();
    }

    private void UpdateHotkeyWarning()
    {
        // Show warning if the hotkey is Alt+V without Shift or Ctrl,
        // since that conflicts with native image paste in non-WSL terminals.
        bool isAltV = _altCheckBox.Checked
            && !_ctrlCheckBox.Checked
            && !_shiftCheckBox.Checked
            && _capturedKey == Keys.V;
        _hotkeyWarning.Visible = isAltV;
    }

    private void HotkeyTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;

        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
            or Keys.LWin or Keys.RWin or Keys.LMenu or Keys.RMenu
            or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey)
            return;

        _capturedKey = e.KeyCode;
        _hotkeyTextBox.Text = e.KeyCode.ToString();

        _altCheckBox.Checked = e.Alt;
        _ctrlCheckBox.Checked = e.Control;
        _shiftCheckBox.Checked = e.Shift;

        UpdateHotkeyWarning();
    }

    private void SaveBtn_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pathTextBox.Text))
        {
            MessageBox.Show("Please enter a save path.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_altCheckBox.Checked && !_ctrlCheckBox.Checked && !_shiftCheckBox.Checked)
        {
            MessageBox.Show("At least one modifier key (Ctrl, Alt, or Shift) is required.",
                "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        UpdatedSettings = new Settings
        {
            ImageSavePath = _pathTextBox.Text.Trim(),
            HotkeyKey = _capturedKey,
            HotkeyAlt = _altCheckBox.Checked,
            HotkeyCtrl = _ctrlCheckBox.Checked,
            HotkeyShift = _shiftCheckBox.Checked,
            StartWithWindows = _startWithWindowsCheckBox.Checked
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
