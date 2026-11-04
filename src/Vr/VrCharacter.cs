#if ROR2COMMON_VR
using System;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;

namespace Ror2Common
{
    // What every character played in the VR mod shares, on top of Vr's primitives: first person
    // from the character's own head, the view only ever turned by the player, the body hidden
    // for its own player (its effects and sounds stay; other players see all of it), its own
    // forearms and hands on the controllers in place of the VR mod's stand-ins, the pointer's
    // crosshair, the HUD on the wrist panel and the opt-in flip view. A character makes one
    // VrCharacter with a VrSettings bound in its plugin's config, and supplies only what's its
    // own: its head and pose each frame, which of its moves are flips, and its controls.
    public sealed class VrCharacter
    {
        public readonly VrSettings Settings;
        /// The pointer's own hits closer than this are its hand's neighbours (a wall it's in).
        public float PointerMinHit = 0.3f;
        /// The crosshair rests this far along a pointer that hits nothing.
        public float CrosshairDistance = 300f;

        public VrCharacter(VrSettings settings) { Settings = settings; }

        /// The local player plays this body in a headset, from inside or behind it: the view is the
        /// headset's (cutscene cameras never take it) and the heading is the head's.
        public bool InHeadset(CharacterBody body) => Vr.IsLocal(body);

        /// ... from inside it (not VR.ThirdPerson).
        public bool FirstPerson(CharacterBody body) => InHeadset(body) && !Settings.ThirdPerson.Value;

        /// ... from behind and above it (VR.ThirdPerson): the body, effects and the VR mod's hands
        /// are drawn, and the pointer still aims.
        public bool ThirdPerson(CharacterBody body) => InHeadset(body) && Settings.ThirdPerson.Value;

        /// ... and holds the VR mod's motion controls (the pointer aims).
        public bool Motion(CharacterBody body) => Vr.IsMotionLocal(body);

        /// Its body isn't drawn for its own player (VR.HideBody); the arms are.
        public bool HideBody(CharacterBody body) => Settings.HideBody.Value && FirstPerson(body);

        /// The character's HUD goes on the off-hand's wrist panel (VR.WristHud), for whoever is the
        /// local player now: the character, spectating, or flying a drone.
        public bool WristHud => Settings.WristHud.Value && Vr.HandsReady;

        /// Runs one of the character's OnGUI drawers: with the wrist HUD its Repaint goes onto the wrist panel
        /// (VrPanel), every other event (a desktop click on one of its buttons) to the screen as ever; without it,
        /// all to the screen. Several drawers share the panel in a frame.
        public void OnWrist(Action draw)
        {
            bool wrist = WristHud && Event.current != null && Event.current.type == EventType.Repaint;
            if (wrist && !VrPanel.Begin()) return;
            try { draw(); }
            finally { if (wrist) VrPanel.End(); }
        }

        /// Each LateUpdate a drawer drew this frame: shows the wrist panel (once a frame however many ask).
        public void ShowWrist()
        {
            if (WristHud) VrPanel.Show();
        }

        /// Where the headset faces, flattened: the heading a character's camera-relative stick follows.
        public Vector3 ViewForward(Vector3 fallback) => Vr.HeadFlatForward(fallback);

        /// Where the headset looks.
        public Vector3 HeadLook(Vector3 fallback)
        {
            var head = Vr.Head();
            return head ? head.forward : fallback;
        }

        /// The dominant hand's pointer.
        public bool Pointer(out Ray ray) => Vr.Pointer(true, out ray);

        /// Where the pointer aims this frame: its ray and the point it lands on in the world, in 3D
        /// (terrain or a hurt box: nothing pins it to the ground).
        public struct PointerAim
        {
            public Ray Ray;
            /// The first hit past the body's own colliders and PointerMinHit, or `miss` along the ray.
            public Vector3 Point;
            /// It hit something (else Point is in the air).
            public bool Hit;
        }

        /// The aim every character's pointer weapon shares (Mario's FLUDD, Olimar's cursor, the
        /// crosshair): the local body played with motion controls, the dominant hand's pointer
        /// cast up to `range`, resting `miss` along it when it hits nothing. False with no pointer.
        public bool Aim(CharacterBody body, out PointerAim aim, float range, float miss)
        {
            aim = default;
            if (!Motion(body) || !Pointer(out aim.Ray)) return false;
            aim.Point = Vr.PointerTarget(aim.Ray, body ? body.transform : null, range, PointerMinHit, out aim.Hit);
            if (!aim.Hit) aim.Point = aim.Ray.GetPoint(miss);
            return true;
        }

        /// Draws the pointer's crosshair this frame (VR.Crosshair), `hot` with a lock.
        public void Crosshair(CharacterBody body, bool hot)
        {
            if (!Settings.Crosshair.Value || PauseManager.isPaused || !Aim(body, out var aim, CrosshairDistance, CrosshairDistance)) return;
            VrCrosshair.Show(aim.Point, hot);
        }

