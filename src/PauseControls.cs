using System;
using System.Collections.Generic;
using System.Text;
using Rewired;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Ror2Common
{
    /// Whether RoR2's pause menu is up: the game is paused, or (in multiplayer, where it never pauses) the
    /// pause screen is open. IMGUI drawn while it is up covers the menu, so characters hide it then.
    public static class PauseMenu
    {
        public static bool IsOpen => PauseManager.isPaused || PauseScreenController.instancesList.Count > 0;
    }

    /// A row of a control sheet: what is pressed, and what it does.
    public struct ControlRow
    {
        public string Input, Action;
        public ControlRow(string input, string action) { Input = input; Action = action; }
    }

    /// A titled group of rows ("Movement", "In the Onion's beam").
    public sealed class ControlSection
    {
        public readonly string Heading;
        public readonly List<ControlRow> Rows = new List<ControlRow>();
        public ControlSection(string heading) { Heading = heading; }

        /// Adds a row; returns this for chaining.
        public ControlSection Add(string input, string action)
        {
            Rows.Add(new ControlRow(input, action));
            return this;
        }
    }

    /// What the pause window shows for a character: his controls for a gamepad and for keyboard and mouse
    /// (either may be empty; the window shows the set for the last device used, else the other).
    public sealed class ControlSheet
    {
        /// Shown under the name ("Sunshine's plumber with FLUDD"); optional.
        public string Subtitle;
        public readonly List<ControlSection> Gamepad = new List<ControlSection>();
        public readonly List<ControlSection> Keyboard = new List<ControlSection>();
        /// Shown at the foot of the window; optional.
        public string Footer;

        public ControlSection Pad(string heading) { var s = new ControlSection(heading); Gamepad.Add(s); return s; }
        public ControlSection Keys(string heading) { var s = new ControlSection(heading); Keyboard.Add(s); return s; }
    }

    // The pause window for modded characters: while the pause menu is up and the local player is a character
    // that registered a sheet, a window at the right edge (clear of the menu's buttons) shows his name, portrait and
    // controls, including combinations. It is a uGUI canvas in RoR2's own font, so its panel and its text are
    // drawn in order and nothing covers the pause menu.
    //
    // Use, from the plugin's Awake (every plugin that includes ror2-common has its own copy, and each shows only
    // its own characters):
    //     PauseControls.Install(gameObject);
    //     PauseControls.Register("SamusBody", body => MySheet());      // the catalog name of the body
    //
    // The provider is called when the window opens (and when the player switches between gamepad and
    // keyboard), so a sheet can reflect the current control scheme or settings. Name and portrait come from
    // the body (its display name and CharacterBody.portraitIcon).
    public static class PauseControls
    {
        static readonly Dictionary<string, Func<CharacterBody, ControlSheet>> sheets = new Dictionary<string, Func<CharacterBody, ControlSheet>>();
        static PauseControlsWindow window;

        /// Shows `sheet`'s window for the body whose catalog name is `bodyName` ("MarioTestBody").
        public static void Register(string bodyName, Func<CharacterBody, ControlSheet> sheet) => sheets[bodyName] = sheet;

        /// Adds the window to `host` (once).
        public static void Install(GameObject host)
        {
            if (!window) window = host.AddComponent<PauseControlsWindow>();
        }

        internal static bool TrySheet(CharacterBody body, out ControlSheet sheet)
        {
            sheet = null;
            if (!body) return false;
            string name = BodyCatalog.GetBodyName(body.bodyIndex);
            return name != null && sheets.TryGetValue(name, out var provider) && (sheet = provider(body)) != null;
        }

        internal static bool Has(CharacterBody body) =>
            body && BodyCatalog.GetBodyName(body.bodyIndex) is string name && sheets.ContainsKey(name);
    }

    sealed class PauseControlsWindow : MonoBehaviour
    {
        const int CanvasOrder = 1000;
        const float Width = 540f, Height = 880f, Margin = 36f;
        const int ActionColumn = 170;
        static readonly Color gold = new Color(1f, .82f, .3f);

        GameObject root;
        Image panel;
        RectTransform window;
        RawImage portrait;
        HGTextMeshProUGUI title, subtitle, controls, footer;
        CharacterBody shown;
        bool shownPad, built;

        void Update()
        {
            var user = LocalUserManager.GetFirstLocalUser();
            var body = user != null ? user.cachedBody : null;
            if (!PauseMenu.IsOpen || !PauseControls.Has(body))
            {
                if (root) root.SetActive(false);
                shown = null;
                return;
            }
            bool pad = UsingPad(user);
            Build();
            root.SetActive(true);
            // On a narrow screen the right half is smaller: shrink the window to stay clear of the menu's
            // buttons, which sit in the middle (about 280 reference pixels either side).
            float referenceWidth = 1080f * Screen.width / Mathf.Max(1, Screen.height);
            float room = referenceWidth * .5f - 280f - Margin;
            window.localScale = Vector3.one * Mathf.Clamp(room / Width, .5f, 1f);
            if (shown != body || shownPad != pad || !portrait.texture)
            {
                shown = body;
                shownPad = pad;
                Fill(body, pad);
            }
        }

        static bool UsingPad(LocalUser user)
        {
            var last = user.inputPlayer != null ? user.inputPlayer.controllers.GetLastActiveController() : null;
            return last != null && last.type == ControllerType.Joystick;
        }

        void Fill(CharacterBody body, bool pad)
        {
            if (!PauseControls.TrySheet(body, out var sheet)) return;
            title.text = body.GetDisplayName();
            subtitle.text = sheet.Subtitle ?? "";
            portrait.texture = body.portraitIcon;
            portrait.color = body.portraitIcon ? Color.white : Color.clear;
            var sections = pad ? sheet.Gamepad : sheet.Keyboard;
            bool other = false;
            if (sections.Count == 0) { sections = pad ? sheet.Keyboard : sheet.Gamepad; other = sections.Count > 0; }
            var text = new StringBuilder(1024);
            if (other) text.Append("<color=#9aa4ad>").Append(pad ? "Keyboard and mouse" : "Gamepad").Append("</color>\n");
            foreach (var section in sections)
            {
                text.Append("<margin-left=0><line-indent=0>");
                if (!string.IsNullOrEmpty(section.Heading))
                    text.Append("<color=#ffd24d><size=108%>").Append(section.Heading.ToUpperInvariant()).Append("</size></color>\n");
                // A hanging indent: the binding starts the first line, the action sits in a column at ActionColumn, and
                // a long action that wraps keeps to that column instead of going back under the binding.
                foreach (var row in section.Rows)
                    text.Append("<margin-left=").Append(ActionColumn).Append("><line-indent=-").Append(ActionColumn)
                        .Append("><color=#8fd0ff>").Append(row.Input).Append("</color><pos=").Append(ActionColumn)
                        .Append(">").Append(row.Action).Append('\n');
                text.Append("<margin-left=0><line-indent=0><size=40%>\n</size>");
            }
            controls.text = text.ToString();
            footer.text = sheet.Footer ?? "";
        }

        void Build()
        {
            if (built) return;
            built = true;
            root = new GameObject("Pause controls window", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = CanvasOrder;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            window = Rect("Window", root.transform, new Vector2(1f, .5f), new Vector2(-Margin, 0f), new Vector2(Width, Height));
            panel = window.gameObject.AddComponent<Image>();
            panel.color = new Color(.035f, .05f, .07f, .93f);
            panel.raycastTarget = false;
            var bar = Rect("Accent", window, new Vector2(1f, 1f), Vector2.zero, new Vector2(Width, 5f), new Vector2(1f, 1f));
            var accent = bar.gameObject.AddComponent<Image>();
            accent.color = gold;
            accent.raycastTarget = false;

            var face = Rect("Portrait", window, new Vector2(0f, 1f), new Vector2(24f, -26f), new Vector2(132f, 132f), new Vector2(0f, 1f));
            portrait = face.gameObject.AddComponent<RawImage>();
            portrait.raycastTarget = false;
            title = Label("Name", window, new Vector2(172f, -30f), new Vector2(Width - 196f, 56f), 42f, Color.white);
            subtitle = Label("Subtitle", window, new Vector2(172f, -92f), new Vector2(Width - 196f, 60f), 21f, new Color(.7f, .78f, .84f));
            subtitle.enableWordWrapping = true;
            controls = Label("Controls", window, new Vector2(24f, -186f), new Vector2(Width - 48f, Height - 250f), 21f, Color.white);
            controls.richText = true;
            controls.enableWordWrapping = true;
            controls.overflowMode = TMPro.TextOverflowModes.Truncate;
            footer = Label("Footer", window, new Vector2(24f, -(Height - 56f)), new Vector2(Width - 48f, 44f), 18f, new Color(.7f, .78f, .84f));
            footer.enableWordWrapping = true;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static HGTextMeshProUGUI Label(string name, Transform parent, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            var rect = Rect(name, parent, new Vector2(0f, 1f), position, size, new Vector2(0f, 1f));
            var text = rect.gameObject.AddComponent<HGTextMeshProUGUI>();
            text.raycastTarget = false;
            text.richText = true;
            text.fontSize = fontSize;
            text.color = color;
            text.enableWordWrapping = false;
            text.alignment = TMPro.TextAlignmentOptions.TopLeft;
            return text;
        }

        void OnDestroy()
        {
            if (root) Destroy(root);
        }
    }
}
