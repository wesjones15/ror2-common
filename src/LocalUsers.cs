using RoR2;

namespace Ror2Common
{
    // The local player who controls a body: null for another player's body, an AI's or no body.
    public static class LocalUsers
    {
        public static LocalUser For(CharacterBody body)
        {
            if (!body) return null;
            foreach (var u in LocalUserManager.readOnlyLocalUsersList)
                if (u != null && u.cachedBody == body) return u;
            return null;
        }

        /// The body the first local player's camera follows while it is another player's (they are
        /// spectating, dead or not); null when they watch their own body, a drone they control, or nothing.
        public static CharacterBody Spectated()
        {
            var user = LocalUserManager.GetFirstLocalUser();
            var rig = user?.cameraRigController;
            var target = rig ? rig.targetBody : null;
            return target && For(target) == null ? target : null;
        }

        /// `body` is the one the local player is spectating (see [`Spectated`]): its mod's HUD is theirs to see.
        public static bool IsSpectated(CharacterBody body) => body && Spectated() == body;

        // A driver's per-frame lookup: keeps `cached` while it still owns the body, else searches again.
        public static LocalUser Refresh(LocalUser cached, CharacterBody body) =>
            cached != null && body && cached.cachedBody == body ? cached : For(body);
    }
}
