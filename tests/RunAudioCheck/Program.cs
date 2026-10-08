using System;
using Ror2Common;

class Program
{
    static void Main()
    {
        bool streamOpen = false;
        int closes = 0;
        RunAudio.Initialize(() => {
            if (!RunAudio.Ended) throw new Exception("Audio could reopen during teardown");
            streamOpen = false;
            closes++;
        });
        for (int run = 0; run < 3; run++)
        {
            RoR2.Run.Start();
            if (RunAudio.Ended || streamOpen) throw new Exception("Dirty new run");
            streamOpen = true;
            // Game over must stop audio while the Run still exists (results screen).
            RoR2.Run.GameOver();
            if (streamOpen || !RunAudio.Ended) throw new Exception("Audio survived game over");
            RoR2.Run.Destroy();
            if (streamOpen || !RunAudio.Ended) throw new Exception("Audio survived run destruction");
        }
        // Quitting directly to menu skips the game-over event.
        RoR2.Run.Start(); streamOpen = true; RoR2.Run.Destroy();
        if (streamOpen || !RunAudio.Ended) throw new Exception("Audio survived direct quit");
        UnityEngine.Application.Quit();
        if (closes != 12 || SpawnMusic.Stops != closes) throw new Exception("Incomplete cleanup sequence");
        Console.WriteLine("PASS: repeated runs, results screen, direct quit, shutdown, and sting cleanup");
    }
}

// Event-only substitutes: the actual event signatures are also checked by both game builds.
namespace RoR2
{
    class Run
    {
        public static event Action<Run> onRunStartGlobal, onRunDestroyGlobal;
        public static event Action<Run, object> onClientGameOverGlobal;
        public static void Start() => onRunStartGlobal?.Invoke(new Run());
        public static void Destroy() => onRunDestroyGlobal?.Invoke(new Run());
        public static void GameOver() => onClientGameOverGlobal?.Invoke(new Run(), null);
    }
}
namespace UnityEngine
{
    static class Application
    {
        public static event Action quitting;
        public static void Quit() => quitting?.Invoke();
    }
}
namespace Ror2Common
{
    static class SpawnMusic
    {
        public static int Stops;
        public static void StopAll() => Stops++;
    }
}
