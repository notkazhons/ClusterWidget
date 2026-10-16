using System;

namespace ClusterWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance only: a second launch exits quietly. (CLUSTERWIDGET_DIR gives a test instance its own lock.)
        Native.CreateMutex(0, 1, "Local\\ClusterWidget.SingleInstance" + (Environment.GetEnvironmentVariable("CLUSTERWIDGET_DIR") is { Length: > 0 } ? ".test" : ""));
        if (Native.GetLastError() == 183 /* ERROR_ALREADY_EXISTS */) return;

        App.Run();
    }
}
