#if ROR2COMMON_VR
using System;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // Every Melee fighter played in Blowntobytes' VR mod, from MeleeDriver, MeleePad and
    // MeleeFighterView alone, so a fighter needs nothing of its own: ror2-common's VrCharacter
    // (first person from the fighter's head, the body hidden for the player with its own forearms
    // and hands on the controllers, opt-in third person, the pointer's crosshair, the wrist HUD)
    // and one VR layout for the whole roster, the flat pad's buttons on the controllers:
    //
    //   dominant hand: A jump, B the fifth button (the A attack, or the shield), trigger RT,
    //   grip RB, stick click pings (the stick itself turns, the VR mod's own);
    //   off hand: stick moves (head forward), trigger LT, grip LB, X grabs (and interacts), Y uses
    //   equipment, stick click sprints.
    //
    // Which special or attack each of RT/LT/RB/LB is stays the fighter's MeleeControlLayout. The
    // dominant pointer is the aim every fighter's camera aim was (MeleeAim.CameraRay), so tilts,
    // smashes and shots follow the hand, never the head. RoR2 reads this layout instead of the VR
    // mod's own buttons (VrBank). The Melee plugin sets Settings and ControlsSetting at load;
    // without them (Mario's and Olimar's plugins, which compile this too) every fighter is flat.
    public static class MeleeVr
    {
        /// The Melee plugin's shared [VR] settings; null leaves every fighter flat.
        public static VrSettings Settings;
        /// VR.Controls: the layout above (else the VR mod's own buttons through RoR2's skills).
        public static ConfigEntry<bool> ControlsSetting;

        static VrCharacter character;
        /// The shared VR character, with the Melee plugin's [VR] settings (null without them).
        public static VrCharacter Character => character ?? (Settings == null ? null : character = new VrCharacter(Settings));

        /// The local player has `body` in a headset (first or third person).
        public static bool InHeadset(CharacterBody body) => Character != null && Character.InHeadset(body);

        /// ... with motion controls, in the VR layout.
        public static bool Controls(CharacterBody body) =>
            Character != null && (ControlsSetting == null || ControlsSetting.Value) && Character.Motion(body);

        /// The body isn't drawn for the player (first person with VR.HideBody); the arms are.
        public static bool HideBody(CharacterBody body) => Character != null && Character.HideBody(body);

        // The pointer reaches this far, and rests this far along it in the air.
        const float AimRange = 300f, AimMiss = 100f;

        /// The aim ray for `body`: the dominant pointer (VrCharacter.Aim, the one Mario's FLUDD and
        /// Olimar's cursor use) in the VR layout. False otherwise (the camera's, as flat).
        public static bool Aim(CharacterBody body, out Ray ray)
        {
            ray = default;
            if (!Controls(body) || !Character.Aim(body, out var aim, AimRange, AimMiss)) return false;
            ray = aim.Ray;
            return true;
        }

        /// The off-hand stick as a world move, forward where the head looks (flattened); its
        /// analog magnitude kept.
        public static Vector3 Move(CharacterBody body, Vector2 stick)
        {
            var forward = Character.ViewForward(body ? body.transform.forward : Vector3.forward);
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            return Vector3.ClampMagnitude(right * stick.x + forward * stick.y, 1f);
        }

        // ---- the layout ---------------------------------------------------------------------------

        /// One read of the controllers in the layout.
        public struct Read
        {
            /// Native bits from the four skill buttons and A.
            public bool Jump, Rt, Lt, Rb, Lb, Defend, Grab, Sprint;
            public Vector2 Stick;
            public VrBank.Buttons Ror2;
        }

        const float Squeeze = 0.5f;

        /// Reads the controllers (`pad` once a frame); `blocked`, a menu has them.
        public static Read ReadPad(VrPad pad, bool blocked)
        {
            pad.Read(blocked);
            var d = pad.Dominant;
            var o = pad.Off;
            var r = new Read
            {
                Jump = d.Primary,
                Defend = d.Secondary,
                Rt = d.Trigger > Squeeze,
                Rb = d.Grip > Squeeze,
                Lt = o.Trigger > Squeeze,
                Lb = o.Grip > Squeeze,
                Grab = o.Primary,
                Sprint = o.StickClick,
                Stick = o.Stick,
            };
            // The flat pad's RoR2 buttons (MeleePad's input-bank path) on the same controls.
            r.Ror2 = new VrBank.Buttons
            {
                Skill1 = r.Rt, Skill2 = r.Lt, Skill3 = r.Rb, Skill4 = r.Lb,
                Jump = r.Jump, Interact = r.Grab, Sprint = r.Sprint,
                Equipment = o.Secondary, Ping = d.StickClick,
            };
            return r;
        }

        // ---- the view -------------------------------------------------------------------------------

        // The tallest grounded head seen: a grounded head near it is standing (the eye's zero),
        // lower is a crouch or a squat the eye follows down.
        static float standingHead;

        /// Each LateUpdate for the local fighter, before its body is drawn: the view follows its
        /// head (`head`, from the feet in metres), or hangs behind in third person.
        public static void FollowHead(CharacterBody body, Vector3 head, Vector3 facing, Vector3 feet, float height, bool grounded)
        {
            if (Character == null || !InHeadset(body)) return;
            if (grounded) standingHead = Mathf.Max(standingHead * (1f - Time.deltaTime * 0.05f), head.y);
            var pose = grounded && head.y >= standingHead * 0.92f ? VrCharacter.Pose.Standing : VrCharacter.Pose.Other;
            Character.FollowHead(body, head, facing, feet, height, pose);
        }

        /// Draws a fighter's body view: on the controllers as its arms only when the body is hidden
        /// for the player, else whole (its arm draws hidden). `place` puts each body draw.
        public static void DrawBody(CharacterBody body, MeleeFighterView view, float scale, Func<ViewDraw, (Vector3, Quaternion)?> place)
        {
            bool inside = HideBody(body);
            view.SetVrArms(inside);
            if (inside) Character.DrawArms(view.Models, scale, place);
            else view.Models.Update(scale, place, d => VrCharacter.ArmSide(d) >= 0);
        }
    }
}
#endif
