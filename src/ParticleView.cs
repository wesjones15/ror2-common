using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ror2Common
{
    // Native HSD particles provide position, size, colours and texture selection.
    // Unity only draws their quads; it does not emit or advance another simulation.
    public sealed class ParticleView : IDisposable
    {
        IParticleSource source;
        ViewParticle[] sprites=new ViewParticle[128];
        readonly Dictionary<uint,(ViewTexture,byte[])> originals=new Dictionary<uint,(ViewTexture,byte[])>();
        readonly Dictionary<(uint,uint,uint,uint),(Texture2D,Material,float)> variants=new Dictionary<(uint,uint,uint,uint),(Texture2D,Material,float)>();
        readonly float[] matrix=new float[16];
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        Mesh quad;
        readonly float scale;
        public ParticleView(IParticleSource source,float units)
        {
            this.source=source??throw new ArgumentNullException(nameof(source));scale=units;
            quad=new Mesh { name="Melee particle quad",vertices=new[]{new Vector3(-0.5f,-0.5f,0),new Vector3(0.5f,-0.5f,0),new Vector3(0.5f,0.5f,0),new Vector3(-0.5f,0.5f,0)},
                uv=new[]{new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),new Vector2(0,0)},triangles=new[]{0,1,2,0,2,3},colors=new[]{Color.white,Color.white,Color.white,Color.white} };
            quad.RecalculateBounds();
        }
        void Matrix(Matrix4x4 transform){for(int i=0;i<16;i++)matrix[i]=transform[i];}
        public static Matrix4x4 At(Vector3 world,Quaternion rotation,float scale)
            =>Matrix4x4.TRS(world/scale,rotation,new Vector3(-1,1,1));
        public void Spawn(uint id,Matrix4x4 transform,ulong key)
        {
            if(source==null)return;Matrix(transform);
            if(!source.Spawn(id,matrix,key))CommonLog.Error("Melee emitter "+id+": "+source.Error);
        }
        public void Attach(ulong key,Matrix4x4 transform)
        {if(source!=null){Matrix(transform);source.Attach(key,matrix);}}
        public void Stop(ulong key){if(source!=null)source.Stop(key);}
        public void Step(float dt)
        {if(source!=null&&!source.Step(dt))CommonLog.Error("Melee particle commands: "+source.Error);}
        static uint Pack(float r,float g,float b,float a)
            =>(uint)(Mathf.Clamp(Mathf.RoundToInt(r*255),0,255)|(Mathf.Clamp(Mathf.RoundToInt(g*255),0,255)<<8)|(Mathf.Clamp(Mathf.RoundToInt(b*255),0,255)<<16)|(Mathf.Clamp(Mathf.RoundToInt(a*255),0,255)<<24));
        Material Material(in ViewParticle sprite)
        {
            uint prim=Pack(sprite.r,sprite.g,sprite.b,sprite.a);
            uint env=(sprite.flags&128)!=0?Pack(sprite.er,sprite.eg,sprite.eb,sprite.ea):0;
            uint flags=sprite.flags&((3u<<22)|(1u<<9)|(1u<<28));
            var key=(sprite.texture,prim,env,flags);
            if(variants.TryGetValue(key,out var existing)){variants[key]=(existing.Item1,existing.Item2,Time.unscaledTime);return existing.Item2;}
            if(!originals.TryGetValue(sprite.texture,out var original)) {
                if(!source.Texture(sprite.texture,out var info,null,0))throw new InvalidOperationException(source.Error);
                var rgba=new byte[checked(info.width*info.height*4)];
                if(!source.Texture(sprite.texture,out info,rgba,(uint)rgba.Length))throw new InvalidOperationException(source.Error);
                original=(info,rgba);originals.Add(sprite.texture,original);
            }
            // psdisptev.c: interpolate env/primary per texture channel, including
            // alpha. Baking this inexpensive colour variant preserves the source
            // two-colour TEV operation with RoR2's existing particle shader.
            var pixels=new byte[original.Item2.Length];
            for(int i=0;i<pixels.Length;i++) {
                int ch=i&3,t=original.Item2[i],p=(int)(prim>>(ch*8))&255,e=(int)(env>>(ch*8))&255;
                pixels[i]=(byte)((e*(255-t)+p*t+127)/255);
            }
            var tex=new Texture2D((int)original.Item1.width,(int)original.Item1.height,TextureFormat.RGBA32,false,true) {name="Melee particle "+sprite.texture,wrapMode=TextureWrapMode.Clamp,filterMode=(flags&(1<<9))!=0?FilterMode.Point:FilterMode.Bilinear};
            tex.LoadRawTextureData(pixels);tex.Apply(false,true);
            var material=Shaders.SeeThrough(tex.name,tex,(flags&(3<<22))==(1<<22),false);
            if(!material){UnityEngine.Object.Destroy(tex);throw new InvalidOperationException("RoR2 particle shader unavailable");}
            if((flags&(1<<28))!=0)material.SetFloat("_ZTest",(float)CompareFunction.Always);
            variants.Add(key,(tex,material,Time.unscaledTime));return material;
        }
        public void Draw(Matrix4x4? placement=null)
        {
            if(source==null)return;
            uint n=source.Draws(null,0);if(n>4096)throw new InvalidOperationException("Native particle budget exceeded");
            if(n>sprites.Length)sprites=new ViewParticle[n];
            if(n>0&&source.Draws(sprites,(uint)sprites.Length)!=n)throw new InvalidOperationException(source.Error);
            var camera=GameSettings.PlayerCamera();if(!camera)return;
            for(int i=0;i<n;i++) {
                var p=sprites[i];var point=new Vector3(p.position.x,p.position.y,p.position.z)*scale;
                if(placement.HasValue)point=placement.Value.MultiplyPoint3x4(point);
                var look=camera.position-point;
                if(look.sqrMagnitude<0.00001f)continue;
                var rotation=Quaternion.LookRotation(look,camera.up)*Quaternion.AngleAxis(-p.rotation*Mathf.Rad2Deg,Vector3.forward);
                var size=new Vector3(p.size*scale,p.size*scale,1);
                if((p.flags&(1<<18))!=0)size.x=-size.x;if((p.flags&(1<<19))!=0)size.y=-size.y;
                Graphics.DrawMesh(quad,Matrix4x4.TRS(point,rotation,size),Material(in p),0,null,0,properties,ShadowCastingMode.Off,false);
            }
            // Animated colour variants are transient; keep current/recent draws.
            if(variants.Count>256)foreach(var entry in new List<KeyValuePair<(uint,uint,uint,uint),(Texture2D,Material,float)>>(variants))
                if(Time.unscaledTime-entry.Value.Item3>1f){UnityEngine.Object.Destroy(entry.Value.Item1);UnityEngine.Object.Destroy(entry.Value.Item2);variants.Remove(entry.Key);}
        }
        public void Dispose()
        {
            if(source!=null){source.Dispose();source=null;}
            foreach(var value in variants.Values){UnityEngine.Object.Destroy(value.Item1);UnityEngine.Object.Destroy(value.Item2);}variants.Clear();originals.Clear();
            if(quad){UnityEngine.Object.Destroy(quad);quad=null;}
        }
    }
}
