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

        // A driver's per-frame lookup: keeps `cached` while it still owns the body, else searches again.
        public static LocalUser Refresh(LocalUser cached, CharacterBody body) =>
            cached != null && body && cached.cachedBody == body ? cached : For(body);
    }
}
