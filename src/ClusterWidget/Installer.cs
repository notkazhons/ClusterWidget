using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClusterWidget;

/// <summary>
/// Per-user setup, run when the exe is started from somewhere other than its install folder (a download, a zip):
/// copies itself to %LOCALAPPDATA%\Programs\ClusterWidget, adds a Start menu shortcut and an "Installed apps" entry, and
/// can be removed again with <c>--uninstall</c>. No administrator rights needed. Skipped by <c>--portable</c>, a
/// <c>portable.txt</c> next to the exe, or CLUSTERWIDGET_DIR (test instances).
/// </summary>
internal static unsafe partial class Installer
{
    private const string Title = "ClusterWidget setup";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClusterWidget";
    private static readonly nint HKCU = unchecked((nint)0x80000001);

    /// <summary>CLUSTERWIDGET_INSTALL_DIR overrides the folder (for testing the installer without touching a real install).</summary>
    public static string InstallDir => Environment.GetEnvironmentVariable("CLUSTERWIDGET_INSTALL_DIR") is { Length: > 0 } d ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ClusterWidget");
    private static string InstalledExe => Path.Combine(InstallDir, "ClusterWidget.exe");
    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ClusterWidget.lnk");
    private static string Version => typeof(Installer).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

    private static bool RunningFromInstallDir(string exe) =>
        string.Equals(Path.GetDirectoryName(exe), InstallDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the setup wizard should be offered for this launch.</summary>
    public static bool ShouldOffer(string[] args)
    {
        if (Has(args, "--install")) return true;
        string? exe = Environment.ProcessPath;
        if (exe == null) return false;
        if (Environment.GetEnvironmentVariable("CLUSTERWIDGET_DIR") is { Length: > 0 }) return false;
        if (Has(args, "--portable")) return false;
        if (File.Exists(Path.Combine(Path.GetDirectoryName(exe) ?? "", "portable.txt"))) return false;
        return !RunningFromInstallDir(exe);
    }

    public static bool Has(string[] args, string flag) => Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static int Ask(bool silent, int silentAnswer, string text, uint type) => silent ? silentAnswer : Native.MessageBox(0, text, Title, type);

    /// <summary>The wizard. Returns true to carry on running this copy (the user chose not to install), false to exit. <paramref name="silent"/> (<c>--install</c>) answers Yes to everything except "start with Windows", and does not launch the app.</summary>
    public static bool RunWizard(bool silent = false)
    {
        string src = Environment.ProcessPath!;
        bool update = File.Exists(InstalledExe);
        string verb = update ? "Update" : "Install";

        int r = Ask(silent, 6,
            $"{(update ? "ClusterWidget is already installed. Update it to" : "Install")} version {Version}?\n\n" +
            $"It will be copied to:\n{InstallDir}\n\n" +
            "A Start menu shortcut is added, and ClusterWidget appears under Settings > Apps > Installed apps, where you can uninstall it. " +
            "Your clusters and settings are kept separately and are never touched by an update.\n\n" +
            $"Yes: {verb}.\nNo: run it once from here, without installing.\nCancel: quit.",
            0x3 | 0x20 /* YESNOCANCEL | QUESTION */);
        if (r == 2) return false;      // cancel
        if (r != 6) return true;       // no: run from here

        int startup = Ask(silent, 7, "Start ClusterWidget automatically when you sign in to Windows?\n\nYou can change this later in the settings.",
            0x4 | 0x20 /* YESNO | QUESTION */);

        CloseRunningCopy();
        try
        {
            Directory.CreateDirectory(InstallDir);
            CopyWithRetry(src, InstalledExe);
            MakeShortcut();
            WriteUninstallEntry();
        }
        catch (Exception ex)
        {
            Ask(silent, 0, "Setup could not finish:\n\n" + ex.Message + "\n\nIf ClusterWidget is still running, exit it from its tray icon and try again.", 0x10);
            return false;
        }
        Native.SetStartsWithWindows(startup == 6, InstalledExe);

        if (silent) return false;
        Native.MessageBox(0, $"ClusterWidget is {(update ? "updated" : "installed")} and will start now.\n\nYou can delete the downloaded files; the installed copy is in:\n{InstallDir}", Title, 0x40);
        Native.ShellExecute(0, null, InstalledExe, null, InstallDir, 1);
        return false;
    }

    /// <summary>Asks a running copy to quit (an old version that ignores the request has to be exited from the tray) and waits for it.</summary>
    private static void CloseRunningCopy()
    {
        nint w = Native.FindWindow("ClusterWidgetApp", null);
        if (w == 0) return;
        Native.PostMessage(w, App.WM_QUIT_SELF, 0, 0);
        for (int i = 0; i < 30 && Native.FindWindow("ClusterWidgetApp", null) != 0; i++) Thread.Sleep(100);
    }

    private static void CopyWithRetry(string from, string to)
    {
        for (int i = 0; ; i++)
        {
            try { File.Copy(from, to, true); return; }
            catch (IOException) when (i < 20) { Thread.Sleep(250); } // still being released by the copy that was just closed
        }
    }

    // ---------------------------------------------------------------- uninstall

    /// <summary><paramref name="silent"/> (<c>--silent</c>) uninstalls without asking and keeps the settings.</summary>
    public static void Uninstall(bool silent = false)
    {
        if (!silent && Native.MessageBox(0, "Uninstall ClusterWidget from this PC?\n\nYour clusters and settings are kept unless you choose to remove them next.", Title, 0x4 | 0x30 | 0x100) != 6) return;
        bool wipe = !silent && Native.MessageBox(0, "Also delete your clusters and settings?\n\nThis removes all your clusters, saved designs and copied pictures. Your own files and programs are never touched.",
            Title, 0x4 | 0x30 | 0x100) == 6;

        CloseRunningCopy();
        Native.SetStartsWithWindows(false, null);
        try { File.Delete(ShortcutPath); } catch (Exception) { /* already gone */ }
        RegDeleteTree(HKCU, UninstallKey);
        if (wipe) try { Directory.Delete(Config.RootDir, true); } catch (Exception) { /* in use or already gone */ }

        // the exe cannot delete itself while running: a hidden command window removes the folder a moment after this process ends
        Native.ShellExecute(0, null, "cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{InstallDir}\"", Path.GetTempPath(), 0);
        if (!silent) Native.MessageBox(0, "ClusterWidget is uninstalled.", Title, 0x40);
    }

    // ---------------------------------------------------------------- registry + shortcut

    private static void WriteUninstallEntry()
    {
        SetString("DisplayName", "ClusterWidget");
        SetString("DisplayVersion", Version);
        SetString("Publisher", "notkazhons");
        SetString("DisplayIcon", InstalledExe);
        SetString("InstallLocation", InstallDir);
        SetString("UninstallString", $"\"{InstalledExe}\" --uninstall");
        SetString("URLInfoAbout", "https://github.com/notkazhons/ClusterWidget");
        SetDword("NoModify", 1);
        SetDword("NoRepair", 1);
        SetDword("EstimatedSize", (uint)(new FileInfo(InstalledExe).Length / 1024));
    }

    private static void SetString(string name, string value)
    {
        fixed (char* p = value) RegSetKeyValue(HKCU, UninstallKey, name, 1 /* REG_SZ */, p, (uint)((value.Length + 1) * sizeof(char)));
    }

    private static void SetDword(string name, uint value) => RegSetKeyValue(HKCU, UninstallKey, name, 4 /* REG_DWORD */, &value, 4);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetKeyValueW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegSetKeyValue(nint hkey, string subKey, string value, uint type, void* data, uint bytes);
    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteTreeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegDeleteTree(nint hkey, string subKey);
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint ctx, in Guid iid, out nint obj);

