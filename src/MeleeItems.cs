namespace Ror2Common
{
    // RoR2 item tuning the generic Melee driver reads. The plugin sets these once from its
    // config (Items.*) in Awake; the shared `MeleeDriver` and every fighter read one value,
    // so the item wiring is not duplicated per fighter.
    public static class MeleeItems
    {
        /// Wax Quail's boost to a sprinting jump, times this (1: RoR2's).
        public static float QuailScale = 1f;
        /// H3AD-5T v2's landing blast, times this (1: RoR2's; 0 disables).
        public static float StompScale = 1f;
    }
}
