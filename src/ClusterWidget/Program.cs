using System;

namespace ClusterWidget;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (Installer.Has(args, "--uninstall")) { Installer.Uninstall(Installer.Has(args, "--silent")); return; }
        if (Installer.ShouldOffer(args) && !Installer.RunWizard(Installer.Has(args, "--install"))) return;

        // One instance only: a second launch asks the running one to show itself. (CLUSTERWIDGET_DIR gives a test instance its own lock.)
        Native.CreateMutex(0, 1, "Local\\ClusterWidget.SingleInstance" + (Environment.GetEnvironmentVariable("CLUSTERWIDGET_DIR") is { Length: > 0 } ? ".test" : ""));
        if (Native.GetLastError() == 183 /* ERROR_ALREADY_EXISTS */)
        {
            // already running: show its box and settings instead of looking like nothing happened
            Native.PostMessage(Native.FindWindow("ClusterWidgetApp", null), App.WM_SHOW_SELF, 0, 0);
            return;
        }

        App.Run();
    }
}
