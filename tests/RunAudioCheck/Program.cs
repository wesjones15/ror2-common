using System;
using Ror2Common;

class Program
{
    static void Main(string[] args)
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
            // Game over must stop audio while the Run still exists (results screen), but let a
            // death sting finish; leaving the run stops that too.
            RoR2.Run.GameOver();
            if (streamOpen || !RunAudio.Ended) throw new Exception("Audio survived game over");
            if (SpawnMusic.LastDeathToo) throw new Exception("Game over cut the death sting");
            RoR2.Run.Destroy();
            if (streamOpen || !RunAudio.Ended) throw new Exception("Audio survived run destruction");
            if (!SpawnMusic.LastDeathToo) throw new Exception("Death sting survived the run");
        }
        // Quitting directly to menu skips the game-over event.
        RoR2.Run.Start(); streamOpen = true; RoR2.Run.Destroy();
        if (streamOpen || !RunAudio.Ended) throw new Exception("Audio survived direct quit");
        UnityEngine.Application.Quit();
        if (closes != 12 || SpawnMusic.Stops != closes) throw new Exception("Incomplete cleanup sequence");
        Console.WriteLine("PASS: repeated runs, results screen, direct quit, shutdown, and sting cleanup");
        if (Array.IndexOf(args, "--device") >= 0)
        {
            // Queue silence: exercise the real output device without playing a test tone.
            for (int i = 0; i < 3; i++)
            {
                using var output = new PcmOutput(new float[3200], 32000, 1f);
                output.Pause(true);
                output.Pause(false);
                if (i == 0)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(3);
                    while (!output.Finished && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(10);
                    if (!output.Finished) throw new Exception("Windows audio buffer never completed");
                }
                output.Dispose(); output.Dispose();
            }
            Console.WriteLine("PASS: Windows PCM completion, pause/resume, immediate stop, repeated disposal/reopen");
        }
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
        public static bool LastDeathToo;
        public static void StopAll(bool deathToo = true) { Stops++; LastDeathToo = deathToo; }
    }
}
