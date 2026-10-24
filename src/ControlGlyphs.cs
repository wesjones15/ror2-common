using Rewired;
using RoR2;
using RoR2.UI;

namespace Ror2Common
{
    /// RoR2's own button glyphs for a pause sheet. A physical pad button gets the same glyph
    /// RoR2's HUD draws for it (`GlyphOverride`), so a sheet names the control the native sim
    /// actually reads rather than a skill slot's rebindable action. A keyboard row gets the
    /// player's bound key for a RoR2 action. Both fall back to plain text when the game has no
    /// glyph for the button or no user is available.
    public static class ControlGlyphs
    {
        /// The glyph for a physical pad button (RT, X, ...), or `fallback`.
        public static string Pad(Player player, PadButton button, string fallback) =>
            GlyphOverride.PhysicalGlyph(player, button) ?? fallback;

        /// The pad's left-stick glyph (RoR2's movement action), or `fallback`.
        public static string Stick(MPEventSystem eventSystem, string fallback)
        {
            if (!eventSystem) return fallback;
            foreach (var action in new[] { "MoveHorizontal", "Move" })
            {
                var glyph = Glyphs.GetGlyphString(eventSystem, action, AxisRange.Full,
                    MPEventSystem.InputSource.Gamepad, true);
                if (!string.IsNullOrEmpty(glyph)) return glyph;
            }
            return fallback;
        }

        /// The player's key for a RoR2 input action ("PrimarySkill", "Interact"), or `fallback`.
        public static string Key(MPEventSystem eventSystem, string action, string fallback)
        {
            if (!eventSystem) return fallback;
            var glyph = Glyphs.GetGlyphString(eventSystem, action, AxisRange.Full,
                MPEventSystem.InputSource.MouseAndKeyboard, true);
            return string.IsNullOrEmpty(glyph) ? fallback : glyph;
        }
    }
}
