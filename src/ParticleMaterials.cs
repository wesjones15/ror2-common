using UnityEngine;
using UnityEngine.AddressableAssets;
namespace Ror2Common
{
    // JParticle indirect maps and host water use the game's scene-distortion shader.
    // Callers own their source textures and coverage masks.
    public static class ParticleMaterials
    {
        public static Material Refraction(string name,Texture normal,Texture mask,float normalGain=4f,float strength=1f)
        {
            try {
                var source=Addressables.LoadAssetAsync<Material>("RoR2/Base/Common/VFX/matDistortion.mat").WaitForCompletion();
                if (!source || !source.shader) return null;
                var material=new Material(source) {name=name};
                if (material.HasProperty("_Magnitude")) material.SetFloat("_Magnitude",material.GetFloat("_Magnitude")*strength/Mathf.Max(0.001f,normalGain));
                material.SetTexture("_BumpMap",normal);
                material.SetTexture("_MaskTex",mask?mask:Texture2D.whiteTexture);
                if (material.HasProperty("_InvFade")) material.SetFloat("_InvFade",2f);
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull",0f);
                return material;
            } catch {return null;}
        }
    }
}
