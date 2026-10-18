using Rewired;
using RoR2;
using UnityEngine;
using Ror2Common;

namespace Ror2Common
{
    public struct MeleePadState { public Vector3 movement,aim; public uint buttons,allowed; }
    public sealed class MeleePad
    {
        uint held,previous,pending,blocked;
        int attackBuffer;
        Vector3 move,aim;
        bool grabbing;
        readonly InteractGuard interactGuard=new InteractGuard();InteractionDriver interaction;bool suppressed,interactionEnabled;
        public bool GrabHeld=>(held&MeleeControls.Grab)!=0;
        public bool JumpHeld=>(held&MeleeControls.Jump)!=0;
        public void ReleaseInteraction(){if(suppressed&&interaction)interaction.enabled=interactionEnabled;suppressed=false;}
        public void Read(CharacterBody body,InputBankTest input,bool grabbing=false,Vector3? cameraAim=null,bool shield=false)
        {
            if(this.grabbing!=grabbing){blocked=2|4|128|256;held=previous=pending=0;attackBuffer=0;}this.grabbing=grabbing;
            LocalUser user=null;foreach(var u in LocalUserManager.readOnlyLocalUsersList)if(u.cachedBody==body){user=u;break;}
            uint buttons=0;move=input?input.moveVector:Vector3.zero;aim=cameraAim??(input?input.aimDirection:Vector3.forward);
            if(user!=null&&user.isUIFocused){held=previous=pending=0;attackBuffer=0;move=Vector3.zero;ReleaseInteraction();return;}
            bool defend=false;
            var last=user?.inputPlayer?.controllers.GetLastActiveController();
            var pad=last!=null&&last.type==ControllerType.Joystick&&last.ImplementsTemplate<IGamepadTemplate>()?last.GetTemplate<IGamepadTemplate>():null;
            if(pad!=null) {
                if(pad.a.value)buttons|=1;
                buttons|=MeleeControls.Skills(pad.rightTrigger.value>0.18f,pad.leftTrigger.value>0.18f,pad.rightBumper.value,pad.leftBumper.value);
                if(pad.leftStick.press.value&&!grabbing)buttons|=32;if(pad.x.value)buttons|=128;defend=pad.b.value;
                // Preserve analog magnitude even when InputBankTest supplies a unit heading.
                move=move.normalized*Mathf.Clamp01(pad.leftStick.value.magnitude);
                if(grabbing)move=new Vector3(pad.leftStick.value.x,pad.leftStick.value.y,0);
            } else if(input) {
                if(input.jump.down)buttons|=1;
                buttons|=MeleeControls.Skills(input.skill1.down,input.skill2.down,input.skill3.down,input.skill4.down);
                if(input.sprint.down)buttons|=32;buttons|=input.interact.down?128u:0u;
                defend=UnityEngine.Input.GetKey(KeyCode.C);
                if(grabbing)move=new Vector3((UnityEngine.Input.GetKey(KeyCode.D)?1:0)-(UnityEngine.Input.GetKey(KeyCode.A)?1:0),(UnityEngine.Input.GetKey(KeyCode.W)?1:0)-(UnityEngine.Input.GetKey(KeyCode.S)?1:0),0);
            }
            if(!interaction)interaction=body.GetComponent<InteractionDriver>();
            bool grab=(buttons&MeleeControls.Grab)!=0;
            grab=interactGuard.Claim(grab,InteractGuard.PromptShown(interaction));
            buttons=(buttons&~MeleeControls.Grab)|(grab?MeleeControls.Grab:0);
            // X belongs to the native grapple only when no vanilla prompt owns it.
            if(grab&&interaction&&!suppressed){interactionEnabled=interaction.enabled;interaction.enabled=false;suppressed=true;}
            else if(!grab)ReleaseInteraction();
            buttons|=MeleeControls.Extra(defend,shield);
            if(grabbing&&(buttons&MeleeControls.NeutralB)!=0){buttons&=~MeleeControls.NeutralB;buttons|=MeleeControls.Special;}
            blocked&=buttons;buttons&=~blocked;
            pending|=buttons&~previous;previous=buttons;held=buttons;
        }
        public MeleePadState Take(uint allowed,bool locked=false)
        {
            uint buttons=held|pending;pending=0;
            if(locked&&((buttons&MeleeControls.Attack)!=0))attackBuffer=90;
            if(!locked&&attackBuffer>0&&(allowed&2)!=0){buttons|=MeleeControls.Attack;attackBuffer=0;}
            else if(!locked&&attackBuffer>0)attackBuffer--;
            // Captured throws use raw Melee axes; other movement stays in world space.
            return new MeleePadState { movement=new Vector3(move.x,grabbing?move.y:0,move.z),aim=new Vector3(aim.x,aim.y,aim.z),buttons=buttons,allowed=grabbing?31u:allowed };
        }
    }
}