        // ---- the arms ----------------------------------------------------------------------------
        //
        // A core that draws its characters' VR arms (libcommon's vr_arms cut, baked into the asset
        // cache on first run) adds one draw a side to its view: the body's model, keyed with
        // ArmKey + side (0 the character's right, 1 its left) under the model byte and posed in
        // arm space (the hand's middle at the origin, +z toward the fingers, +y the back of the
        // hand, ModelView's mirrored X to the right: a controller's own axes). DrawArms puts each
        // on its controller and hides everything else of the body.

        /// The low key bits of a VR arm draw, + side.
        public const ulong ArmKey = 0x10;

        /// The side (0 right, 1 left) of a VR arm draw, or -1 for any other.
        public static int ArmSide(ViewDraw d)
        {
            ulong low = d.key & 0x00FF_FFFF_FFFF_FFFFUL;
            return low == ArmKey ? 0 : low == ArmKey + 1 ? 1 : -1;
        }

        /// Where a side's arm space goes this frame: on that controller (the character's right arm
        /// on the right one), turned by VR.ArmPitch / VR.ArmRoll (the left's roll mirrored), moved
        /// VR.ArmForward along it, and turned with the view through a flip. Null with no hand.
        public (Vector3, Quaternion)? ArmAt(int side)
        {
            if (!Vr.HandsReady) return null;
            bool left = side == 1;
            var hand = Vr.HandBySide(left);
            if (!hand) return null;
            float roll = Settings.ArmRoll.Value * (left ? -1f : 1f);
            var rot = hand.rotation * Quaternion.Euler(Settings.ArmPitch.Value, 0f, roll);
            var at = hand.position + rot * (Vector3.forward * Settings.ArmForward.Value);
            return VrRig.Turned(at, rot);
        }

        /// Updates `models` showing only its VR arm draws, each on its controller (`body` places
        /// every other draw, posed but hidden, as the view would). The VR mod's stand-in hands show
        /// only when no arm was drawn (a skin with no cut). How many arms were drawn.
        public int DrawArms(ModelView models, float scale, Func<ViewDraw, (Vector3, Quaternion)?> body)
        {
            if (models == null) { ArmsDrawn(0); return 0; }
            var placed = new (Vector3, Quaternion)?[2];
            placed[0] = ArmAt(0);
            placed[1] = ArmAt(1);
            int drawn = 0;
            models.Update(scale,
                d => { int s = ArmSide(d); return s >= 0 ? placed[s] : body?.Invoke(d); },
                d =>
                {
                    int s = ArmSide(d);
                    bool show = s >= 0 && placed[s].HasValue;
                    if (show) drawn++;
                    return !show;
                });
            ArmsDrawn(drawn);
            return drawn;
        }

        /// After drawing arms some other way: the VR mod's stand-in hands show only with none drawn.
        public void ArmsDrawn(int count)
        {
            Vr.ShowHandModel(true, count == 0);
            Vr.ShowHandModel(false, count == 0);
        }

        // ---- the eye -----------------------------------------------------------------------------
        //
        // The VR mod holds the view at a fixed eye height on the body (VrRig). The character's head
        // height above its feet moves it from there: down low in a slide or a crouch, back up as it
        // stands. Only the height: a spin swings the head round the body, and following that would
        // orbit the view. The height is smoothed (the headset's own motion never is). Through a
        // flip the eye holds still unless VR.FlipCamera turns the view over with the character,
        // about its middle, for the render only.

        /// What the character's pose means for the view this frame.
        public enum Pose
        {
            /// Moving about: the eye follows the head's height.
            Other,
            /// Standing or running on the ground: the head's height here is the eye's zero.
            Standing,
            /// A somersault or flip: the eye holds, or turns over with VR.FlipCamera.
            Flip,
        }

        float standing = -1f, eyeOffset;
        Quaternion flipTurn = Quaternion.identity;
        // The offset settles toward the head this quickly (seconds); a cut-short flip turns back.
        const float EyeSmoothing = 0.08f, FlipRecover = 0.15f, StandingLearn = 1f;

        /// Each frame, before the arms are drawn (so they turn with a flip): `head` is the head
        /// from the feet in world metres, `facing` the body's forward, `height` its standing height.
        public void FollowHead(CharacterBody body, Vector3 head, Vector3 facing, Vector3 feet, float height, Pose pose)
        {
            PlaceBehind(body);
            if (!FirstPerson(body)) return;
            float h = head.y;
            if (standing < 0f) standing = h;
            if (pose == Pose.Standing) standing += (h - standing) * (1f - Mathf.Exp(-Time.deltaTime / StandingLearn));
            bool flip = pose == Pose.Flip;
            float target = flip ? 0f : (h - standing) * Mathf.Clamp01(Settings.HeadFollow.Value);
            target = Mathf.Clamp(target, -standing * 0.85f, standing * 0.15f);
            eyeOffset += (target - eyeOffset) * (1f - Mathf.Exp(-Time.deltaTime / EyeSmoothing));
            VrRig.EyeOffset(body, eyeOffset);

            var want = Quaternion.identity;
            var up = Vector3.up;
            var forward = facing;
            forward.y = 0f;
            if (flip && Settings.FlipCamera.Value && forward.sqrMagnitude > 1e-4f && head.sqrMagnitude > 1e-6f)
            {
                forward.Normalize();
                // How far round it has gone: the head's angle from straight up, forward positive
                // (a somersault pitches the view down, a backflip up), about its right.
                float angle = Mathf.Atan2(Vector3.Dot(head, forward), Vector3.Dot(head, up)) * Mathf.Rad2Deg;
                want = Quaternion.AngleAxis(angle, Vector3.Cross(up, forward));
                flipTurn = want;
            }
            else flipTurn = Quaternion.Slerp(flipTurn, want, 1f - Mathf.Exp(-Time.deltaTime / FlipRecover));
            if (Quaternion.Angle(flipTurn, Quaternion.identity) > 0.5f)
                VrRig.RenderTurn(flipTurn, feet + Vector3.up * (height * 0.5f));
            else flipTurn = Quaternion.identity;
        }