    /// <summary>Writes the Start menu .lnk through IShellLink (called by vtable slot, since there is no managed COM here).</summary>
    private static void MakeShortcut()
    {
        Native.CoInitializeEx(0, 2);
        var clsid = new Guid("00021401-0000-0000-C000-000000000046");
        var iidLink = new Guid("000214F9-0000-0000-C000-000000000046");
        var iidFile = new Guid("0000010B-0000-0000-C000-000000000046");
        if (CoCreateInstance(clsid, 0, 1 /* INPROC */, iidLink, out nint link) != 0 || link == 0) return;
        nint* vt = *(nint**)link;
        fixed (char* exe = InstalledExe) fixed (char* dir = InstallDir) fixed (char* desc = "Group your shortcuts into a desktop box")
        {
            ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[20])(link, exe);          // SetPath
            ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[9])(link, dir);           // SetWorkingDirectory
            ((delegate* unmanaged[Stdcall]<nint, char*, int>)vt[7])(link, desc);          // SetDescription
            ((delegate* unmanaged[Stdcall]<nint, char*, int, int>)vt[17])(link, exe, 0);  // SetIconLocation
        }
        nint file;
        if (((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)vt[0])(link, &iidFile, &file) == 0 && file != 0)
        {
            nint* fvt = *(nint**)file;
            fixed (char* lnk = ShortcutPath) ((delegate* unmanaged[Stdcall]<nint, char*, int, int>)fvt[6])(file, lnk, 1); // IPersistFile.Save
            ((delegate* unmanaged[Stdcall]<nint, uint>)fvt[2])(file);                                                    // Release
        }
        ((delegate* unmanaged[Stdcall]<nint, uint>)vt[2])(link);
    }
}
