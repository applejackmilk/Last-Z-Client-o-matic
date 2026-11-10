
namespace WindowsMCP_Integrated
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    public sealed class MouseListener : IDisposable
    {
        public event Action<int, int>? ActionMade;

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;

        private IntPtr _hookHandle = IntPtr.Zero;
        private readonly LowLevelMouseProc _hookProc;

        public bool IsListening => _hookHandle != IntPtr.Zero;

        public MouseListener()
        {
            _hookProc = HookCallback;
        }

        public void Start()
        {
            if (IsListening)
                return;

            using Process process = Process.GetCurrentProcess();
            using ProcessModule module = process.MainModule!;

            _hookHandle = SetWindowsHookEx(
                WH_MOUSE_LL,
                _hookProc,
                GetModuleHandle(module.ModuleName),
                0);

            if (_hookHandle == IntPtr.Zero)
                throw new InvalidOperationException("Failed to install mouse hook.");
        }

        public void Stop()
        {
            if (!IsListening)
                return;

            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        private IntPtr HookCallback(
            int nCode,
            IntPtr wParam,
            IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
            {
                var mouseData =
                    Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                ActionMade?.Invoke(
                    mouseData.pt.x,
                    mouseData.pt.y);
            }

            return CallNextHookEx(
                _hookHandle,
                nCode,
                wParam,
                lParam);
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        #region Win32

        private delegate IntPtr LowLevelMouseProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelMouseProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(
            IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(
            string? lpModuleName);

        #endregion
    }
}
