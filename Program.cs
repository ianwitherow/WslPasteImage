namespace WslPasteImage;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        _mutex = new Mutex(true, "WslPasteImage_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("WSL Paste Image is already running.", "WSL Paste Image",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new App());

        GC.KeepAlive(_mutex);
    }
}
