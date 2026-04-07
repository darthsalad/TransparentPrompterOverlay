using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using TransparentPrompterOverlay.Native;

namespace TransparentPrompterOverlay.Services;

public enum HotkeyAction
{
    ToggleClickThrough,
    ToggleVisibility,
    TogglePlayback,
    IncreaseScrollRate,
    DecreaseScrollRate
}

public sealed class GlobalHotkeyManager : IDisposable
{
    private readonly IntPtr _windowHandle;
    private readonly Dictionary<int, HotkeyAction> _registeredHotkeys = new();
    private HwndSource? _hwndSource;

    public GlobalHotkeyManager(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public event Action<HotkeyAction>? HotkeyPressed;

    public void RegisterDefaultHotkeys()
    {
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WndProc);

        Register(2001, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.F8, HotkeyAction.ToggleClickThrough);
        Register(2002, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.F9, HotkeyAction.ToggleVisibility);
        Register(2003, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.F10, HotkeyAction.TogglePlayback);
        Register(2004, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.F11, HotkeyAction.IncreaseScrollRate);
        Register(2005, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.F12, HotkeyAction.DecreaseScrollRate);
    }

    public void Dispose()
    {
        foreach (var hotkeyId in _registeredHotkeys.Keys)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, hotkeyId);
        }

        _registeredHotkeys.Clear();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private void Register(int hotkeyId, uint modifiers, Key key, HotkeyAction action)
    {
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!NativeMethods.RegisterHotKey(_windowHandle, hotkeyId, modifiers, virtualKey))
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Unable to register the {action} hotkey. Win32 error {errorCode}.");
        }

        _registeredHotkeys[hotkeyId] = action;
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY &&
            _registeredHotkeys.TryGetValue(wParam.ToInt32(), out var action))
        {
            HotkeyPressed?.Invoke(action);
            handled = true;
        }

        return IntPtr.Zero;
    }
}
