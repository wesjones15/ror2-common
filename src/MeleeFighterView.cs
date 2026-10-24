using System;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    /// The surface `MeleeEffects` needs from an object view: pose it, face it at
    /// the camera, and draw it through its `ModelView`.
    public interface IMeleeObjectView : IDisposable
    {
        IntPtr Handle { get; }
        ModelView Models { get; }
        void ObjectPose(float frame);
        void FaceCamera(Vector3 position, Quaternion rotation, float scale);
        uint TextureVersion(uint id);
    }

    /// The object view shared by every fighter: poses a native view/model, faces
    /// billboards at the camera, and exposes geometry through `ModelView`. The
    /// only per-fighter inputs are the assets dir and fighter kind.
    public sealed class MeleeFighterView : IViewSource, IAnimatedTextureSource, IMeleeObjectView
    {
        public IntPtr Handle { get; private set; }
        public ModelView Models { get; private set; }
        public Quaternion LocalRotation = Quaternion.identity;
        public Vector3 LocalPosition;
        readonly float[] overlay = new float[4];
        readonly float[] light = new float[8];
        /// Whether fighter views draw the colour programs' rim light (the Melee plugin's config sets it
        /// before any view is made; effects and articles never draw one).
        public static bool RimLight = true;
        /// How much brighter than the source's material colour fighter bodies are drawn. Every Melee fighter material
        /// has a diffuse colour of 0.70, which the source's stage lighting more than makes up for; RoR2's lighting
        /// does not (the Melee plugin's config sets it before any view is made). Effects and articles are not touched.
        public static float FighterBrightness = 1f;
        float brightness = 1f;
        readonly MaterialAlphaBuffer alpha = new MaterialAlphaBuffer();
        byte drawFlags;
        public ViewDraw[] InstanceDraws;

        public MeleeFighterView(string assetsDir, uint kind, uint costume)
            : this(MeleeNative.ssbm_view_new(NativeLib.CString(assetsDir), kind, Math.Min(costume, 4)), RimLight) { brightness = FighterBrightness; }
        public MeleeFighterView(string assetsDir, uint kind, uint article, uint state)
            : this(MeleeNative.ssbm_article_new(NativeLib.CString(assetsDir), kind, article, state)) { }
        public static MeleeFighterView Effect(string assetsDir, uint kind, uint model)
            => new MeleeFighterView(MeleeNative.ssbm_effect_new(NativeLib.CString(assetsDir), kind, model));
        /// Modular effect lookup: every fighter's effects are just extracted assets, not part of the
        /// body, so a move borrowed from another character still finds its cue. Tries the fighter's
        /// own bank first, then every other fighter's, and only fails when no bank has the model.
        public static MeleeFighterView EffectAny(string assetsDir, uint kind, uint model)
        {
            try { return Effect(assetsDir, kind, model); } catch (Exception) { }
            for (uint other = 0; other < 8; other++)
            {
                if (other == kind) continue;
                try { return Effect(assetsDir, other, model); } catch (Exception) { }
            }
            throw new InvalidOperationException("Melee effect model " + model + " is in no extracted fighter bank");
        }
        /// A fighter's alternate model alone (choice 1: Samus's morph ball, Yoshi's egg),
        /// for a laid egg or another prop that does not follow a live sim.
        public static MeleeFighterView FighterChoice(string assetsDir, uint kind, uint costume, uint choice)
            => new MeleeFighterView(MeleeNative.ssbm_view_new_choice(NativeLib.CString(assetsDir), kind, Math.Min(costume, 4), choice));
        public static MeleeFighterView Game(string assetsDir)
            => new MeleeFighterView(MeleeNative.ssbm_game_new(NativeLib.CString(assetsDir)));
        /// An item of the common item archive: the Super Scope held (0x15) or a level of its beam (0x26, state = level).
        public static MeleeFighterView Item(string assetsDir, uint item, uint state)
            => new MeleeFighterView(MeleeNative.ssbm_item_new(NativeLib.CString(assetsDir), item, state));
        public static MeleeFighterView EntryPlatform(string assetsDir)
            => new MeleeFighterView(MeleeNative.ssbm_entry_platform_new(NativeLib.CString(assetsDir)));
        public static MeleeFighterView Grapple(string assetsDir, uint part)
            => new MeleeFighterView(MeleeNative.ssbm_grapple_new(NativeLib.CString(assetsDir), part));

        MeleeFighterView(IntPtr handle, bool rim = false)
        {
            Handle = handle;
            if (Handle == IntPtr.Zero) throw new InvalidOperationException(MeleeNative.Error);
            Models = ModelView.Create(this, "Melee", translucentOpacity: 1f, rim: rim && MeleeNative.HasLight);
            if (Models == null) { Dispose(); throw new InvalidOperationException("Melee ModelView creation failed"); }
        }

        public void Pose(IntPtr sim, float dt)
        {
            if (MeleeNative.ssbm_view_update(Handle, sim, dt) != 1) throw new InvalidOperationException(MeleeNative.Error);
            drawFlags = MeleeNative.HasOverlay && MeleeNative.ssbm_sim_overlay(sim, overlay) == 1 ? (byte)1 : (byte)0;
            if (drawFlags != 0) Models.FlashColor = new Color(overlay[0], overlay[1], overlay[2], overlay[3]);
            // The additive light of a colour program (a lamp-type light is not drawn).
            Models.RimColor = MeleeNative.HasLight && MeleeNative.ssbm_sim_light(sim, light) == 1 && light[6] > 0.5f
                ? new Color(light[0], light[1], light[2], light[3]) : default;
        }
        public void IdlePose() { if (MeleeNative.ssbm_view_pose(Handle, NativeLib.CString("Wait"), 0) != 1) throw new InvalidOperationException(MeleeNative.Error); }
        public void ClipPose(string clip, float frame) { if (MeleeNative.ssbm_view_pose(Handle, NativeLib.CString(clip), frame) != 1) throw new InvalidOperationException(MeleeNative.Error); }
        public void ObjectPose(float frame) { if (MeleeNative.ssbm_object_update(Handle, frame) != 1) throw new InvalidOperationException(MeleeNative.Error); }
        public void FaceCamera(Vector3 position, Quaternion rotation, float scale)
        {
            var camera = GameSettings.PlayerCamera();
            if (!camera || scale <= 0) return;
            var local = Quaternion.Inverse(rotation) * (camera.position - position) / scale;
            local.x = -local.x;
            if (MeleeNative.ssbm_object_camera(Handle, new MeleeNative.Vec3(local.x, local.y, local.z)) != 1) throw new InvalidOperationException(MeleeNative.Error);
        }
        public uint ModelCount() => 1;
        public bool Model(uint id, out ViewModelInfo info) => MeleeNative.ssbm_view_model(Handle, out info) == 1;
        public uint Batches(uint id, ViewBatch[] output, uint max) => MeleeNative.ssbm_view_batches(Handle, output, max);
        public uint Indices(uint id, int[] output, uint max) => MeleeNative.ssbm_view_indices(Handle, output, max);
        public uint Uvs(uint id, float[] output, uint max) => MeleeNative.ssbm_view_uvs(Handle, output, max);
        public bool Texture(uint id, out ViewTexture info, byte[] rgba, uint len) => MeleeNative.ssbm_view_texture(Handle, id, out info, rgba, len) == 1;
        public uint Draws(ViewDraw[] output, uint max)
        {
            if (InstanceDraws != null)
            {
                if (output != null) Array.Copy(InstanceDraws, output, Math.Min(InstanceDraws.Length, (int)max));
                return (uint)InstanceDraws.Length;
            }
            if (output != null && max > 0) output[0] = new ViewDraw { key = 1, flags = drawFlags, pos = new ViewVec3(LocalPosition.x, LocalPosition.y, LocalPosition.z), rot_x = LocalRotation.x, rot_y = LocalRotation.y, rot_z = LocalRotation.z, rot_w = LocalRotation.w, alpha = 1 };
            return 1;
        }
        public IntPtr Positions(out uint count) => MeleeNative.ssbm_view_positions(Handle, out count);
        public IntPtr Normals(out uint count) => MeleeNative.ssbm_view_normals(Handle, out count);
        public IntPtr Colors(out uint count)
        {
            var colors = MeleeNative.ssbm_view_colors(Handle, out count);
            if (!MeleeNative.HasMaterialAlpha) return brightness == 1f ? colors : alpha.Resolve(colors, count, IntPtr.Zero, 0, brightness);
            var sources = MeleeNative.ssbm_view_alpha_sources(Handle, out uint n);
            return alpha.Resolve(colors, count, sources, n, brightness);
        }
        public IntPtr TextureIds(out uint count) => MeleeNative.ssbm_view_texture_ids(Handle, out count);
        public IntPtr FrameUvs(out uint count) => MeleeNative.ssbm_view_frame_uvs(Handle, out count);
        public uint TextureVersion(uint id) => MeleeNative.ssbm_view_texture_version(Handle, id);
        public void Dispose()
        {
            Models?.Dispose(); Models = null; alpha.Dispose();
            if (Handle != IntPtr.Zero) { MeleeNative.ssbm_view_free(Handle); Handle = IntPtr.Zero; }
        }
    }
}
