using MouseDebouncer;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        _mutex = new Mutex(true, "MouseDebouncerSingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show(
                "Mouse Debouncer is already running.",
                "Mouse Debouncer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());

        _mutex.ReleaseMutex();
    }
}
