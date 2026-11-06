using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Ror2Common
{
    // Loading a plugin's Rust DLL (sms.dll, pikmin.dll) and checking it matches the plugin.
    public static class NativeLib
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string path);

        /// Loads the DLL at `path` up front, so a plugin's [DllImport("name")]s bind to it
        /// whatever Mono's search path is (Windows matches the loaded module by name).
        public static bool Load(string path, out string error)
        {
            if (LoadLibraryW(path) == IntPtr.Zero)
            {
                error = $"LoadLibrary({path}) failed: Win32 error {Marshal.GetLastWin32Error()}";
                return false;
            }
            error = null;
            return true;
        }

        /// Compares the native side's struct sizes with the plugin's copies (`expected`, in the
        /// native list's order). Null if they match, else what differs.
        public static string CompareSizes(uint[] native, uint nativeCount, (string name, int size)[] expected)
        {
            var sb = new StringBuilder();
            if (nativeCount != expected.Length)
                sb.Append($"{nativeCount} structs natively, {expected.Length} in the plugin; ");
            for (int i = 0; i < expected.Length && i < native.Length && i < nativeCount; i++)
                if (native[i] != expected[i].size)
                    sb.Append($"{expected[i].name} is {native[i]} bytes natively, {expected[i].size} in the plugin; ");
            return sb.Length == 0 ? null : sb.ToString().TrimEnd(' ', ';');
        }

        public static int Size<T>() => Marshal.SizeOf(typeof(T));

        /// A nul-terminated UTF-8 copy of `s`, for `const char*` arguments.
        public static byte[] CString(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            Array.Resize(ref b, b.Length + 1);
            return b;
        }

        /// A nul-terminated UTF-8 string at `p`, or null.
        public static string Utf8(IntPtr p)
        {
            if (p == IntPtr.Zero) return null;
            int n = 0;
            while (Marshal.ReadByte(p, n) != 0) n++;
            var bytes = new byte[n];
            Marshal.Copy(p, bytes, 0, n);
            return Encoding.UTF8.GetString(bytes);
        }

        /// A core's `*_take_panic(out, len)`: the oldest panic it caught, 0 when none.
        public delegate uint TakePanicFn([Out] byte[] output, uint length);

        static readonly byte[] panicText = new byte[2048];
        static readonly System.Collections.Generic.HashSet<string> panicsUnavailable =
            new System.Collections.Generic.HashSet<string>();

        /// Logs the panics `library` caught at its C boundary since the last call. Every export
        /// of the Rust cores catches its own panics (libcommon::ffi) and returns its failed value
        /// (0, -1, null) instead of crashing the game, so this log is the only sign one happened.
        /// Call it from one place, once a frame, on the main thread. An older DLL without the
        /// export is noted once and skipped.
        public static void DrainPanics(string library, TakePanicFn take, Action<string> logError)
        {
            if (panicsUnavailable.Contains(library)) return;
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    uint n = take(panicText, (uint)panicText.Length);
                    if (n == 0) return;
                    int len = (int)Math.Min(n, (uint)panicText.Length);
                    logError($"{library} caught a native panic; the call returned its failure value instead: " +
                        Encoding.UTF8.GetString(panicText, 0, len));
                }
            }
            catch (Exception e) when (e is EntryPointNotFoundException || e is DllNotFoundException)
            {
                panicsUnavailable.Add(library);
                logError($"{library} has no panic report ({e.GetType().Name}); is the DLL older than the plugin?");
            }
        }
    }
}
