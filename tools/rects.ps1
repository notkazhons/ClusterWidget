# Prints the screen rectangle of every visible ClusterWidget cluster window of the running app: "left,top,right,bottom".
Add-Type -TypeDefinition @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public class ClusterRects {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [StructLayout(LayoutKind.Sequential)] public struct RC { public int L, T, R, B; }
  public static List<string> Find(uint wanted) {
    var res = new List<string>();
    EnumProc cb = delegate(IntPtr h, IntPtr l) {
      var s = new StringBuilder(64); GetClassNameW(h, s, 64);
      if (s.ToString() == "ClusterWidgetWindow") {
        uint owner; GetWindowThreadProcessId(h, out owner);
        if (owner == wanted && IsWindowVisible(h)) { RC rc; GetWindowRect(h, out rc); res.Add(rc.L + "," + rc.T + "," + rc.R + "," + rc.B); }
      }
      return true;
    };
    EnumWindows(cb, IntPtr.Zero);
    return res;
  }
}
"@
[ClusterRects]::SetProcessDPIAware() | Out-Null
$p = Get-Process ClusterWidget | Select-Object -First 1
[ClusterRects]::Find([uint32]$p.Id)