        // ---- third person ------------------------------------------------------------------------
        //
        // VR.ThirdPerson hangs the view VR.ThirdPersonBack behind and VR.ThirdPersonUp above the
        // VR mod's eye, behind as the player has turned the rig (never the head, so looking about
        // doesn't swing it, and never the body's spins). No flips, no head following.

        bool behind;

        /// Each frame in a headset (FollowHead does it; call it directly for a character with no head
        /// pose): in third person it puts the view behind, and back at the eye when that's turned off.
        public void PlaceBehind(CharacterBody body)
        {
            if (!InHeadset(body)) return;
            if (!Settings.ThirdPerson.Value)
            {
                if (behind) { behind = false; eyeOffset = 0f; VrRig.ViewOffset(body, Vector3.zero); }
                return;
            }
            behind = true;
            flipTurn = Quaternion.identity;
            var back = VrRig.FlatForward(body.transform.forward);
            VrRig.ViewOffset(body, Vector3.up * Settings.ThirdPersonUp.Value - back * Settings.ThirdPersonBack.Value);
        }
    }

    /// The [VR] settings every character shares, bound in its own plugin's config.
    public sealed class VrSettings
    {
        public readonly ConfigEntry<bool> HideBody, Crosshair, WristHud, FlipCamera, ThirdPerson;
        public readonly ConfigEntry<float> HeadFollow, ArmPitch, ArmRoll, ArmForward, ThirdPersonBack, ThirdPersonUp;

        /// `who` names the character in the descriptions ("Mario").
        public VrSettings(ConfigFile config, string who)
        {
            const string S = "VR";
            HideBody = config.Bind(S, "HideBody", true,
                $"In VR, don't draw your own {who} (the effects and sounds stay; other players still see all of " +
                "them). The forearms and hands are drawn on the controllers.");
            Crosshair = config.Bind(S, "Crosshair", true, "In VR, a ring where the dominant hand's pointer lands.");
            HeadFollow = config.Bind(S, "HeadFollow", 1f,
                new ConfigDescription($"In VR, how much the view follows {who}'s head down and up (a slide drops it " +
                    "low, a crouch a little): 0 never, 1 fully. Never sideways or round in a spin.",
                    new AcceptableValueRange<float>(0f, 1f)));
            ArmPitch = config.Bind(S, "ArmPitch", 0f,
                $"In VR, {who}'s forearms and hands on the controllers tip this many degrees about the " +
                "controller's side (positive: fingers down).");
            ArmRoll = config.Bind(S, "ArmRoll", 0f,
                "In VR, the arms roll this many degrees about the controller's length (positive turns the " +
                "right hand's back outward; the left mirrors it).");
            ArmForward = config.Bind(S, "ArmForward", 0f,
                "In VR, the arms sit this far forward (metres; negative: back) along the controller from " +
                "the hand's middle in the grip.");
            WristHud = config.Bind(S, "WristHud", true,
                $"In VR with motion controls, {who}'s own HUD on a panel over the off-hand's wrist (point at " +
                "its menus to pick from them). False: it's drawn on the desktop window only.");
            FlipCamera = config.Bind(S, "FlipCamera", false,
                $"In VR, the view turns over with {who} through a somersault or flip. Off: the view is only ever " +
                "turned by you.");
            ThirdPerson = config.Bind(S, "ThirdPerson", false,
                $"In VR, see {who} from behind and above instead of from inside (the VR mod's hands, not " +
                $"{who}'s; the pointer still aims). The view follows where you turn, not {who}'s facing.");
            ThirdPersonBack = config.Bind(S, "ThirdPersonBack", 3.5f,
                new ConfigDescription("In VR third person, how far behind the view sits (metres).",
                    new AcceptableValueRange<float>(0f, 20f)));
            ThirdPersonUp = config.Bind(S, "ThirdPersonUp", 2f,
                new ConfigDescription("In VR third person, how far above the eye the view sits (metres).",
                    new AcceptableValueRange<float>(-1f, 20f)));
        }
    }
}
#endif
