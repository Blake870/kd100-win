namespace Kd100Win;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\kd100-win", out bool first);
        if (!first)
        {
            MessageBox.Show(Strings.AlreadyRunning, "kd100-win",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Info($"Unhandled UI exception: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Info($"Unhandled exception: {e.ExceptionObject}");
        using var app = new TrayApp();
        Application.Run(app);
    }
}
