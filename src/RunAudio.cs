using System;
using RoR2;

namespace Ror2Common
{
    /// Native audio streams outlive Unity scenes unless their host explicitly frees them.
    public static class RunAudio
    {
        public static bool Ended { get; private set; }

        public static void Initialize(Action reset)
        {
            void Stop()
            {
                Ended = true;
                SpawnMusic.StopAll();
                reset();
            }
            Run.onClientGameOverGlobal += (run, report) => Stop();
            Run.onRunDestroyGlobal += run => Stop();
            Run.onRunStartGlobal += run => { Stop(); Ended = false; };
            UnityEngine.Application.quitting += Stop;
        }
    }
}
