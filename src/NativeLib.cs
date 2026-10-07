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
    }
}
