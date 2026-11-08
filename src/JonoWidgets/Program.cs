using System.Runtime.InteropServices;

namespace JonoWidgets;

public static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [MTAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            RunProvider();
            return;
        }

        // Opened from the Start menu: there's no window, the widgets live in the Widgets board.
        MessageBoxW(IntPtr.Zero,
            "These widgets live in the Widgets board.\n\n" +
            "Press Win+W, click + (Add widgets), choose Jono's Widgets, and pin Todoist, Repos or Now.\n\n" +
            $"Installed build: {Updates.OwnBuild}",
            "Jono's Widgets", 0x40 /* MB_ICONINFORMATION */);
    }

    private static void RunProvider()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Log.Info($"provider starting, build {Updates.OwnBuild}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("unobserved task", e.Exception); e.SetObserved(); };

        // Restore pinned widgets (and their settings) before the host starts calling us.
        Hub.Start();
        using var registration = new Registration<WidgetProvider>();
        // The Widgets board starts this process on demand and ends it when it's no longer needed.
        Thread.Sleep(Timeout.Infinite);
    }
}
