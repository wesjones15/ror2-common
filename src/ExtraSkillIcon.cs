using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Ror2Common
{
    /// A real SkillIcon for an additional GenericSkill, using the current HUD's own style.
    /// RoR2's HUD.Update binds only its four stock icons, so keep this clone outside that array.
    public sealed class ExtraSkillIcon : MonoBehaviour
    {
        public string skillName, actionName="Interact";
        readonly Dictionary<HUD,SkillIcon> icons=new Dictionary<HUD,SkillIcon>();
        readonly List<HUD> remove=new List<HUD>();
        void Update()
        {
            var locator=GetComponent<SkillLocator>();var skill=locator?locator.FindSkill(skillName):null;
            if(!skill)return;
            remove.Clear();
            foreach(var pair in icons)if(!pair.Key||pair.Key.targetBodyObject!=gameObject){if(pair.Value)Destroy(pair.Value.gameObject);remove.Add(pair.Key);}
            foreach(var hud in remove)icons.Remove(hud);
            foreach(var hud in HUD.readOnlyInstanceList){
                if(!hud||hud.targetBodyObject!=gameObject||icons.ContainsKey(hud)||hud.skillIcons==null||hud.skillIcons.Length<4)continue;
                var source=hud.skillIcons[3];if(!source)continue;
                var clone=Instantiate(source.gameObject,source.transform.parent,false);
                clone.name="NativeExtraSkill";
                var layout=clone.GetComponent<LayoutElement>()??clone.AddComponent<LayoutElement>();layout.ignoreLayout=true;
                var rect=clone.GetComponent<RectTransform>();var original=source.GetComponent<RectTransform>();
                // Above the fourth icon avoids the equipment column and four-icon layout.
                rect.anchoredPosition=original.anchoredPosition+Vector2.up*(original.rect.height+12f);
                var icon=clone.GetComponent<SkillIcon>();icon.targetSkill=skill;icon.targetSkillSlot=SkillSlot.None;
                icon.playerCharacterMasterController=source.playerCharacterMasterController;
                foreach(var binding in clone.GetComponentsInChildren<InputBindingDisplayController>(true)){
                    binding.actionName=actionName;binding.SendMessage("Refresh",SendMessageOptions.DontRequireReceiver);
                }
                icons.Add(hud,icon);
            }
        }
        void LateUpdate()
        {
            // HUD layout groups may place the stock row after the clone was made.
            // Follow its current rect instead of retaining an unlaid-out position.
            foreach(var pair in icons){
                if(!pair.Key||!pair.Value||pair.Key.skillIcons==null||pair.Key.skillIcons.Length<4||!pair.Key.skillIcons[3])continue;
                var original=pair.Key.skillIcons[3].GetComponent<RectTransform>();
                var rect=pair.Value.GetComponent<RectTransform>();
                rect.anchoredPosition=original.anchoredPosition+Vector2.up*(original.rect.height+12f);
            }
        }
        void OnDisable(){foreach(var icon in icons.Values)if(icon)Destroy(icon.gameObject);icons.Clear();}
    }
}
