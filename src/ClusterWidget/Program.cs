using System;

namespace ClusterWidget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance only: a second launch exits quietly.
        Native.CreateMutex(0, 1, "Local\\ClusterWidget.SingleInstance");
        if (Native.GetLastError() == 183 /* ERROR_ALREADY_EXISTS */) return;

        new Widget(Config.Load()).Run();
    }
}
