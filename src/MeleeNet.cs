using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Ror2Common
{
    /// Melee multiplayer. Every fighter works the same way:
    ///
    /// * The machine that owns a player's body runs the real native sim (`MeleeDriver`), exactly as
    ///   a single-player game does, and does the fighter's attacks there (`Acts`). With other players
    ///   in the game it records what it did to the sim (`ssbm_sim_record`) and sends the log in
    ///   batches of a few steps to the server, which relays it to everyone else (`FramesMsg`).
    /// * Every other machine, the server's included, runs a view-only shadow sim of that fighter on
    ///   the log (`ssbm_sim_replay`): the same models, animation, sounds and effects, with no attacks
    ///   of its own (its position is pulled to the recorded one when they drift).
    /// * What only the server may do to the world is a message from the owner: damage (`HitMsg`),
    ///   holding an enemy helpless (`HoldMsg`), capturing and carrying one (`CaptureMsg`), turning
    ///   enemy projectiles back (`ReflectMsg`), pulling an enemy (`PushMsg`) and the fighter's
    ///   protection buff (`ProtectMsg`).
    ///
    /// Everyone needs the same plugin build and the fighter's own disc assets; a mismatch is logged
    /// once and that player's fighter looks like Commando to the other.
    public static class MeleeNet
    {
        public const byte Protocol = 1;
        /// An owner's recorded sim calls: owner to server, then server to the other clients.
        public const short FramesMsg = 0x4C01;
        /// One hit, a client fighter's: client to server.
        public const short HitMsg = 0x4C02;
        public const short HoldMsg = 0x4C03;
        public const short CaptureMsg = 0x4C04;
        public const short ReflectMsg = 0x4C05;
        public const short PushMsg = 0x4C06;
        public const short ProtectMsg = 0x4C07;
        /// "I run Melee, this build": client to server when a run starts, answered by the server. Fighters are
        /// only sent to machines that said so, so a player without the mod never sees messages it cannot read.
        public const short HelloMsg = 0x4C08;

        /// The most a single hit may deal and push, whatever a client says.
        const float MaxCoefficient = 200f, MaxForce = 30000f, MaxReach = 250f;

        /// This machine owns `body` and so runs its real sim and makes its attacks: a client for its
        /// own fighter, the server for every other (including its own player's).
        public static bool Acts(CharacterBody body) => body && body.hasEffectiveAuthority;

        /// The batches of recorded calls received for one body, oldest first.
        public sealed class Stream
        {
            public readonly Queue<byte[]> Batches = new Queue<byte[]>();
            public int Waiting;
        }

        static readonly Dictionary<uint, Stream> streams = new Dictionary<uint, Stream>();
        static readonly Dictionary<uint, EnemyCapture> captures = new Dictionary<uint, EnemyCapture>();
        // Server: the connections that run this Melee build. Client: whether the server does.
        static readonly HashSet<int> peers = new HashSet<int>();
        static bool serverHasMelee;
        static readonly Dictionary<uint, bool> protectedBodies = new Dictionary<uint, bool>();
        static bool registered, warned;

        public static void Register()
        {
            if (registered) return;
            registered = true;
            Mp.Register(HelloMsg, OnHelloServer, OnHelloClient);
            Mp.Register(FramesMsg, OnFramesServer, OnFramesClient);
            Mp.Register(HitMsg, OnHit, null);
            Mp.Register(HoldMsg, OnHold, null);
            Mp.Register(CaptureMsg, OnCapture, null);
            Mp.Register(ReflectMsg, OnReflect, null);
            Mp.Register(PushMsg, OnPush, null);
            Mp.Register(ProtectMsg, OnProtect, null);
            Run.onRunDestroyGlobal += run => { streams.Clear(); captures.Clear(); protectedBodies.Clear(); serverHasMelee = false; };
            // A client says hello when a run starts and at each stage, so the server knows to send it fighters.
            Run.onRunStartGlobal += run => SendHello();
            Stage.onStageStartGlobal += stage => SendHello();
            // A client that joined mid-run, or whose hello went unanswered, tries again every few seconds.
            RoR2Application.onFixedUpdate += () =>
            {
                if (NetworkServer.active || !NetworkClient.active || serverHasMelee || Run.instance == null) return;
                if ((helloTick = (helloTick + 1) % 150) == 0) SendHello();
            };
        }
        static int helloTick;

        // --- Who runs Melee --------------------------------------------------------------------

        static byte[] HelloPayload()
        {
            var w = new ByteWriter(8);
            w.U8(Protocol); w.U32(MeleeNative.AbiVersion);
            return w.ToArray();
        }

        static void SendHello()
        {
            if (NetworkServer.active || !NetworkClient.active) return;
            Mp.SendToServer(HelloMsg, HelloPayload());
        }

        static bool Compatible(ByteReader r, string who)
        {
            byte protocol = r.U8(); uint abi = r.U32();
            if (!r.Failed && protocol == Protocol && abi == MeleeNative.AbiVersion) return true;
            if (!warned)
            {
                warned = true;
                CommonLog.Warn($"{who} runs a different Melee build (network protocol {protocol}, libssbm ABI {abi}; this one {Protocol} and {MeleeNative.AbiVersion}): install the same Melee release as everyone else. Their fighter will look like Commando.");
            }
            return false;
        }

        static void OnHelloServer(NetworkConnection from, ByteReader r)
        {
            if (from == null) return;
            if (!Compatible(r, "A player")) return;
            peers.Add(from.connectionId);
            Mp.SendTo(from, HelloMsg, HelloPayload());
        }

        static void OnHelloClient(NetworkConnection from, ByteReader r)
        {
            if (Compatible(r, "The host")) serverHasMelee = true;
        }

        /// More than one player is in the game: worth recording and sending the sim.
        public static bool Networked => Mp.Networked;

        public static Stream StreamFor(uint netId) => streams.TryGetValue(netId, out var s) ? s : null;
        public static void Drop(uint netId) => streams.Remove(netId);

        // --- Frames --------------------------------------------------------------------------

        /// Sends `ops` (a recorded log) for `body`. The server's own go to everyone; a client's to the server.
        public static void SendFrames(CharacterBody body, uint abi, byte[] ops)
        {
            if (!body || body.netId.Value == 0 || ops == null || ops.Length == 0) return;
            if (!NetworkServer.active && !serverHasMelee) return;
            var w = new ByteWriter(ops.Length + 16);
            w.U8(Protocol); w.U32(abi); w.U32(body.netId.Value); w.U16((ushort)ops.Length);
            w.Bytes(ops, 0, ops.Length);
            Mp.SendToServer(FramesMsg, w.ToArray());
        }

        /// The server keeps a client's frames for the shadows on its own machine and relays them, untouched, to
        /// the clients that run Melee (the host's own come in with `from` null and go to all of them).
        static void OnFramesServer(NetworkConnection from, ByteReader reader)
        {
            if (from != null) Accept(new ByteReader(reader.Raw));
            var raw = reader.Raw;
            foreach (var conn in NetworkServer.connections)
                if (conn != null && conn != from && conn.isReady && peers.Contains(conn.connectionId))
                    Mp.SendTo(conn, FramesMsg, raw);
        }

        static void OnFramesClient(NetworkConnection from, ByteReader r) => Accept(r);

        /// Queues a batch of an owner's recorded calls for the shadow on this machine (never for the owner itself).
        static void Accept(ByteReader r)
        {
            byte protocol = r.U8(); uint abi = r.U32(); uint netId = r.U32(); int length = r.U16();
            if (r.Failed || protocol != Protocol || abi != MeleeNative.AbiVersion) return;
            // The owner's own frames never come back, but a relay can: it has them.
            var go = Mp.Find(netId);
            var body = go ? go.GetComponent<CharacterBody>() : null;
            if (body && body.hasEffectiveAuthority) return;
            var ops = r.Bytes(length);
            if (r.Failed) return;
            if (!streams.TryGetValue(netId, out var stream)) streams[netId] = stream = new Stream();
            // A stream nobody replays (no fighter on this body) must not grow without end.
            if (stream.Batches.Count > 600) stream.Batches.Dequeue();
            stream.Batches.Enqueue(ops);
        }

        // --- What only the server may do -----------------------------------------------------

        static void Vec(ByteWriter w, Vector3 v) { w.F32(v.x); w.F32(v.y); w.F32(v.z); }
        static Vector3 Vec(ByteReader r) => new Vector3(r.F32(), r.F32(), r.F32());
        static bool Finite(Vector3 v) { float s = v.x + v.y + v.z; return !(float.IsNaN(s) || float.IsInfinity(s)); }

        /// The fighter a message says it came from: a Melee body that is another machine's (never the server's own).
        static CharacterBody Sender(uint netId)
        {
            var go = Mp.Find(netId);
            var body = go ? go.GetComponent<CharacterBody>() : null;
            if (!body || body.hasEffectiveAuthority || !body.GetComponent<MeleeDriver>()) return null;
            return body.healthComponent && body.healthComponent.alive ? body : null;
        }

        static CharacterBody Body(uint netId)
        {
            var go = Mp.Find(netId);
            return go ? go.GetComponent<CharacterBody>() : null;
        }

        /// A client fighter's hit, for the server to land as that fighter's.
        public static bool RemoteDeal(CharacterBody attacker, HealthComponent victim, in NativeHitSpec spec)
        {
            if (!Acts(attacker) || !attacker.GetComponent<MeleeDriver>() || !victim || !victim.alive || !victim.body) return false;
            uint victimId = victim.body.netId.Value;
            if (victimId == 0 || attacker.netId.Value == 0) return false;
            var w = new ByteWriter(80);
            w.U8(Protocol); w.U32(attacker.netId.Value); w.U32(victimId);
            w.F32(spec.coefficient); w.F32(spec.procCoefficient); Vec(w, spec.position); Vec(w, spec.force);
            ulong damageType = spec.damageType;
            w.U32((uint)damageType); w.U32((uint)(damageType >> 32)); w.U8((byte)spec.color);
            w.F32(spec.staggerDuration); w.Bool(spec.staggerBosses);
            Mp.SendToServer(HitMsg, w.ToArray());
            return true;
        }

        static void OnHit(NetworkConnection from, ByteReader r)
        {
            if (r.U8() != Protocol) return;
            var attacker = Sender(r.U32());
            var victim = Body(r.U32());
            var spec = new NativeHitSpec { coefficient = r.F32(), procCoefficient = r.F32(), position = Vec(r), force = Vec(r) };
            ulong low = r.U32(), high = r.U32();
            spec.damageType = (DamageTypeCombo)(low | (high << 32));
            spec.color = (DamageColorIndex)r.U8();
            spec.staggerDuration = r.F32(); spec.staggerBosses = r.Bool();
            if (r.Failed || !attacker || !victim || !victim.healthComponent) return;
            if (!Finite(spec.position) || !Finite(spec.force) || float.IsNaN(spec.coefficient + spec.procCoefficient + spec.staggerDuration)) return;
            if (spec.coefficient < 0f || spec.coefficient > MaxCoefficient || spec.force.magnitude > MaxForce) return;
            if ((spec.position - attacker.corePosition).magnitude > MaxReach) return;
            spec.procCoefficient = Mathf.Clamp(spec.procCoefficient, 0f, 2f);
            spec.staggerDuration = Mathf.Clamp(spec.staggerDuration, 0f, 5f);
            NativeHit.Deal(attacker, victim.healthComponent, spec);
        }

        public static bool RemoteHold(CharacterBody victim, float seconds)
        {
            if (!victim || victim.isPlayerControlled || seconds <= 0f || victim.netId.Value == 0 || !LocalFighter()) return false;
            var w = new ByteWriter(16);
            w.U8(Protocol); w.U32(victim.netId.Value); w.F32(seconds);
            Mp.SendToServer(HoldMsg, w.ToArray());
            return true;
        }

        static void OnHold(NetworkConnection from, ByteReader r)
        {
            if (r.U8() != Protocol) return;
            var victim = Body(r.U32());
            float seconds = r.F32();
            if (r.Failed || !victim || float.IsNaN(seconds) || seconds <= 0f || !SomeoneElsesFighterAlive(from)) return;
            EnemyHold.Hold(victim, Mathf.Min(seconds, 10f));
        }

        // A message from a connection: that connection has a living Melee fighter (the server cannot map a
        // body to its owner more cheaply, and this is a friends-only game).
        static bool SomeoneElsesFighterAlive(NetworkConnection from)
        {
            foreach (var master in PlayerCharacterMasterController.instances)
            {
                var body = master.master ? master.master.GetBody() : null;
                if (body && !body.hasEffectiveAuthority && body.GetComponent<MeleeDriver>() && body.healthComponent && body.healthComponent.alive) return true;
            }
            return false;
        }

        static bool LocalFighter()
        {
            foreach (var master in PlayerCharacterMasterController.instances)
            {
                var body = master.master ? master.master.GetBody() : null;
                if (body && body.hasEffectiveAuthority && body.GetComponent<MeleeDriver>()) return true;
            }
            return false;
        }

        /// Capture messages: 1 start, 2 step (carry), 3 end.
        public static void SendCapture(CharacterBody owner, byte op, CharacterBody victim, Vector3 forward, float dt, Vector3? anchor)
        {
            if (!owner || owner.netId.Value == 0) return;
            var w = new ByteWriter(48);
            w.U8(Protocol); w.U8(op); w.U32(owner.netId.Value); w.U32(victim ? victim.netId.Value : 0u);
            Vec(w, forward); w.F32(dt); w.Bool(anchor.HasValue); Vec(w, anchor ?? Vector3.zero);
            Mp.SendToServer(CaptureMsg, w.ToArray(), op != 2);
        }

        static void OnCapture(NetworkConnection from, ByteReader r)
        {
            if (r.U8() != Protocol) return;
            byte op = r.U8(); uint ownerId = r.U32(), victimId = r.U32();
            var forward = Vec(r); float dt = r.F32(); bool hasAnchor = r.Bool(); var anchor = Vec(r);
            var owner = Sender(ownerId);
            if (r.Failed || !owner || !Finite(forward) || !Finite(anchor) || float.IsNaN(dt)) { if (op == 3) EndCapture(ownerId); return; }
            if (op == 1)
            {
                var victim = Body(victimId);
                EndCapture(ownerId);
                if (!victim || !victim.healthComponent) return;
                var capture = new EnemyCapture(owner);
                if (capture.TryCapture(victim.healthComponent)) captures[ownerId] = capture;
            }
            else if (op == 2)
            {
                if (captures.TryGetValue(ownerId, out var capture) && !capture.Step(forward, Mathf.Clamp(dt, 0f, 0.1f), hasAnchor ? anchor : (Vector3?)null))
                    captures.Remove(ownerId);
            }
            else EndCapture(ownerId);
        }

        static void EndCapture(uint ownerId)
        {
            if (!captures.TryGetValue(ownerId, out var capture)) return;
            capture.Dispose();
            captures.Remove(ownerId);
        }

        public static void SendReflect(CharacterBody owner, Vector3 centre, float radius, float damageMul, float speedMul)
        {
            if (!owner || owner.netId.Value == 0) return;
            var w = new ByteWriter(40);
            w.U8(Protocol); w.U32(owner.netId.Value); Vec(w, centre); w.F32(radius); w.F32(damageMul); w.F32(speedMul);
            Mp.SendToServer(ReflectMsg, w.ToArray(), false);
        }

        static void OnReflect(NetworkConnection from, ByteReader r)
        {
            if (r.U8() != Protocol) return;
            var owner = Sender(r.U32());
            var centre = Vec(r); float radius = r.F32(), damageMul = r.F32(), speedMul = r.F32();
            if (r.Failed || !owner || !Finite(centre) || float.IsNaN(radius + damageMul + speedMul)) return;
            if ((centre - owner.corePosition).magnitude > 20f) return;
            MeleeReflect.Reflect(owner, centre, Mathf.Clamp(radius, 0f, 20f), Mathf.Clamp(damageMul, 0.1f, 10f), Mathf.Clamp(speedMul, 0.1f, 10f));
        }

        /// Sets an enemy's horizontal velocity (Luigi's Cyclone pull); the vertical part is its own.
        public static void SendPush(CharacterBody victim, float x, float z)
        {
            if (!victim || victim.isPlayerControlled || victim.netId.Value == 0) return;
            var w = new ByteWriter(16);
            w.U8(Protocol); w.U32(victim.netId.Value); w.F32(x); w.F32(z);
            Mp.SendToServer(PushMsg, w.ToArray(), false);
        }

        static void OnPush(NetworkConnection from, ByteReader r)
        {
            if (r.U8() != Protocol) return;
            var victim = Body(r.U32());
            float x = r.F32(), z = r.F32();
            if (r.Failed || !victim || victim.isPlayerControlled || float.IsNaN(x + z) || Mathf.Abs(x) > 60f || Mathf.Abs(z) > 60f || !SomeoneElsesFighterAlive(from)) return;
            if (victim.characterMotor)
            {
                var v = victim.characterMotor.velocity;
                victim.characterMotor.velocity = new Vector3(x, v.y, z);
            }
            else
            {
                var body = victim.GetComponent<Rigidbody>();
                if (body && !body.isKinematic) body.velocity = new Vector3(x, body.velocity.y, z);
            }
        }

        /// The fighter is invulnerable (arrival, recovery): a client asks the server to put the buff on, because buffs are the server's.
        public static void SendProtect(CharacterBody owner, bool on)
        {
            if (!owner || owner.netId.Value == 0) return;
            var w = new ByteWriter(8);
            w.U8(Protocol); w.U32(owner.netId.Value); w.Bool(on);
            Mp.SendToServer(ProtectMsg, w.ToArray());
        }

        static void OnProtect(NetworkConnection from, ByteReader r)
        {
            if (r.U8() != Protocol) return;
            uint id = r.U32(); bool on = r.Bool();
            var body = Sender(id);
            if (r.Failed || !body) return;
            protectedBodies.TryGetValue(id, out bool had);
            if (on && !had) body.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
            else if (!on && had) body.RemoveBuff(RoR2Content.Buffs.HiddenInvincibility);
            protectedBodies[id] = on;
        }
    }
}
