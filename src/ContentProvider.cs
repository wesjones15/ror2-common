using System;
using System.Collections;
using RoR2.ContentManagement;

namespace Ror2Common
{
    // A plugin's content (survivors, bodies, entity states) through RoR2's own content-pack
    // hook, without R2API: `ContentProvider.Register(guid, pack => { ... })` in Awake.
    public sealed class ContentProvider : IContentPackProvider
    {
        readonly ContentPack pack = new ContentPack();
        readonly Action<ContentPack> fill;

        public string identifier { get; }

        ContentProvider(string id, Action<ContentPack> fill)
        {
            identifier = id;
            this.fill = fill;
        }

        /// `fill` runs once, while RoR2 loads static content (LegacyResourcesAPI works there).
        public static void Register(string id, Action<ContentPack> fill) =>
            ContentManager.collectContentPackProviders += add => add(new ContentProvider(id, fill));

        public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
        {
            try { fill(pack); }
            catch (Exception e) { CommonLog.Error($"{identifier}: content failed to load: {e}"); }
            yield break;
        }

        public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
        {
            ContentPack.Copy(pack, args.output);
            yield break;
        }

        public IEnumerator FinalizeAsync(FinalizeAsyncArgs args)
        {
            yield break;
        }
    }
}
