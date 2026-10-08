using RoR2;

namespace Ror2Common
{
    // RoR2's interact button (X on a pad, E on keyboard by default) may also be one of the
    // character's own buttons (Olimar's dismiss). A press made while RoR2 shows an interact
    // prompt (the drop pod's exit, a chest, a shrine) belongs to RoR2 until it's let go: the
    // character doesn't see it. A press already in use by the character stays the
    // character's when a prompt comes into view.
    //
    // RoR2 itself interacts on the press, and again every so often while the button stays
    // down (`InteractionDriver.FixedUpdate`: justPressed, or down once its cooldown runs out),
    // so a character's long hold that walks onto a prompt can still set it off.
    public sealed class InteractGuard
    {
        bool claimed, wasDown;

        /// True while the current press belongs to RoR2's prompt.
        public bool Claimed => claimed;

        /// Feed it the button every frame: `down`, held now; `prompt`, RoR2 shows an interact
        /// prompt now (see `PromptShown`). Returns the button as the character should see it:
        /// false while RoR2 has the press.
        public bool Claim(bool down, bool prompt)
        {
            if (!down) claimed = false;
            else if (!wasDown && prompt) claimed = true;
            wasDown = down;
            return down && !claimed;
        }

        /// Whether RoR2 shows `driver`'s interact prompt now, the question `ContextManager`
        /// asks: there's a best interactable and it isn't Disabled.
        public static bool PromptShown(InteractionDriver driver)
        {
            if (!driver) return false;
            var target = driver.FindBestInteractableObject();
            if (!target) return false;
            var it = target.GetComponent<IInteractable>();
            return it == null || it.GetInteractability(driver.interactor) != Interactability.Disabled;
        }
    }
}
