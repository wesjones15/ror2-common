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
        /// The page this section sits on ("Skills", "Moveset"); null is the sheet's only page. A sheet whose
        /// sections name two or more pages is shown with a tab strip at a fixed font; one without is shown as one
        /// block that shrinks to fit, as before.
        public string Page;
        /// Width in reference pixels reserved for the input before the action column; 0 uses the default (170).
        public int InputColumn;
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

        /// The page that Pad() and Keys() put new sections on until it is changed (null: no pages).
        public string CurrentPage;

        public ControlSection Pad(string heading) { var s = new ControlSection(heading) { Page = CurrentPage }; Gamepad.Add(s); return s; }
        public ControlSection Keys(string heading) { var s = new ControlSection(heading) { Page = CurrentPage }; Keyboard.Add(s); return s; }
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

        /// The body whose sheet is being built right now (inside a provider call), else null. Lets a shared
        /// sheet builder find the body's components (the Melee move list) without changing the provider signature.
        public static CharacterBody SheetBody { get; private set; }

        internal static bool TrySheet(CharacterBody body, out ControlSheet sheet)
        {
            sheet = null;
            if (!body) return false;
            string name = BodyCatalog.GetBodyName(body.bodyIndex);
            if (name == null || !sheets.TryGetValue(name, out var provider)) return false;
            SheetBody = body;
            try { sheet = provider(body); }
            finally { SheetBody = null; }
            return sheet != null;
        }

        internal static bool Has(CharacterBody body) =>
            body && BodyCatalog.GetBodyName(body.bodyIndex) is string name && sheets.ContainsKey(name);
    }

    sealed class PauseControlsWindow : MonoBehaviour
    {
        const int CanvasOrder = 1000;
        const float Width = 540f, Height = 880f, Margin = 36f;
        const int ActionColumn = 170;
        // A paged sheet is drawn at this fixed size, never shrunk: the sheet's rows are split over views instead.
        const float PagedFont = 21f;
        const int MaxTabs = 6;
        static readonly Color gold = new Color(1f, .82f, .3f);
        static readonly Color dim = new Color(.55f, .62f, .68f);

        // One screenful of a paged sheet: a part of a named page.
        sealed class View
        {
            public string Page, Text;
            public int Part, Parts;
        }

        GameObject root;
        Image panel;
        RectTransform window, controlsRect;
        RawImage portrait;
        HGTextMeshProUGUI title, subtitle, controls, footer, hint;
        readonly HGTextMeshProUGUI[] tabs = new HGTextMeshProUGUI[MaxTabs];
        CharacterBody shown;
        bool shownPad, built;
        readonly List<View> views = new List<View>();
        readonly List<string> pageNames = new List<string>();
        int view;
        string wantedPage;
        bool padLeftHeld, padRightHeld;

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
            if (views.Count > 1) PollPaging(user);
        }

        static bool UsingPad(LocalUser user)
        {
            var last = user.inputPlayer != null ? user.inputPlayer.controllers.GetLastActiveController() : null;
            return last != null && last.type == ControllerType.Joystick;
        }

        // ---- Paging (only sheets that name pages) ----------------------------------------------------------------

        void PollPaging(LocalUser user)
        {
            int step = 0;
            if (UnityEngine.Input.GetKeyDown(KeyCode.E) || UnityEngine.Input.GetKeyDown(KeyCode.PageDown)) step = 1;
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Q) || UnityEngine.Input.GetKeyDown(KeyCode.PageUp)) step = -1;
            var last = user.inputPlayer != null ? user.inputPlayer.controllers.GetLastActiveController() : null;
            if (last != null && last.type == ControllerType.Joystick && last.ImplementsTemplate<IGamepadTemplate>())
            {
                var template = last.GetTemplate<IGamepadTemplate>();
                bool left = template.leftBumper.value, right = template.rightBumper.value;
                if (right && !padRightHeld) step = 1;
                else if (left && !padLeftHeld) step = -1;
                padLeftHeld = left;
                padRightHeld = right;
            }
            if (step != 0) Show((view + step + views.Count) % views.Count);
            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                var mouse = (Vector2)UnityEngine.Input.mousePosition;
                for (int i = 0; i < pageNames.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(tabs[i].rectTransform, mouse, null)) continue;
                    int first = views.FindIndex(v => v.Page == pageNames[i]);
                    if (first >= 0) Show(first);
                    break;
                }
            }
        }

        void Show(int index)
        {
            view = index;
            var current = views[index];
            wantedPage = current.Page;
            controls.text = current.Text;
            // The tab strip: every page, the open one in gold (with "2/3" when it needs more than one screen).
            float x = 0f;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool used = i < pageNames.Count;
                tabs[i].gameObject.SetActive(used);
                if (!used) continue;
                bool open = pageNames[i] == current.Page;
                string label = pageNames[i] + (open && current.Parts > 1 ? " " + (current.Part + 1) + "/" + current.Parts : "");
                tabs[i].text = "<color=#" + (open ? "ffd24d" : "8a9aa6") + ">" + label + "</color>";
                float w = tabs[i].GetPreferredValues(tabs[i].text, 400f, 0f).x;
                tabs[i].rectTransform.anchoredPosition = new Vector2(24f + x, -170f);
                tabs[i].rectTransform.sizeDelta = new Vector2(w + 4f, 34f);
                x += w + 26f;
            }
        }

        // ---- Filling the window ----------------------------------------------------------------------------------

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
            string otherLine = other ? "<color=#9aa4ad>" + (pad ? "Keyboard and mouse" : "Gamepad") + "</color>\n" : "";
            footer.text = sheet.Footer ?? "";

            pageNames.Clear();
            foreach (var section in sections)
                if (!pageNames.Contains(section.Page ?? "")) pageNames.Add(section.Page ?? "");
            views.Clear();
            if (pageNames.Count < 2)
            {
                FillSingle(sections, otherLine);
                return;
            }
            FillPaged(sections, otherLine);
            int start = views.FindIndex(v => v.Page == wantedPage);
            Show(start >= 0 ? start : 0);
        }

        // One block, shrunk to fit if long (Olimar and Mario).
        void FillSingle(List<ControlSection> sections, string otherLine)
        {
            var text = new StringBuilder(1024).Append(otherLine);
            foreach (var section in sections) AppendSection(text, section);
            Layout(false);
            controls.text = text.ToString();
        }

        // Pages as tabs: a fixed font, each page split over as many screens as its rows need.
        void FillPaged(List<ControlSection> sections, string otherLine)
        {
            Layout(true);
            float width = controlsRect.sizeDelta.x;
            float line = controls.GetPreferredValues("Ag", width, 0f).y;
            float spacer = line * .4f;
            float budget = controlsRect.sizeDelta.y - line * .6f;
            var made = new List<View>();
            foreach (var name in pageNames)
            {
                var text = new StringBuilder(1024);
                float used = 0f;
                var pageViews = new List<View>();
                if (made.Count == 0 && otherLine.Length > 0) { text.Append(otherLine); used += line; }
                Action flush = () =>
                {
                    if (text.Length == 0) return;
                    pageViews.Add(new View { Page = name, Text = text.ToString() });
                    text.Length = 0;
                    used = 0f;
                };
                foreach (var section in sections)
                {
                    if ((section.Page ?? "") != name) continue;
                    var chunks = Chunks(section, width);
                    float total = spacer;
                    foreach (var chunk in chunks) total += chunk.Height;
                    // A section that fits on a fresh screen but not on what is left starts the next screen.
                    if (used > 0f && used + total > budget && total <= budget) flush();
                    foreach (var chunk in chunks)
                    {
                        if (used > 0f && used + chunk.Height > budget) flush();
                        text.Append(chunk.Markup);
                        used += chunk.Height;
                    }
                    text.Append("<margin-left=0><line-indent=0><size=40%>\n</size>");
                    used += spacer;
                }
                flush();
                for (int i = 0; i < pageViews.Count; i++) { pageViews[i].Part = i; pageViews[i].Parts = pageViews.Count; }
                made.AddRange(pageViews);
            }
            views.AddRange(made);
        }

        // The window's text area: the whole body when unpaged, or below a tab strip when paged.
        void Layout(bool paged)
        {
            controlsRect.anchoredPosition = new Vector2(24f, paged ? -208f : -186f);
            controlsRect.sizeDelta = new Vector2(Width - 48f, paged ? Height - 208f - 62f : Height - 250f);
            controls.enableAutoSizing = !paged;
            controls.fontSizeMax = 21f;
            controls.fontSizeMin = 13f;
            controls.fontSize = paged ? PagedFont : 21f;
            hint.gameObject.SetActive(paged);
            if (!paged) foreach (var tab in tabs) tab.gameObject.SetActive(false);
        }

        struct Chunk { public string Markup; public float Height; }

        // A section as separable pieces: the heading with its first row, then one piece per row, each measured.
        List<Chunk> Chunks(ControlSection section, float width)
        {
            var list = new List<Chunk>();
            string heading = "";
            if (!string.IsNullOrEmpty(section.Heading))
                heading = "<margin-left=0><line-indent=0><color=#ffd24d><size=108%>" + section.Heading.ToUpperInvariant() + "</size></color>\n";
            for (int i = 0; i < section.Rows.Count; i++)
            {
                var sb = new StringBuilder();
                if (i == 0) sb.Append(heading);
                AppendRow(sb, section, section.Rows[i]);
                string markup = sb.ToString();
                list.Add(new Chunk { Markup = markup, Height = controls.GetPreferredValues(markup.TrimEnd('\n'), width, 0f).y });
            }
            if (section.Rows.Count == 0 && heading.Length > 0)
                list.Add(new Chunk { Markup = heading, Height = controls.GetPreferredValues(heading.TrimEnd('\n'), width, 0f).y });
            return list;
        }

        static void AppendSection(StringBuilder text, ControlSection section)
        {
            text.Append("<margin-left=0><line-indent=0>");
            if (!string.IsNullOrEmpty(section.Heading))
                text.Append("<color=#ffd24d><size=108%>").Append(section.Heading.ToUpperInvariant()).Append("</size></color>\n");
            foreach (var row in section.Rows) AppendRow(text, section, row);
            text.Append("<margin-left=0><line-indent=0><size=40%>\n</size>");
        }

        // A hanging indent: the binding starts the first line, the action sits in a column, and
        // a long action that wraps keeps to that column instead of going back under the binding.
        static void AppendRow(StringBuilder text, ControlSection section, ControlRow row)
        {
            int column = section.InputColumn > 0 ? section.InputColumn : ActionColumn;
            text.Append("<margin-left=").Append(column).Append("><line-indent=-").Append(column)
                .Append("><color=#8fd0ff>").Append(row.Input).Append("</color><pos=").Append(column)
                .Append(">").Append(row.Action).Append('\n');
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
            controlsRect = controls.rectTransform;
            controls.richText = true;
            controls.enableWordWrapping = true;
            controls.overflowMode = TMPro.TextOverflowModes.Truncate;
            // An unpaged long move list shrinks to fit instead of being cut off; a paged sheet never does.
            controls.enableAutoSizing = true;
            controls.fontSizeMax = 21f;
            controls.fontSizeMin = 13f;
            footer = Label("Footer", window, new Vector2(24f, -(Height - 56f)), new Vector2(Width - 48f, 44f), 18f, new Color(.7f, .78f, .84f));
            footer.enableWordWrapping = true;
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i] = Label("Tab " + i, window, new Vector2(24f, -170f), new Vector2(100f, 34f), PagedFont, Color.white);
                tabs[i].gameObject.SetActive(false);
            }
            hint = Label("Page hint", window, new Vector2(Width - 224f, -176f), new Vector2(200f, 26f), 15f, dim);
            hint.alignment = TMPro.TextAlignmentOptions.TopRight;
            hint.text = "Q / E  or  LB / RB";
            hint.gameObject.SetActive(false);
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
