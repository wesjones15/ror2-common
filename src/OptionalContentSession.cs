using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
namespace Ror2Common
{
    /// One instance per optional gameplay pack. Only capability bits cross the wire.
    public sealed class OptionalContentSession : MonoBehaviour
    {
        const byte Version = 1;
        readonly SessionFeaturePolicy policy = new SessionFeaturePolicy();
        readonly Dictionary<NetworkConnection,float> denied = new Dictionary<NetworkConnection,float>();
        short hello, result;
        Func<uint> local;
        Func<bool> applies;
        readonly Dictionary<NetworkConnection,uint> reports = new Dictionary<NetworkConnection,uint>();
        string feature;
        uint received;
        bool receivedState, locked, wasNetworked;
        float nextSend;
        bool Applies => applies == null || applies();
        /// The server: connected players who reported without `bit` and without `excuse` (a bit meaning "still preparing"), so a host can refuse them.
        public IEnumerable<NetworkConnection> Lacking(uint bit, uint excuse)
        {
            var gone = new List<NetworkConnection>();
            foreach (var pair in reports)
            {
                if (pair.Key == null || !pair.Key.isConnected) { gone.Add(pair.Key); continue; }
                if ((pair.Value & bit) == 0 && (pair.Value & excuse) == 0) yield return pair.Key;
            }
            foreach (var key in gone) reports.Remove(key);
        }
        public bool Has(uint mask) => ((NetworkServer.active ? policy.Active : NetworkClient.active ? receivedState ? received : 0u : local()) & mask) == mask;
        /// `applies`, when given, says when the feature is enforced at all (a mode that needs it); until then the session only collects reports.
        public static OptionalContentSession Attach(GameObject owner, short message, string feature, Func<uint> local, Func<bool> applies = null)
        {
            var self = owner.AddComponent<OptionalContentSession>(); self.hello = message; self.result = (short)(message+1); self.feature=feature; self.local=local; self.applies=applies;
            Mp.Register(self.hello,self.Report,null); Mp.Register(self.result,null,self.Receive);
            Run.onRunStartGlobal += self.Begin; Run.onRunDestroyGlobal += self.End;
            return self;
        }
        IEnumerable<int> Required() => NetworkUser.readOnlyInstancesList.Where(u => u && !u.isLocalPlayer && u.connectionToClient != null)
            .Select(u => u.connectionToClient.connectionId).Distinct();
        void Report(NetworkConnection from, ByteReader reader)
        {
            if (reader.U8()!=Version) return; uint supported=reader.U32(); if(reader.Failed || from==null)return;
            policy.Report(from.connectionId,supported); reports[from]=supported;
            if(Applies && !policy.Compatible(supported) && !denied.ContainsKey(from)) denied[from]=Time.unscaledTime+3f;
            Broadcast();
        }
        void Broadcast()
        {
            if(!NetworkServer.active)return;
            policy.Reconcile(local(),Required());
            var writer=new ByteWriter(); writer.U8(Version);writer.U32(policy.Active);writer.Bool(policy.Locked);
            Mp.SendToAll(result,writer.ToArray());
        }
        void Receive(NetworkConnection from,ByteReader reader)
        {
            if(reader.U8()!=Version)return;uint mask=reader.U32();bool frozen=reader.Bool();if(reader.Failed)return;
            received=mask;locked=frozen;receivedState=true;
            if(Applies && frozen && (local() & mask)!=mask)
                Chat.AddMessage("This run uses "+feature+" bonus content. Join a compatible new run, or put the required disc in "+SharedFiles.DiscHint+" and restart.");
        }
        void Begin(Run run) { if(NetworkServer.active){policy.Reconcile(local(),Required());policy.Lock();Broadcast();} }
        void End(Run run) {policy.Reset();reports.Clear();received=0;receivedState=false;locked=false;denied.Clear();}
        void Update()
        {
            if(local==null)return;
            bool networked=NetworkServer.active||NetworkClient.active;
            if(!networked && wasNetworked)End(null);wasNetworked=networked;
            if(Time.unscaledTime>=nextSend){nextSend=Time.unscaledTime+1f;
                if(NetworkServer.active)Broadcast();
                else if(NetworkClient.active){var writer=new ByteWriter();writer.U8(Version);writer.U32(local());Mp.SendToServer(hello,writer.ToArray());}}
            foreach(var pair in denied.ToArray())if(Time.unscaledTime>=pair.Value){pair.Key.Disconnect();denied.Remove(pair.Key);}
        }
        void OnGUI()
        {
            if(local==null || applies!=null || Run.instance || NetworkUser.readOnlyInstancesList.Count<2 || Has(1))return;
            GUI.Label(new Rect(25,Screen.height-85,Screen.width-50,65),"You can play together. "+feature+" bonus content is unavailable while a player's required data is missing or still preparing. Put the disc image in "+SharedFiles.DiscHint+" and restart if needed.");
        }
        void OnDestroy(){Run.onRunStartGlobal-=Begin;Run.onRunDestroyGlobal-=End;}
    }
}
