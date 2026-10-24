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
        /// Which stock skill icon supplies the clone's key glyph: 0..3 (Yoshi's extra
        /// slot is on LT, so it clones the secondary icon). Placement still follows slot 3.
        public int glyphFrom=3;
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
                var glyph=hud.skillIcons[Mathf.Clamp(glyphFrom,0,3)];
                var source=glyph?glyph:hud.skillIcons[3];if(!source)continue;
                var clone=Instantiate(source.gameObject,source.transform.parent,false);
                clone.name="NativeExtraSkill";
                var layout=clone.GetComponent<LayoutElement>()??clone.AddComponent<LayoutElement>();layout.ignoreLayout=true;
                var rect=clone.GetComponent<RectTransform>();var original=source.GetComponent<RectTransform>();
                var last=hud.skillIcons[3]?hud.skillIcons[3].GetComponent<RectTransform>():original;
                Place(hud,rect,last);
                var icon=clone.GetComponent<SkillIcon>();icon.targetSkill=skill;icon.targetSkillSlot=SkillSlot.None;
                icon.playerCharacterMasterController=source.playerCharacterMasterController;
                if(!string.IsNullOrEmpty(actionName))foreach(var binding in clone.GetComponentsInChildren<InputBindingDisplayController>(true)){
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
                Place(pair.Key,rect,original);
            }
        }
        /// Puts the fifth icon in the stock row: one slot past the last icon, at the spacing the stock icons
        /// already have (last minus the one before it), so its cooldown overlay and key glyph line up with theirs.
        /// If that spot would sit on the equipment slot or off the screen, the same step before the first icon,
        /// and only then above the row.
        static void Place(HUD hud,RectTransform clone,RectTransform last)
        {
            var icons=hud.skillIcons;
            Vector2 step=Vector2.zero;
            var before=icons.Length>=3&&icons[2]?icons[2].GetComponent<RectTransform>():null;
            if(before)step=last.anchoredPosition-before.anchoredPosition;
            // A row runs sideways; with no usable spacing (a layout group not yet laid out) use one slot and a gap.
            if(Mathf.Abs(step.x)<1f||Mathf.Abs(step.x)<Mathf.Abs(step.y))step=new Vector2(last.rect.width+12f,0f);
            var first=icons[0]?icons[0].GetComponent<RectTransform>():last;
            var parent=clone.parent as RectTransform;
            Vector2[] candidates={
                last.anchoredPosition+step,
                first.anchoredPosition-step,
                last.anchoredPosition+Vector2.up*(last.rect.height+12f),
            };
            for(int i=0;i<candidates.Length;i++){
                var offset=candidates[i]-last.anchoredPosition;
                // The last slot's bounds in the shared parent's space, moved to the candidate.
                var bounds=LocalBounds(last,parent);bounds.center+=(Vector3)offset;
                if(i<candidates.Length-1&&!Free(hud,bounds,parent,clone))continue;
                clone.anchoredPosition=candidates[i];
                return;
            }
        }
        // `rect`'s box in `space`'s local coordinates.
        static Bounds LocalBounds(RectTransform rect,Transform space)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            var box=new Bounds(space.InverseTransformPoint(corners[0]),Vector3.zero);
            for(int i=1;i<4;i++)box.Encapsulate(space.InverseTransformPoint(corners[i]));
            box.center=new Vector3(box.center.x,box.center.y,0f);box.size=new Vector3(box.size.x,box.size.y,0f);
            return box;
        }
        // Whether `box` (in `parent`'s space) is on the screen and clear of the equipment slot and the stock icons.
        static bool Free(HUD hud,Bounds box,RectTransform parent,RectTransform clone)
        {
            var canvas=hud.mainContainerCanvas?hud.mainContainerCanvas.rootCanvas:null;
            var camera=canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
            foreach(var corner in new[]{box.min,box.max}){
                var p=RectTransformUtility.WorldToScreenPoint(camera,parent.TransformPoint(corner));
                if(p.x<0f||p.y<0f||p.x>Screen.width||p.y>Screen.height)return false;
            }
            // Slightly shrunk, so slots that merely touch (the stock spacing) do not count as overlapping.
            box.size-=new Vector3(4f,4f,0f);
            if(hud.equipmentIcons!=null)foreach(var equipment in hud.equipmentIcons){
                var rect=equipment?equipment.GetComponent<RectTransform>():null;
                if(rect&&rect.gameObject.activeInHierarchy&&LocalBounds(rect,parent).Intersects(box))return false;
            }
            foreach(var stock in hud.skillIcons){
                var rect=stock?stock.GetComponent<RectTransform>():null;
                if(rect&&rect!=clone&&rect.gameObject.activeInHierarchy&&LocalBounds(rect,parent).Intersects(box))return false;
            }
            return true;
        }
        void OnDisable(){foreach(var icon in icons.Values)if(icon)Destroy(icon.gameObject);icons.Clear();}
    }
}
