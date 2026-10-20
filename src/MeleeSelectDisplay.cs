using System;
using UnityEngine;

namespace Ror2Common
{
    /// A Melee fighter supplies its native view and chosen selection move.
    /// Each character owns the move; the shared presenter owns one-shot timing.
    public interface IMeleeSelectSource : IDisposable
    {
        ModelView Models { get; }
        float Scale { get; }
        int AudioRate { get; }
        bool IntroFinished { get; }
        /// Whether any intro audio (the announcer name call, the up-B cue) is
        /// still playing. The presenter keeps its stream alive until this is
        /// false so a name call is never cut off by the intro finishing first.
        bool AudioActive { get; }
        /// Posed body anchor in display space, excluding the current draw offset.
        Vector3 PoseAnchor { get; }
        Vector3 DrawOffset { set; }
        /// Current vertical rise of the posed fighter above its resting floor in
        /// metres, for the presenter to pan the preview camera and keep an
        /// airborne intro on screen. Zero for grounded/idle fighters.
        float RiseMetres { get; }
        void BeginIntro();
        void Step(float dt);
        bool RenderAudio(float[] stereo);
    }

    /// Plays the fighter's intro once, then lets it idle on the UI clock. Releases
    /// PCM on completion/exit. A silent idle source can use intro=false.
    public sealed class MeleeSelectDisplay : IDisplayActor, IDisplayEffects
    {
        IMeleeSelectSource source;
        PcmStream audio;
        readonly bool intro;
        bool started, audioFinished;
        Camera frameCamera;
        float appliedRise;
        bool frameActive;
        public MeleeSelectDisplay(IMeleeSelectSource source, bool intro = true)
        { this.source = source ?? throw new ArgumentNullException(nameof(source)); this.intro = intro; }
        public ModelView Models => source?.Models;
        public float Scale => source?.Scale ?? 1f;
        public void Step(float dt, Transform display)
        {
            if (source == null) return;
            if (!started)
            {
                started = true;
                if (intro)
                {
                    source.BeginIntro();
                    try { audio = new PcmStream(source.AudioRate); }
                    catch (Exception e) { CommonLog.Warn("Melee selection audio unavailable: " + e.Message); }
                }
            }
            source.Step(dt);
            // Leave the authored vertical root motion visible. The fighter starts
            // and finishes on its floor; an airborne intro may rise between.
            if (intro) source.DrawOffset = Vector3.zero;
            if (intro) FramePreview(source.RiseMetres);
            if (!intro || audioFinished) return;
            // The intro move can finish before the announcer name call does (a
            // short up-B); keep the stream alive until the mixer is silent so the
            // name plays its full length.
            if (source.IntroFinished && !source.AudioActive)
            {
                audio?.Dispose(); audio = null; audioFinished = true;
                return;
            }
            if (audio != null)
                try { audio.Pump(source.RenderAudio, GameSettings.SfxVolume(), false); }
                catch (Exception e)
                {
                    CommonLog.Warn("Melee selection audio stopped: " + e.Message);
                    audio.Dispose(); audio = null; audioFinished = true;
                }
        }
        // Pan the menu preview camera up as an airborne intro rises and back down
        // as it returns, so the fighter stays on screen for the whole move. The
        // offset is applied additively and undone on release, so it never fights
        // RoR2's own camera placement.
        void FramePreview(float riseMetres)
        {
            if (!frameCamera) frameCamera = Camera.main;
            if (!frameCamera) return;
            float next = Mathf.Lerp(appliedRise, Mathf.Max(0f, riseMetres),
                Mathf.Clamp01(Time.unscaledDeltaTime * 18f));
            frameCamera.transform.position += Vector3.up * (next - appliedRise);
            appliedRise = next;
            frameActive = true;
        }
        void RestoreCamera()
        {
            if (frameCamera && frameActive && appliedRise != 0f)
                frameCamera.transform.position -= Vector3.up * appliedRise;
            appliedRise = 0f;
            frameActive = false;
        }
        public void Draw(Transform display){if(source is IDisplayEffects effects)effects.Draw(display);}
        public void Release()
        {
            RestoreCamera();
            audio?.Dispose(); audio = null;
            var old = source; source = null; old?.Dispose();
        }
    }
}
