using System;
using System.IO;
using System.Text;

namespace Ror2Common
{
    // The first-run flow every character plugin shares for its VR arms: each core cuts its bodies'
    // forearms and hands into `<cache>/vr_arms/` (libcommon's vr_arms; sms_bake_vr_arms,
    // ssbm_bake_vr_arms, pik_bake_vr_arms), and the views read those cuts back when they draw the
    // arms. A bake loads every body, so it runs once per `tag` (the plugin's version and the asset
    // folders it was given): a stamp beside the cuts says it ran. A later run with a new tag (a
    // new version, a disc found since) bakes again, and the cores keep the cuts already there.
    // Call it from the plugin's asset worker thread, after extraction; it never throws.
    public static class VrArmsBake
    {
        /// `bake` writes its notes into the buffer and returns nonzero on success.
        public static void Run(string cache, string who, string tag, Func<byte[], uint, byte> bake)
        {
            try
            {
                if (string.IsNullOrEmpty(cache) || !Directory.Exists(cache)) return;
                string stamp = Path.Combine(Path.Combine(cache, "vr_arms"), who + ".baked");
                if (File.Exists(stamp) && File.ReadAllText(stamp) == tag) return;
                var msg = new byte[8192];
                if (bake(msg, (uint)msg.Length) == 0)
                {
                    CommonLog.Log?.LogWarning($"{who} VR arms not baked: {Text(msg)}");
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(stamp));
                File.WriteAllText(stamp, tag);
                string notes = Text(msg);
                CommonLog.Log?.LogInfo($"{who} VR arms: {(notes.Length == 0 ? "already cut" : notes)}");
            }
            catch (Exception e)
            {
                CommonLog.Log?.LogWarning($"{who} VR arms not baked: {e.Message}");
            }
        }

        static string Text(byte[] msg)
        {
            int n = Array.IndexOf(msg, (byte)0);
            return Encoding.UTF8.GetString(msg, 0, n < 0 ? msg.Length : n);
        }
    }
}
