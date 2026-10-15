using System.Collections.Generic;
using System.Reflection;
using RoR2;

namespace Ror2Common
{
    // Items, buffs and private fields looked up by name at run time, not bound when the plugin
    // compiles: a RoR2 without one (an older build, or one that renamed it) reads as none and
    // nothing breaks, and a later build's new items can be named before the build this
    // compiles against has them. Names are RoR2's internal ones (`ItemDef.name`: "FallBoots",
    // "SecondarySkillMagazine").
    public static class ByName
    {
        static readonly Dictionary<string, ItemIndex> items = new Dictionary<string, ItemIndex>();
        static readonly Dictionary<string, BuffIndex> buffs = new Dictionary<string, BuffIndex>();

        public static ItemIndex Item(string name)
        {
            if (items.TryGetValue(name, out var i)) return i;
            if (ItemCatalog.itemCount == 0) return ItemIndex.None;
            i = ItemCatalog.FindItemIndex(name);
            items[name] = i;
            return i;
        }

        public static BuffIndex Buff(string name)
        {
            if (buffs.TryGetValue(name, out var b)) return b;
            if (BuffCatalog.buffCount == 0) return BuffIndex.None;
            b = BuffCatalog.FindBuffIndex(name);
            buffs[name] = b;
            return b;
        }

        /// How many of the item `body` carries; 0 when this RoR2 has no such item.
        public static int Count(CharacterBody body, string item)
        {
            var inv = body ? body.inventory : null;
            var i = Item(item);
            return inv && i != ItemIndex.None ? inv.GetItemCountEffective(i) : 0;
        }

        /// The sum over several items (one item renamed between builds, or a set).
        public static int Count(CharacterBody body, params string[] names)
        {
            int n = 0;
            foreach (var s in names) n += Count(body, s);
            return n;
        }

        public static bool Has(CharacterBody body, string buff)
        {
            var b = Buff(buff);
            return body && b != BuffIndex.None && body.HasBuff(b);
        }

        /// An instance field by name, public or not; null when this RoR2 hasn't got it.
        public static FieldInfo Field(System.Type type, string name) =>
            type?.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// A RoR2 type by full name; null when this RoR2 hasn't got it.
        public static System.Type Type(string fullName) => typeof(CharacterBody).Assembly.GetType(fullName);
    }
}
