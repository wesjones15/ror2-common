using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace Ror2Common
{
    // A native view's models in Unity: one mesh per model built at load (a submesh per batch),
    // then every frame one object per posed instance with the vertices the native side skinned
    // for it. Each batch's material is its TEV baked into one texture, drawn with RoR2's own
    // HGStandard shader so the game's lighting applies; see-through and additive batches use
    // its Cloud Remap effect shader instead (unlit; HGStandard is deferred and can't blend).
    //
    // Generalised from ror2-olimar's ModelView (lessons: SetPixelData + Apply for mipmapped
    // uploads, try/catch per texture, mirror right-handed models across their own X).
    public sealed class ModelView : IDisposable
    {
        readonly IViewSource src;
        readonly Transform root;
        readonly float translucentOpacity;
        // One material for every part, in place of the baked ones (Create's `skin`).
        Material skin;
        readonly Dictionary<Material, Material> skinCopies = new Dictionary<Material, Material>();

        class Model
        {
            public Mesh template;
            public ViewBatch[] batches;
            public int vertexCount;
            public bool hasEnv;
            public bool lightsVertices;
            public float[] vertexAlpha;
            public bool sharedGeometry, geometryReady;
        }

        /// `ViewBatch.lit` bit 4: the batch fades by its vertices' alpha (`IVertexAlphaSource`).
        const byte FadesByVertex = 4;

        class Instance
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Mesh mesh;
            public uint model;
            public bool seen;
            public int[] textures;
            public Material[] materials;
            public Material skin;
            public BlockState[] blocks;
        }

        // Unity copies a property block on every submission. Most native batches keep the
        // same tint/fade for many frames; submit again only when their actual inputs change.
        struct BlockState
        {
            public bool set;
            public byte mode;
            public Color color, flash;
            public float alpha;
            static bool Same(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
            public bool Matches(BlockState other) => set && mode == other.mode && alpha == other.alpha
                && Same(color, other.color) && Same(flash, other.flash);
        }

        Model[] models;
        bool needsVertexLighting;
        readonly Dictionary<ulong, Instance> instances = new Dictionary<ulong, Instance>();
        readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();
        readonly Dictionary<(int, byte, byte), Material> materials = new Dictionary<(int, byte, byte), Material>();
        ViewDraw[] draws = new ViewDraw[64];
        int drawCount;
        Vector3[] vbuf = new Vector3[0], nbuf = new Vector3[0];
        Vector2[] ubuf = new Vector2[0];
        Color[] cbuf = new Color[0];
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int FlashId = Shader.PropertyToID("_FlashColor");
        static readonly int TintId = Shader.PropertyToID("_TintColor");

        /// A skin for particular draws (a Goobo Jr. clone's captain among a shared view's), or
        /// null for the usual: this material replaces the draw's textures as Create's `skin` does.
        public Func<ViewDraw, Material> SkinFor;

        /// RoR2's hit flash colour for draws flagged as flashing.
        public Color FlashColor = new Color(1f, 1f, 1f, 0.5f);
        /// The icy sheen for draws flagged frozen (flag 2: a Pikmin caught in a Glacial blast).
        public Color FrozenColor = new Color(0.55f, 0.8f, 1f, 0.35f);
        public string Name { get; }
        /// The object every model instance hangs under.
        public Transform Root => root;

        public int DrawCount => drawCount;
        public ViewDraw DrawAt(int i) => draws[i];

        ModelView(IViewSource src, Transform root, string name, Material skin, float translucentOpacity)
        {
            this.src = src;
            this.skin = skin;
            this.root = root;
            this.translucentOpacity = Mathf.Clamp01(translucentOpacity);
            Name = name;
        }

        /// Builds every model's mesh. Null (and logs why) if that fails; the caller then keeps
        /// its stand-in. `parent` null keeps the objects across scenes. `skin`, if given, is drawn
        /// on every part instead of their textures (a copy, both sides, with the part's alpha
        /// and hit flash; the game's gummy clone material, say).
        public static ModelView Create(IViewSource src, string name, Transform parent = null, Material skin = null,
            float translucentOpacity = Shaders.SurfaceTintOpacity)
        {
            var root = new GameObject(name).transform;
            if (parent) root.SetParent(parent, false);
            else UnityEngine.Object.DontDestroyOnLoad(root.gameObject);
            if (skin)
            {
                skin = new Material(skin) { name = name + " skin" };
                // The mirror flip turns the winding around.
                skin.SetFloat("_Cull", 0f);
            }
            var mv = new ModelView(src, root, name, skin, translucentOpacity);
            try
            {
                mv.Build();
            }
            catch (Exception e)
            {
                CommonLog.Error($"building {name}'s meshes: {e}");
                mv.Dispose();
                return null;
            }
            return mv;
        }

        void Build()
        {
            int n = (int)src.ModelCount();
            models = new Model[n];
            for (uint m = 0; m < n; m++)
            {
                if (!src.Model(m, out var info))
                    throw new Exception($"model {m} has no info");
                var batches = new ViewBatch[info.batch_count];
                src.Batches(m, batches, info.batch_count);
                var indices = new int[info.index_count];
                src.Indices(m, indices, info.index_count);
                var uvPairs = new float[info.vertex_count * 2];
                src.Uvs(m, uvPairs, info.vertex_count);
                var uvs = new Vector2[info.vertex_count];
                for (int i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(uvPairs[2 * i], uvPairs[2 * i + 1]);

                // A batch that fades by its vertices (bit 4 of lit) gets their alpha.
                float[] alphas = null;
                if (src is IVertexAlphaSource fades && Array.Exists(batches, x => (x.lit & FadesByVertex) != 0))
                {
                    alphas = new float[info.vertex_count];
                    fades.VertexAlphas(m, alphas, info.vertex_count);
                }

                var mesh = new Mesh { name = $"{Name} model {m}" };
                if (info.vertex_count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = new Vector3[info.vertex_count];
                mesh.uv = uvs;
                if (alphas != null)
                {
                    var white = new Color[alphas.Length];
                    for (int i = 0; i < white.Length; i++) white[i] = new Color(1f, 1f, 1f, alphas[i]);
                    mesh.colors = white;
                }
                mesh.subMeshCount = batches.Length;
                for (int b = 0; b < batches.Length; b++)
                {
                    var tri = new int[batches[b].index_count];
                    Array.Copy(indices, batches[b].first_index, tri, 0, tri.Length);
                    mesh.SetTriangles(tri, b, false);
                    MaterialFor(batches[b], batches[b].texture);
                }
                // A first box from the bind pose (each frame boxes the posed vertices).
                var lo = new Vector3(info.lo.x, info.lo.y, info.lo.z);
                var hi = new Vector3(info.hi.x, info.hi.y, info.hi.z);
                mesh.bounds = new Bounds((lo + hi) * 0.5f, (hi - lo) * 2f + Vector3.one * 20f);
                models[m] = new Model
                {
                    template = mesh,
                    batches = batches,
                    vertexCount = (int)info.vertex_count,
                    hasEnv = Array.Exists(batches, x => x.env != 0),
                    lightsVertices = Array.Exists(batches, x => (x.blend == ViewBlend.Translucent && (x.lit & 3) != 0) || (x.lit & FadesByVertex) != 0),
                    vertexAlpha = alphas,
                };
                // Vertex lighting depends on each instance's rotation: those meshes must
                // remain separate even when the source's positions are rigid.
                models[m].sharedGeometry = src is IStaticGeometrySource && !models[m].lightsVertices;
                needsVertexLighting |= models[m].lightsVertices;
            }
        }

        // Baked texture `id`, loaded the first time it's asked for. A texture Unity won't take
        // leaves its batches untextured, not the whole model.
        Texture2D TextureFor(int id)
        {
            if (id < 0) return null;
            if (textures.TryGetValue(id, out var tex)) return tex;
            try { tex = LoadTexture((uint)id); }
            catch (Exception e) { CommonLog.Warn($"{Name} texture {id}: {e.Message}"); }
            textures[id] = tex;
            return tex;
        }

        Material MaterialFor(ViewBatch b, int texture)
        {
            var k = (texture, b.blend, b.lit);
            if (!materials.TryGetValue(k, out var m))
            {
                m = MakeMaterial(b, TextureFor(texture));
                materials[k] = m;
            }
            return m;
        }

        Texture2D LoadTexture(uint id)
        {
            if (!src.Texture(id, out var t, null, 0)) return null;
            var px = new byte[t.width * t.height * 4];
            if (!src.Texture(id, out t, px, (uint)px.Length)) return null;
            return MakeTexture($"{Name} texture {id}", t, px);
        }

        /// A mipmapped texture from a native view's RGBA8 pixels (rows top first, as Unity's
        /// v = 0 is the first row: GX's v = 0 is the top). Not readable afterwards.
        public static Texture2D MakeTexture(string name, ViewTexture t, byte[] px)
        {
            var tex = new Texture2D((int)t.width, (int)t.height, TextureFormat.RGBA32, true)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapModeU = Wrap(t.wrap_s),
                wrapModeV = Wrap(t.wrap_t),
            };
            // Only the top level: Apply builds the smaller mips (LoadRawTextureData would want
            // the whole chain).
            tex.SetPixelData(px, 0);
            tex.Apply(true, true);
            return tex;
        }

        static TextureWrapMode Wrap(byte w) =>
            w == 1 ? TextureWrapMode.Clamp : w == 2 ? TextureWrapMode.Mirror : TextureWrapMode.Repeat;

        Material MakeMaterial(ViewBatch b, Texture2D tex)
        {
            if (skin) return skin;
            bool blended = b.blend == ViewBlend.Translucent || b.blend == ViewBlend.Additive;
            if (blended && Shaders.CloudRemap())
                return Shaders.SeeThrough($"{Name} glass", tex, b.blend == ViewBlend.Additive, b.lit != 0,
                    b.blend == ViewBlend.Translucent ? translucentOpacity : 1f);
            var m = new Material(Shaders.HgStandard()) { name = Name };
            if (tex) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", Color.white);
            if (b.blend != ViewBlend.Opaque)
            {
                m.SetFloat("_EnableCutout", 1f);
                m.EnableKeyword("CUTOUT");
            }
            // Both sides: GameCube models draw some faces from both, and the mirror flip below
            // is easy to get backwards.
            m.SetFloat("_Cull", 0f);
            // Smoothed two tone like RoR2's characters; "unlitish" where the game draws unlit.
            m.SetFloat("_RampInfo", b.lit != 0 ? 1f : 3f);
            m.SetFloat("_SpecularStrength", 0.1f);
            m.SetFloat("_SpecularExponent", 10f);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_Fade", 1f);
            return m;
        }

        /// Moves the objects to this frame's draws (after the native side's update). `scale` is
        /// metres per sim unit. `place` overrides where a draw goes (default: its own position
        /// times `scale`, and its rotation); `hide` leaves draws out.
        public unsafe void Update(float scale, Func<ViewDraw, (Vector3, Quaternion)?> place = null, Func<ViewDraw, bool> hide = null)
        {
            if (models == null) return;
            uint n = src.Draws(null, 0);
            if (n > draws.Length) draws = new ViewDraw[Math.Max(n, (uint)draws.Length * 2)];
            drawCount = (int)src.Draws(draws, (uint)draws.Length);
            var pos = (Vector3*)src.Positions(out uint vc);
            var nrm = (Vector3*)src.Normals(out _);
            var col = (Color*)src.Colors(out uint cc);
            var tid = (int*)src.TextureIds(out uint tc);
            var uvs = (Vector2*)src.FrameUvs(out uint uc);
            if (pos == null || nrm == null || col == null || tid == null) drawCount = 0;
            if (needsVertexLighting) FindSun();

            foreach (var inst in instances.Values) inst.seen = false;
            for (int i = 0; i < drawCount; i++)
            {
                var d = draws[i];
                if (d.model >= models.Length) continue;
                if (hide != null && hide(d)) continue;
                var model = models[d.model];
                int bn = model.batches.Length;
                if (d.first_vertex + model.vertexCount > vc || d.first_color + bn > Math.Min(cc, tc)) continue;
                var inst = Get(d.key, d.model);
                inst.seen = true;
                if (!inst.go.activeSelf) inst.go.SetActive(true);
                var t = inst.go.transform;
                var at = place?.Invoke(d);
                if (at.HasValue)
                {
                    t.position = at.Value.Item1;
                    t.rotation = at.Value.Item2;
                }
                else
                {
                    t.position = new Vector3(d.pos.x, d.pos.y, d.pos.z) * scale;
                    t.rotation = new Quaternion(d.rot_x, d.rot_y, d.rot_z, d.rot_w);
                }
                // The models are right-handed and Unity is left-handed: mirroring each across its
                // own X puts the right hand back on the right without moving or turning it.
                t.localScale = new Vector3(-scale, scale, scale);

                int vn = model.vertexCount;
                if (!model.sharedGeometry || !model.geometryReady)
                {
                    if (vbuf.Length < vn)
                    {
                        vbuf = new Vector3[vn];
                        nbuf = new Vector3[vn];
                        ubuf = new Vector2[vn];
                        cbuf = new Color[vn];
                    }
                    fixed (Vector3* vd = vbuf)
                    fixed (Vector3* nd = nbuf)
                    {
                        long bytes = (long)vn * sizeof(Vector3);
                        Buffer.MemoryCopy(pos + d.first_vertex, vd, bytes, bytes);
                        Buffer.MemoryCopy(nrm + d.first_vertex, nd, bytes, bytes);
                    }
                    inst.mesh.SetVertices(vbuf, 0, vn);
                    inst.mesh.SetNormals(nbuf, 0, vn);
                    // Parts posed away from their own origin leave the bind-pose box behind:
                    // box the posed vertices. Rigid sources keep this box with the shared mesh.
                    inst.mesh.RecalculateBounds();
                    if (model.hasEnv && uvs != null && d.first_vertex + vn <= uc)
                    {
                        fixed (Vector2* ud = ubuf)
                        {
                            long bytes = (long)vn * sizeof(Vector2);
                            Buffer.MemoryCopy(uvs + d.first_vertex, ud, bytes, bytes);
                        }
                        inst.mesh.SetUVs(0, ubuf, 0, vn);
                    }
                    model.geometryReady = true;
                }
                if (model.lightsVertices)
                {
                    Light(model, t.rotation, vn);
                    inst.mesh.SetColors(cbuf, 0, vn);
                }

                // A skin for this draw (the view's, or SkinFor's), or its baked textures.
                var wanted = SkinOf(d);
                bool changed = false;
                if (wanted != inst.skin)
                {
                    inst.skin = wanted;
                    for (int b = 0; b < bn; b++) { inst.textures[b] = int.MinValue; inst.materials[b] = null; }
                }
                // A draw's own textures (a blink frame); materials change only when they do.
                for (int b = 0; b < bn; b++)
                {
                    int tex = tid[d.first_color + b];
                    if (inst.textures[b] == tex && inst.materials[b]) continue;
                    inst.textures[b] = tex;
                    inst.materials[b] = wanted ? wanted : MaterialFor(model.batches[b], tex);
                    changed = true;
                }
                if (changed)
                {
                    inst.renderer.sharedMaterials = inst.materials;
                    Array.Clear(inst.blocks, 0, inst.blocks.Length);
                }

                for (int b = 0; b < bn; b++)
                {
                    var c = col[d.first_color + b];
                    var mb = model.batches[b];
                    bool cloud = !wanted && (mb.blend == ViewBlend.Translucent || mb.blend == ViewBlend.Additive) && Shaders.CloudRemap();
                    var state = new BlockState
                    {
                        set = true, mode = wanted ? (byte)1 : cloud ? (byte)2 : (byte)0,
                        color = c, alpha = Mathf.Clamp01(d.alpha),
                        flash = (d.flags & 1) != 0 ? FlashColor : (d.flags & 2) != 0 ? FrozenColor : default,
                    };
                    if (inst.blocks[b].Matches(state)) continue;
                    block.Clear();
                    if (wanted)
                    {
                        // Its own colour; only the fade and the flash.
                        block.SetFloat(FadeId, Mathf.Clamp01(d.alpha));
                        if ((d.flags & 1) != 0) block.SetColor(FlashId, FlashColor);
                        else if ((d.flags & 2) != 0) block.SetColor(FlashId, FrozenColor);
                    }
                    else if (cloud)
                        block.SetColor(TintId, c);
                    else
                    {
                        block.SetColor(ColorId, c);
                        block.SetFloat(FadeId, Mathf.Clamp01(d.alpha));
                        if ((d.flags & 1) != 0) block.SetColor(FlashId, FlashColor);
                        else if ((d.flags & 2) != 0) block.SetColor(FlashId, FrozenColor);
                    }
                    inst.renderer.SetPropertyBlock(block, b);
                    inst.blocks[b] = state;
                }
            }
            foreach (var inst in instances.Values)
                if (!inst.seen && inst.go.activeSelf) inst.go.SetActive(false);
        }

        // The skin for a draw: SkinFor's (a copy drawn both sides), else the view's.
        Material SkinOf(ViewDraw d)
        {
            var given = SkinFor?.Invoke(d);
            if (!given) return skin;
            if (!skinCopies.TryGetValue(given, out var copy))
            {
                copy = new Material(given) { name = Name + " skin" };
                copy.SetFloat("_Cull", 0f);
                skinCopies[given] = copy;
            }
            return copy;
        }

        /// Hides every instance (until the next Update).
        public void HideAll()
        {
            foreach (var inst in instances.Values)
                if (inst.go && inst.go.activeSelf) inst.go.SetActive(false);
        }

        Vector3 toSun = new Vector3(0.35f, 0.85f, 0.4f).normalized;
        Color sunColor = Color.white, ambient = new Color(0.2f, 0.2f, 0.2f, 0f);
        Light sun;
        float sunLooked = -10f;

        // RoR2's sun (the scene's main directional light) and ambient, to light see-through
        // batches the way GX lights their material channel.
        void FindSun()
        {
            if (!sun && Time.unscaledTime - sunLooked > 2f)
            {
                sunLooked = Time.unscaledTime;
                sun = RenderSettings.sun;
                if (!sun)
                    foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
                        if (l.type == LightType.Directional && l.isActiveAndEnabled && (!sun || l.intensity > sun.intensity))
                            sun = l;
            }
            if (sun)
            {
                toSun = -sun.transform.forward;
                sunColor = sun.color * Mathf.Clamp(sun.intensity, 0f, 1.5f);
            }
            var a = RenderSettings.ambientLight;
            ambient = new Color(Mathf.Clamp01(a.r), Mathf.Clamp01(a.g), Mathf.Clamp01(a.b), 0f);
        }

        // Vertex colours for a posed instance (normals in nbuf): one light,
        // `ambient + light * max(0, n.l)`, on see-through lit batches; white elsewhere.
        void Light(Model model, Quaternion rot, int vn)
        {
            for (int i = 0; i < vn; i++) cbuf[i] = Color.white;
            foreach (var b in model.batches)
            {
                bool lights = b.blend == ViewBlend.Translucent && (b.lit & 3) != 0;
                bool fades = (b.lit & FadesByVertex) != 0 && model.vertexAlpha != null;
                if (!lights && !fades) continue;
                int end = (int)Math.Min(b.first_vertex + b.vertex_count, (uint)vn);
                for (int i = (int)b.first_vertex; i < end; i++)
                {
                    var c = Color.white;
                    if (lights)
                    {
                        var n = nbuf[i];
                        // The model is mirrored across its own X, so are its normals.
                        float d = Mathf.Max(0f, Vector3.Dot(rot * new Vector3(-n.x, n.y, n.z), toSun));
                        if ((b.lit & 1) != 0)
                        {
                            c.r = Mathf.Clamp01(ambient.r + sunColor.r * d);
                            c.g = Mathf.Clamp01(ambient.g + sunColor.g * d);
                            c.b = Mathf.Clamp01(ambient.b + sunColor.b * d);
                        }
                        if ((b.lit & 2) != 0) c.a = Mathf.Clamp01(d);
                    }
                    if (fades) c.a *= model.vertexAlpha[i];
                    cbuf[i] = c;
                }
            }
        }

        Instance Get(ulong key, uint model)
        {
            if (instances.TryGetValue(key, out var inst) && inst.go)
            {
                if (inst.model == model) return inst;
                if (!models[inst.model].sharedGeometry) UnityEngine.Object.Destroy(inst.mesh);
                inst.mesh = InstanceMesh(models[model]);
                inst.go.GetComponent<MeshFilter>().sharedMesh = inst.mesh;
                inst.model = model;
                ResetMaterials(inst);
                return inst;
            }
            var go = new GameObject($"{Name} {key >> 56}/{key & 0xFFFFFFFF}");
            go.transform.SetParent(root, false);
            var mesh = InstanceMesh(models[model]);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            inst = new Instance { go = go, renderer = r, mesh = mesh, model = model };
            ResetMaterials(inst);
            instances[key] = inst;
            return inst;
        }

        static Mesh InstanceMesh(Model model)
        {
            if (model.sharedGeometry) return model.template;
            var mesh = UnityEngine.Object.Instantiate(model.template);
            mesh.MarkDynamic();
            return mesh;
        }

        void ResetMaterials(Instance inst)
        {
            var batches = models[inst.model].batches;
            inst.textures = new int[batches.Length];
            inst.materials = new Material[batches.Length];
            inst.blocks = new BlockState[batches.Length];
            for (int b = 0; b < batches.Length; b++)
            {
                inst.textures[b] = batches[b].texture;
                inst.materials[b] = MaterialFor(batches[b], batches[b].texture);
            }
            inst.renderer.sharedMaterials = inst.materials;
        }

        public void Dispose()
        {
            foreach (var inst in instances.Values)
                if (inst.mesh && !models[inst.model].sharedGeometry) UnityEngine.Object.Destroy(inst.mesh);
            instances.Clear();
            if (models != null)
                foreach (var m in models)
                    if (m != null && m.template) UnityEngine.Object.Destroy(m.template);
            models = null;
            foreach (var mat in materials.Values)
                if (mat) UnityEngine.Object.Destroy(mat);
            materials.Clear();
            foreach (var copy in skinCopies.Values)
                if (copy) UnityEngine.Object.Destroy(copy);
            skinCopies.Clear();
            if (skin) UnityEngine.Object.Destroy(skin);
            skin = null;
            foreach (var tex in textures.Values)
                if (tex) UnityEngine.Object.Destroy(tex);
            textures.Clear();
            if (root) UnityEngine.Object.Destroy(root.gameObject);
        }
    }

    public static class Shaders
    {
        // Glass and water are mostly transmission. Their baked pale colour represents the
        // visible surface tint, not an opaque white coat. Keep it faint when shells or drops
        // overlap; particle smoke and additive lights retain their authored opacity.
        public const float SurfaceTintOpacity = 0.25f;

        static Shader standard, cloud;
        static bool cloudLooked;

        /// Load the shader itself: DLC4 loads Commando's renderer materials asynchronously,
        /// so his body prefab can exist before baseRendererInfos has a usable material.
        public static Shader HgStandard()
        {
            if (standard) return standard;
            try
            {
                standard = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
            }
            catch (Exception e) { CommonLog.Warn($"loading HGStandard: {e.Message}"); }
            if (standard)
            {
                CommonLog.Info($"native model shader: {standard.name}");
                return standard;
            }
            var commando = LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterBodies/CommandoBody");
            var cm = commando ? commando.GetComponentInChildren<CharacterModel>() : null;
            if (cm && cm.baseRendererInfos != null)
                foreach (var info in cm.baseRendererInfos)
                    if (info.defaultMaterial && info.defaultMaterial.shader)
                    {
                        standard = info.defaultMaterial.shader;
                        break;
                    }
            if (!standard)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                standard = probe.GetComponent<Renderer>().sharedMaterial.shader;
                UnityEngine.Object.DestroyImmediate(probe);
                CommonLog.Warn($"HGStandard not found; using {standard.name}");
            }
            return standard;
        }

        /// RoR2's Cloud Remap effect shader, for blending; null if it can't be had (RoR2 hooks
        /// Shader.Find and throws for shaders not in its table).
        public static Shader CloudRemap()
        {
            if (cloudLooked) return cloud;
            cloudLooked = true;
            try { cloud = LegacyShaderAPI.Find("Hopoo Games/FX/Cloud Remap"); }
            catch (Exception e) { CommonLog.Warn($"no Cloud Remap shader ({e.Message}); see-through parts drawn solid"); }
            return cloud;
        }

        /// An unlit see-through material on Cloud Remap with its colour remap off: `tex` (white
        /// if null) times _TintColor, alpha blended or `additive`, times the vertex colours with
        /// `vertexColor`. `opacity` scales alpha only (not RGB). Null without Cloud Remap.
        public static Material SeeThrough(string name, Texture tex, bool additive, bool vertexColor,
            float opacity = 1f)
        {
            if (!CloudRemap()) return null;
            var g = new Material(CloudRemap()) { name = name, renderQueue = 3000 };
            g.SetTexture("_MainTex", tex ? tex : Texture2D.whiteTexture);
            g.SetColor("_TintColor", Color.white);
            g.SetFloat("_DisableRemapOn", 1f);
            g.EnableKeyword("DISABLEREMAP");
            g.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            g.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            g.SetFloat("_Boost", 1f);
            g.SetFloat("_AlphaBoost", Mathf.Clamp01(opacity));
            g.SetFloat("_AlphaBias", 0f);
            g.SetFloat("_InvFade", 100f);
            g.SetFloat("_Cull", 0f);
            g.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            if (vertexColor)
            {
                g.SetFloat("_VertexColorOn", 1f);
                g.EnableKeyword("VERTEXCOLOR");
            }
            return g;
        }
    }
}
