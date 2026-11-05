
namespace WindowsMCP_Integrated
{
    using System;
    using System.Runtime.InteropServices;

    [Flags]
    public enum HotKeyModifiers : uint
    {
        None = 0x0000,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008
    }

public sealed class GlobalHotKeyManager : IDisposable
    {
        public event Action? ToggleRequested;
        public event Action? ExitRequested;

        private const int WM_HOTKEY = 0x0312;

        private const int TOGGLE_HOTKEY_ID = 1;
        private const int EXIT_HOTKEY_ID = 2;

        private readonly HotKeyModifiers _toggleModifiers;
        private readonly uint _toggleKey;

        private readonly HotKeyModifiers _exitModifiers;
        private readonly uint _exitKey;

        private bool _disposed;

        public GlobalHotKeyManager(
            HotKeyModifiers toggleModifiers,
            uint toggleKey,
            HotKeyModifiers exitModifiers,
            uint exitKey)
        {
            _toggleModifiers = toggleModifiers;
            _toggleKey = toggleKey;
            _exitModifiers = exitModifiers;
            _exitKey = exitKey;
        }

        public void Start()
        {
            RegisterToggleHotKey();
            RegisterExitHotKey();
        }

        public void RunMessageLoop(Func<bool> shouldExit)
        {
            MSG msg;

            while (!shouldExit() &&
                   GetMessage(out msg, IntPtr.Zero, 0, 0))
            {
                if (msg.message == WM_HOTKEY)
                {
                    switch (msg.wParam.ToInt32())
                    {
                        case TOGGLE_HOTKEY_ID:
                            ToggleRequested?.Invoke();
                            break;

                        case EXIT_HOTKEY_ID:
                            ExitRequested?.Invoke();
                            break;
                    }
                }

                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private void RegisterToggleHotKey()
        {
            if (!RegisterHotKey(
                    IntPtr.Zero,
                    TOGGLE_HOTKEY_ID,
                    (uint)_toggleModifiers,
                    _toggleKey))
            {
                throw new InvalidOperationException(
                    "Failed to register toggle hotkey.");
            }
        }

        private void RegisterExitHotKey()
        {
            if (!RegisterHotKey(
                    IntPtr.Zero,
                    EXIT_HOTKEY_ID,
                    (uint)_exitModifiers,
                    _exitKey))
            {
                throw new InvalidOperationException(
                    "Failed to register exit hotkey.");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            UnregisterHotKey(IntPtr.Zero, TOGGLE_HOTKEY_ID);
            UnregisterHotKey(IntPtr.Zero, EXIT_HOTKEY_ID);

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #region Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(
            IntPtr hWnd,
            int id,
            uint fsModifiers,
            uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(
            IntPtr hWnd,
            int id);

        [DllImport("user32.dll")]
        private static extern bool GetMessage(
            out MSG lpMsg,
            IntPtr hWnd,
            uint wMsgFilterMin,
            uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(
            ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(
            ref MSG lpMsg);

        #endregion
    }
}
