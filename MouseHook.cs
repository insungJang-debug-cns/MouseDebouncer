using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseDebouncer;

/// <summary>
/// SetWindowsHookEx(WH_MOUSE_LL)로 전역 마우스 이벤트를 감지하고,
/// DebounceManager의 판단에 따라 이벤트를 차단하거나 다음 훅으로 전달한다.
/// </summary>
public sealed class MouseHook : IDisposable
{
    #region Win32 API

    private const int WH_MOUSE_LL   = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint  mouseData;  // XBUTTONDOWN 시 high-word가 버튼 ID (1=XBtn1, 2=XBtn2)
        public uint  flags;
        public uint  time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc fn, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    #endregion

    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelMouseProc? _hookProc; // GC가 수집하지 않도록 필드로 보관

    private readonly DebounceManager _debounceManager;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 마우스 버튼이 눌릴 때마다 발생. 차단 여부와 무관하게 항상 발생.
    /// </summary>
    public event EventHandler<MouseButton>? ButtonDetected;

    public MouseHook(DebounceManager debounceManager)
    {
        _debounceManager = debounceManager;
    }

    /// <summary>
    /// 전역 마우스 훅을 설치한다. 호출 스레드(UI 스레드)에서 훅 콜백이 실행된다.
    /// </summary>
    public void Install()
    {
        _hookProc = HookCallback;

        using var process = Process.GetCurrentProcess();
        using var module  = process.MainModule
            ?? throw new InvalidOperationException("Cannot get main module.");

        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc,
            GetModuleHandle(module.ModuleName), threadId: 0);

        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"SetWindowsHookEx failed. Error: {Marshal.GetLastWin32Error()}");
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            MouseButton? button = ResolveButton(wParam, lParam);
            if (button.HasValue)
            {
                ButtonDetected?.Invoke(this, button.Value);
                if (IsEnabled && _debounceManager.ShouldBlock(button.Value))
                    return (IntPtr)1; // 0이 아닌 값 → 이벤트 전달 차단
            }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static MouseButton? ResolveButton(IntPtr wParam, IntPtr lParam)
    {
        return wParam.ToInt32() switch
        {
            WM_LBUTTONDOWN => MouseButton.Left,
            WM_RBUTTONDOWN => MouseButton.Right,
            WM_MBUTTONDOWN => MouseButton.Middle,
            WM_XBUTTONDOWN => ResolveXButton(lParam),
            _              => null
        };
    }

    private static MouseButton? ResolveXButton(IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        int id   = (int)(info.mouseData >> 16); // high-word
        return id switch
        {
            1 => MouseButton.XButton1,
            2 => MouseButton.XButton2,
            _ => null
        };
    }

    public void Dispose() => Uninstall();
}
