using System;
using System.Runtime.InteropServices;
using Ror2Common;

static class Program
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static unsafe void Main()
    {
        Check(MaterialAlpha.Tint(0,TextureAlphaMode.Replace,0)==1,"texture replace must ignore zero material alpha");
        Check(MaterialAlpha.Tint(0,TextureAlphaMode.Blend,.75f)==.75f,"texture blend must keep its contribution");
        Check(MaterialAlpha.Tint(0,TextureAlphaMode.Blend,0)==0,"authored fade start must stay transparent");
        Check(MaterialAlpha.Tint(0,TextureAlphaMode.Multiply,1)==0,"intentional zero alpha must stay transparent");
        Check(MaterialAlpha.Tint(.5f,TextureAlphaMode.Multiply,1)==.5f,"normal surface alpha must stay unchanged");
        float textureBorder=0,textureInterior=.4f;
        Check(MaterialAlpha.Tint(0,TextureAlphaMode.Replace,1)*textureBorder==0,"transparent borders must remain transparent");
        Check(MaterialAlpha.Tint(0,TextureAlphaMode.Replace,1)*textureInterior==.4f,"texture interior must retain source alpha");
        var colors=new float[]{.2f,.4f,.6f,0, 1,1,1,0};var sources=new float[]{0,.75f,1, 0,1,2};
        using(var buffer=new MaterialAlphaBuffer())fixed(float* c=colors)fixed(float* a=sources) {
            var ptr=buffer.Resolve((IntPtr)c,2,(IntPtr)a,2);var result=new float[8];Marshal.Copy(ptr,result,0,8);
            Check(result[0]==.2f&&result[1]==.4f&&result[2]==.6f,"alpha correction changed RGB");
            Check(result[3]==.75f&&result[7]==1,"batch alpha operations not resolved");
            sources[1]=0;buffer.Resolve((IntPtr)c,2,(IntPtr)a,2);Marshal.Copy(ptr,result,0,8);
            Check(result[3]==0,"animated texture blend did not fade out");
            bool rejected=false;try{buffer.Resolve((IntPtr)c,2,(IntPtr)a,1);}catch(ArgumentException){rejected=true;}
            Check(rejected,"mismatched native buffers accepted");
            Check(buffer.Resolve((IntPtr)c,2,IntPtr.Zero,0)==(IntPtr)c,"legacy resolved source changed");
        }
        Console.WriteLine("PASS: common alpha replacement, blend animation, intentional transparency, texture mask, RGB preservation and native buffer bounds");
    }
}
