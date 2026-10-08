using System;
using System.Runtime.InteropServices;

namespace Ror2Common
{
    // Short PCM playback independent of Unity's disabled audio engine in Wwise builds.
    // Owns the queued memory until waveOutReset has returned it to the application.
    public sealed class PcmOutput : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        struct Format
        {
            public ushort tag, channels;
            public uint rate, bytesPerSecond;
            public ushort align, bits, extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Header
        {
            public IntPtr data;
            public uint length, recorded;
            public UIntPtr user;
            public uint flags, loops;
            public IntPtr next;
            public UIntPtr reserved;
        }
        [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr device, uint id, ref Format format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutPause(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutRestart(IntPtr device);
        IntPtr device, header, data;
        bool prepared;
        static readonly uint HeaderSize = (uint)Marshal.SizeOf<Header>();
        public bool Finished => header == IntPtr.Zero || (Marshal.PtrToStructure<Header>(header).flags & 1) != 0;

        public PcmOutput(float[] samples, int rate, float gain)
        {
            try
            {
                var format = new Format { tag = 1, channels = 2, rate = (uint)rate,
                    bytesPerSecond = (uint)rate * 4, align = 4, bits = 16 };
                Check(waveOutOpen(out device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0), "open");
                var pcm = new short[samples.Length];
                for (int i = 0; i < pcm.Length; i++)
                    pcm[i] = (short)(Math.Max(-1f, Math.Min(1f, samples[i] * gain)) * 32767f);
                data = Marshal.AllocHGlobal(pcm.Length * 2);
                Marshal.Copy(pcm, 0, data, pcm.Length);
                header = Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.StructureToPtr(new Header { data = data, length = (uint)pcm.Length * 2 }, header, false);
                Check(waveOutPrepareHeader(device, header, HeaderSize), "prepare");
                prepared = true;
                Check(waveOutWrite(device, header, HeaderSize), "play");
            }
            catch { Dispose(); throw; }
        }
        static void Check(uint result, string operation)
        {
            if (result != 0) throw new InvalidOperationException($"Windows audio {operation} failed ({result})");
        }
        public void Pause(bool pause)
        {
            if (device != IntPtr.Zero) { if (pause) waveOutPause(device); else waveOutRestart(device); }
        }
        public void Dispose()
        {
            if (device != IntPtr.Zero)
            {
                waveOutReset(device);
                if (prepared) waveOutUnprepareHeader(device, header, HeaderSize);
                waveOutClose(device);
                device = IntPtr.Zero;
            }
            if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
            if (data != IntPtr.Zero) { Marshal.FreeHGlobal(data); data = IntPtr.Zero; }
            prepared = false;
        }
    }
}
