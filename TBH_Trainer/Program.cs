namespace TBH_Trainer;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ShowFatal(ex);
        };

        try
        {
            // Let Windows scale the complete fixed-layout UI as one unit on high-DPI displays.
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            ShowFatal(ex);
        }
    }

    static void ShowFatal(Exception ex)
    {
        var log = Path.Combine(AppContext.BaseDirectory, "TBH_Trainer_crash.log");
        try
        {
            File.WriteAllText(log, ex + Environment.NewLine);
        }
        catch { /* ignore */ }

        MessageBox.Show(
            ex.Message + "\n\nDetails written to:\n" + log,
            "TBH Trainer — startup error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
