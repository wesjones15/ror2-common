using System;
using System.Collections.Generic;
using UnityEngine;
namespace Ror2Common
{
    /// Terrain-sampled colored quads with a generated soft mask; no game assets/colliders.
    public sealed class SurfaceQuads : IDisposable
    {
        readonly GameObject root; readonly Mesh mesh; readonly Material material; readonly Texture2D mask;
        readonly List<Vector3> points=new List<Vector3>(); readonly List<Vector2> uv=new List<Vector2>();
        readonly List<Color> colors=new List<Color>(); readonly List<int> triangles=new List<int>();
        public SurfaceQuads(string name, Color tint)
        {
            root=new GameObject(name); UnityEngine.Object.DontDestroyOnLoad(root);
            mesh=new Mesh {name=name};mesh.MarkDynamic();root.AddComponent<MeshFilter>().sharedMesh=mesh;
            mask=new Texture2D(32,32,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[1024]; for(int y=0;y<32;y++)for(int x=0;x<32;x++){
                float r=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;
                pixels[y*32+x]=new Color(1,1,1,Mathf.Clamp01((1f-r)*4f));}
            mask.SetPixels(pixels);mask.Apply();
            material=Shaders.SeeThrough(name,mask,false,true);if(material){material.SetColor("_TintColor",tint);material.renderQueue=2998;}
            root.AddComponent<MeshRenderer>().sharedMaterial=material;
        }
        public void Begin(){points.Clear();uv.Clear();colors.Clear();triangles.Clear();}
        public void Vertex(Vector3 at,Vector2 tex,float opacity){points.Add(at);uv.Add(tex);colors.Add(new Color(1,1,1,opacity));}
        public void End(){for(int i=0;i+3<points.Count;i+=4){triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);triangles.Add(i);triangles.Add(i+2);triangles.Add(i+3);}
            mesh.Clear();mesh.SetVertices(points);mesh.SetUVs(0,uv);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();root.SetActive(material && points.Count>0);}
        public void Hide(){if(root)root.SetActive(false);}
        public void Dispose(){UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(mask);}
    }
}
