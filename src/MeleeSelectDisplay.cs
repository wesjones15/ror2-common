using System;
using UnityEngine;

namespace Ror2Common
{
    /// A Melee fighter supplies its native view and up-special execution. The
    /// shared character-select rule is up-B (LB+B), including its source sounds.
    public interface IMeleeSelectSource : IDisposable
    {
        ModelView Models { get; }
        float Scale { get; }
        int AudioRate { get; }
        bool UpSpecialFinished { get; }
        /// Posed body anchor in display space, excluding the current draw offset.
        Vector3 PoseAnchor { get; }
        Vector3 DrawOffset { set; }
        void BeginUpSpecial();
        void Step(float dt);
        bool RenderAudio(float[] stereo);
    }

    /// Plays up-special once per selection and releases its PCM device on exit.
    /// Portraits use the same source with intro=false: idle pose, no sound device.
    public sealed class MeleeSelectDisplay : IDisplayActor, IDisplayEffects
    {
        IMeleeSelectSource source;
        PcmStream audio;
        readonly bool intro;
        bool started, audioFinished;
        Vector3 anchor;
        float repeatAt;
        bool anchorSet;
        public MeleeSelectDisplay(IMeleeSelectSource source, bool intro = true)
        { this.source = source ?? throw new ArgumentNullException(nameof(source)); this.intro = intro; }
        public ModelView Models => source?.Models;
        public float Scale => source?.Scale ?? 1f;
        public void Step(float dt, Transform display)
        {
            if (source == null) return;
            if(intro&&audioFinished){repeatAt+=dt;if(repeatAt>=1f){started=false;audioFinished=false;repeatAt=0;}}
            if (!started)
            {
                started = true;
                if (intro)
                {
                    if(!anchorSet){anchor = source.PoseAnchor;anchorSet=true;}
                    source.BeginUpSpecial();
                    try { audio = new PcmStream(source.AudioRate); }
                    catch (Exception e) { CommonLog.Warn("Melee selection audio unavailable: " + e.Message); }
                }
            }
            source.Step(dt);
            // Source jump/root translation must not take the intro out of the
            // lobby camera. Keep its body anchor at the initial idle location.
            if (intro) source.DrawOffset = anchor - source.PoseAnchor;
            if (!intro || audioFinished) return;
            if (source.UpSpecialFinished)
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
