using System;
using System.Linq;
using Mono.Cecil;
using Ror2Common;
using RoR2.Skills;
using UnityEngine;

static class Program
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Main(string[] args)
    {
        var mario = new GameObject(); var olimar = new GameObject(); var other = new GameObject();
        var baseImage = new Sprite(); var luigiImage = new Sprite(); var piantissimoImage = new Sprite(); var piantaImage = new Sprite();
        var skill = new SkillDef { icon = baseImage }; var air = new SkillDef { icon = baseImage };
        SkinPresentation.Register(mario, 0, "Mario"); SkinPresentation.Register(mario, 1, "Luigi"); SkinPresentation.Register(mario, 2, "Il Piantissimo");
        // Louie is unavailable here: Pianta is index 1, despite native id 2.
        SkinPresentation.Register(olimar, 0, "Olimar"); SkinPresentation.Register(olimar, 1, "Pianta");
        SkinPresentation.SetIcon(mario, 1, skill, luigiImage); SkinPresentation.SetIcon(mario, 2, skill, piantissimoImage);
        SkinPresentation.SetIcon(olimar, 1, skill, piantaImage);
        Check(SkinPresentation.NameToken(mario, 2) == "Il Piantissimo" && SkinPresentation.NameToken(olimar, 1) == "Pianta", "names mixed native ids and loadout indices");
        Check(SkinPresentation.Icon(mario, 1, skill) == luigiImage && SkinPresentation.Icon(mario, 2, skill) == piantissimoImage,
            "two players sharing a SkillDef cannot select independent skin icons");
        Check(SkinPresentation.Icon(olimar, 1, skill) == piantaImage, "body registration leaked between plugins");
        Check(SkinPresentation.Icon(mario, 1, air) == baseImage && SkinPresentation.Icon(mario, 0, skill) == baseImage,
            "shared artwork or default skin lost its fallback");
        Check(SkinPresentation.NameToken(other, 1) == null && SkinPresentation.Icon(other, 1, skill) == baseImage,
            "unregistered survivor changed");
        try
        {
            using (SkinPresentation.MenuIcons(mario, 1))
            {
                Check(skill.icon == luigiImage, "menu didn't see selected artwork");
                using (SkinPresentation.MenuIcons(olimar, 1)) Check(skill.icon == piantaImage, "nested menu didn't select its skin");
                Check(skill.icon == luigiImage, "nested scope didn't restore outer artwork");
                throw new InvalidOperationException();
            }
        }
        catch (InvalidOperationException) { }
        Check(skill.icon == baseImage, "failed menu build changed shared SkillDef");
        if (args.Length > 0)
        {
            using var game = AssemblyDefinition.ReadAssembly(args[0]);
            var types = game.MainModule.Types;
            var select = types.Single(t => t.FullName == "RoR2.UI.CharacterSelectController");
            Check(select.Fields.Any(f => f.Name == "localUser" && f.FieldType.FullName == "RoR2.LocalUser"), "character-select local user API changed");
            Check(select.Methods.Any(m => m.Name == "RebuildLocal" && m.HasThis && m.ReturnType.FullName == "System.Void"
                && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Boolean"), "select detour signature changed");
            var loadout = types.Single(t => t.FullName == "RoR2.UI.LoadoutPanelController");
            Check(loadout.Fields.Any(f => f.Name == "currentDisplayData" && f.FieldType.Name == "DisplayData"), "loadout context API changed");
            foreach (var type in new[] { select, loadout, types.Single(t => t.FullName == "RoR2.UI.SkillIcon") })
            {
                string method = type.Name == "SkillIcon" ? "LateUpdate" : "Update";
                Check(type.Methods.Any(m => m.Name == method && m.HasThis && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Void"), "UI detour signature changed: " + type.Name);
            }
            Console.WriteLine("PASS: actual DLC4 menu fields and detour signatures match the shared skin UI boundary");
        }
        Console.WriteLine("PASS: per-player skin icons, separate body/name mapping, shared-image fallbacks, nested menu scopes and exception restoration");
    }
}
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class GameObject : Object { }
    public class Sprite : Object { }
}
namespace RoR2 { }
namespace RoR2.Skills { public class SkillDef : UnityEngine.Object { public UnityEngine.Sprite icon; } }
