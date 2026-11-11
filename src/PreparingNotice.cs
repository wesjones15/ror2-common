using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ror2Common
{
    /// A notice, never a block: while a plugin is still preparing its game files in the background (a first launch extracts them, builds caches and bakes
    /// VR arms) the player can go on and start. Each plugin attaches one; several that are busy at once stack down the top of the screen.
    public sealed class PreparingNotice : MonoBehaviour
    {
        static readonly List<PreparingNotice> all = new List<PreparingNotice>();
        Func<bool> busy;
        string text;
        GUIStyle style;
        bool logged;

        /// `game` names what is preparing ("Mario"), `files` the files ("Sunshine"), `consequence` what the player will see until it is done.
        public static PreparingNotice Attach(GameObject owner, string game, string files, string consequence, Func<bool> busy)
        {
            var notice = owner.AddComponent<PreparingNotice>();
            notice.busy = busy;
            notice.text = $"{game}: the {files} files are still being prepared.\nYou can start now; {consequence}";
            return notice;
        }

        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() => all.Remove(this);

        void OnGUI()
        {
            if (busy == null || !busy()) return;
            if (!logged)
            {
                logged = true;
                CommonLog.Info("Preparing notice shown (nothing is blocked): " + text.Replace("\n", " "));
            }
            if (style == null)
                style = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Color.white } };
            int row = 0;
            foreach (var other in all)
            {
                if (other == this) break;
                if (other.busy != null && other.busy()) row++;
            }
            float width = Mathf.Min(760f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - width) * .5f, 12f + row * 76f, width, 70f), text, style);
        }
    }
}
