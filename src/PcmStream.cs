using System;
using System.Runtime.InteropServices;

namespace Ror2Common
{
    /// Main-thread PCM queue for native synthesizers. Uses WinMM because RoR2's
    /// Wwise build disables Unity's audio engine. No unmanaged callback/reentry.
    public sealed class PcmStream : IDisposable
    {
        [StructLayout(LayoutKind.Sequential, Pack=2)]
        struct Format { public ushort tag,channels; public uint rate,bytesPerSecond; public ushort align,bits,extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct Header { public IntPtr data; public uint length,recorded; public UIntPtr user; public uint flags,loops; public IntPtr next; public UIntPtr reserved; }
        [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr device,uint id,ref Format format,IntPtr callback,IntPtr instance,uint flags);
        [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr device,IntPtr header,uint size);
        [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr device,IntPtr header,uint size);
        [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr device,IntPtr header,uint size);
        [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutPause(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutRestart(IntPtr device);
        const int Frames=512,Blocks=4;
        static readonly uint HeaderSize=(uint)Marshal.SizeOf<Header>();
        readonly IntPtr[] headers=new IntPtr[Blocks],data=new IntPtr[Blocks];
        readonly bool[] prepared=new bool[Blocks],queued=new bool[Blocks];
        readonly float[] samples=new float[Frames*2];
        readonly short[] pcm=new short[Frames*2];
        IntPtr device;
        int next;
        bool paused;
        public PcmStream(int rate)
        {
            try {
                var format=new Format { tag=1,channels=2,rate=(uint)rate,bytesPerSecond=(uint)rate*4,align=4,bits=16 };
                Check(waveOutOpen(out device,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0),"open");
                for(int i=0;i<Blocks;i++) {
                    data[i]=Marshal.AllocHGlobal(Frames*4);headers[i]=Marshal.AllocHGlobal((int)HeaderSize);
                    Marshal.StructureToPtr(new Header { data=data[i],length=Frames*4 },headers[i],false);
                    Check(waveOutPrepareHeader(device,headers[i],HeaderSize),"prepare");prepared[i]=true;
                }
            } catch { Dispose();throw; }
        }
        // Reuse headers in playback order even after a long rendered frame.
        public void Pump(Func<float[],bool> render,float gain,bool pause)
        {
            if(device==IntPtr.Zero)return;
            if(paused!=pause) { Check(pause?waveOutPause(device):waveOutRestart(device),"pause");paused=pause; }
            if(pause)return;
            for(int count=0;count<Blocks;count++) {
                int i=next;var h=Marshal.PtrToStructure<Header>(headers[i]);
                if(queued[i]&&(h.flags&1)==0)break;
                Array.Clear(samples,0,samples.Length);
                if(!render(samples))throw new InvalidOperationException("Native PCM render failed");
                for(int s=0;s<pcm.Length;s++)pcm[s]=(short)(Math.Max(-1f,Math.Min(1f,samples[s]*gain))*32767f);
                Marshal.Copy(pcm,0,data[i],pcm.Length);
                Check(waveOutWrite(device,headers[i],HeaderSize),"queue");queued[i]=true;next=(i+1)%Blocks;
            }
        }
        static void Check(uint result,string operation) { if(result!=0)throw new InvalidOperationException($"Windows audio {operation} failed ({result})"); }
        public void Dispose()
        {
            if(device!=IntPtr.Zero)waveOutReset(device);
            for(int i=0;i<Blocks;i++) {
                if(prepared[i]&&device!=IntPtr.Zero)waveOutUnprepareHeader(device,headers[i],HeaderSize);
                prepared[i]=false;
                if(headers[i]!=IntPtr.Zero){Marshal.FreeHGlobal(headers[i]);headers[i]=IntPtr.Zero;}
                if(data[i]!=IntPtr.Zero){Marshal.FreeHGlobal(data[i]);data[i]=IntPtr.Zero;}
            }
            if(device!=IntPtr.Zero){waveOutClose(device);device=IntPtr.Zero;}
        }
    }
}
