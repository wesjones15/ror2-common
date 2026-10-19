using System;
using UnityEngine;

namespace Ror2Common
{
    public static class MeleePortrait
    {
        /// Loads the original character-select grid photo for `kind`. The name is
        /// only the Texture2D name shown in the inspector.
        public static Texture2D Load(string assetsDir, uint kind, string name)
        {
            var path = NativeLib.CString(assetsDir);
            if (MeleeNative.ssbm_portrait(path, kind, 0, out var info, null, 0) != 1)
                throw new InvalidOperationException(MeleeNative.Error);
            var rgba = new byte[checked(info.width * info.height * 4)];
            if (MeleeNative.ssbm_portrait(path, kind, 0, out info, rgba, (uint)rgba.Length) != 1)
                throw new InvalidOperationException(MeleeNative.Error);
            return Portrait.FromRgba(name, (int)info.width, (int)info.height, rgba);
        }
    }
}
