using System;
using RoR2;

namespace Ror2Common
{
    /// Native audio streams outlive Unity scenes unless their host explicitly frees them.
    /// `reset` runs at game over, when the run is left, and on quitting. A game over (which
    /// comes the moment the last player dies) lets death stings finish; leaving stops them.
    public static class RunAudio
    {
        public static bool Ended { get; private set; }

        public static void Initialize(Action reset)
        {
            void Stop(bool deathToo)
            {
                Ended = true;
                SpawnMusic.StopAll(deathToo);
                reset();
            }
            Run.onClientGameOverGlobal += (run, report) => Stop(false);
            Run.onRunDestroyGlobal += run => Stop(true);
            Run.onRunStartGlobal += run => { Stop(true); Ended = false; };
            UnityEngine.Application.quitting += () => Stop(true);
        }
    }
}
