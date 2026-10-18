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
        /// Posed body anchor in display space, excluding the current draw offset.
        Vector3 PoseAnchor { get; }
        Vector3 DrawOffset { set; }
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
            if (!intro || audioFinished) return;
            if (source.IntroFinished)
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
        public void Draw(Transform display){if(source is IDisplayEffects effects)effects.Draw(display);}
        public void Release()
        {
            audio?.Dispose(); audio = null;
            var old = source; source = null; old?.Dispose();
        }
    }
}
