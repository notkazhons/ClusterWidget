using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ClusterWidget;

/// <summary>A tiny modal "type a name" box (Win32 has no ready-made one).</summary>
internal static unsafe class TextPrompt
{
    private static bool _registered, _done, _ok;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProcThunk(nint h, uint m, nint w, nint l)
    {
        switch (m)
        {
            case 0x111: // WM_COMMAND
                switch ((int)(w & 0xFFFF))
                {
                    case 1: _ok = true; _done = true; return 0;   // IDOK (Enter)
                    case 2: _done = true; return 0;               // IDCANCEL (Esc)
                }
                break;
            case 0x10: _done = true; return 0; // WM_CLOSE
        }
        return Native.DefWindowProc(h, m, w, l);
    }

    /// <summary>Returns the text typed, or null if the box was cancelled or left empty.</summary>
    public static string? Ask(nint owner, string title, string label, string initial)
    {
        nint inst = Native.GetModuleHandle(0);
        if (!_registered)
        {
            fixed (char* cls = "ClusterWidgetPrompt")
            {
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)sizeof(WNDCLASSEX),
                    lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProcThunk,
                    hInstance = inst, hCursor = Native.LoadCursor(0, 32512), hbrBackground = 16, lpszClassName = cls,
                };
                Native.RegisterClassEx(ref wc);
            }
            _registered = true;
        }

        _done = false; _ok = false;
        const uint style = 0x80C80000; // WS_POPUP | WS_CAPTION | WS_SYSMENU
        nint h = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW, "ClusterWidgetPrompt", title, style, 300, 300, 380, 170, owner, 0, inst, 0);
        float s = Native.GetDpiForWindow(h) / 96f;
        if (s <= 0) s = 1f;
        nint font = Native.CreateFont(-(int)MathF.Round(9f * s * 96f / 72f), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");

        nint Ctl(string cls, string text, uint st, int x, int y, int w, int hh, int id)
        {
            nint c = Native.CreateWindowEx(0, cls, text, 0x50000000 | st, (int)(x * s), (int)(y * s), (int)(w * s), (int)(hh * s), h, id, inst, 0);
            Native.SendMessage(c, 0x30, font, 1);
            return c;
        }
        Ctl("STATIC", label, 0, 16, 14, 340, 20, 0);
        nint edit = Ctl("EDIT", initial, 0x800000 /* WS_BORDER */ | 0x80 /* ES_AUTOHSCROLL */ | 0x10000 /* WS_TABSTOP */, 16, 40, 340, 26, 10);
        Ctl("BUTTON", "OK", 0x1 /* BS_DEFPUSHBUTTON */ | 0x10000, 176, 86, 86, 28, 1);
        Ctl("BUTTON", "Cancel", 0x10000, 270, 86, 86, 28, 2);

        // size so the client area is 372x128 logical pixels
        Native.GetWindowRect(h, out RECT wr);
        Native.GetClientRect(h, out RECT cr);
        int ww = (int)(372 * s) + (wr.Right - wr.Left) - cr.Right, wh = (int)(130 * s) + (wr.Bottom - wr.Top) - cr.Bottom;
        Native.GetWindowRect(owner, out RECT ownerRect);
        Native.SetWindowPos(h, Native.HWND_TOPMOST, ownerRect.Left + 60, ownerRect.Top + 80, ww, wh, 0);

        Native.EnableWindow(owner, 0);
        Native.ShowWindow(h, 5);
        Native.SetForegroundWindow(h);
        Native.SetFocus(edit);
        Native.SendMessage(edit, 0xB1 /* EM_SETSEL */, 0, -1);

        while (!_done && Native.GetMessage(out MSG m, 0, 0, 0) > 0)
        {
            if (Native.IsDialogMessage(h, m) != 0) continue; // Tab, Enter and Esc work like in a dialog
            Native.TranslateMessage(m);
            Native.DispatchMessage(m);
        }

        string? result = null;
        if (_ok)
        {
            char* buf = stackalloc char[256];
            int n = Native.GetWindowText(edit, buf, 256);
            string t = new string(buf, 0, n).Trim();
            if (t.Length > 0) result = t;
        }
        Native.EnableWindow(owner, 1);
        Native.DestroyWindow(h);
        Native.DeleteObject(font);
        Native.SetForegroundWindow(owner);
        return result;
    }
}
