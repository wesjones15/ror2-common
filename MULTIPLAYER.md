# Multiplayer for Mario and Olimar

Status: **implemented on the `multiplayer` branches, never run in a real two-player game.** The wire
formats, codecs and native replica path have headless tests; the in-game behaviour is what the test plan
at the end is for. Everything here is offline / friends-only co-op, on RoR2 1.2.4.4 with no R2API.

## The rule that shapes everything

Neither character is a normal RoR2 survivor: the body is Commando's, and the Mario or Olimar you see is
drawn by a native renderer (`sms.dll`, `pikmin.dll`) from a sim that runs on one machine and from
assets on that machine's own disc. RoR2 replicates the Commando body (position, health, skills);
nothing replicates the sim. So each machine needs the plugin, the assets (its own disc, extracted on first
run), and **the same plugin build**: a mismatched ABI or protocol is refused and logged, and the other
player's character then looks like Commando.

## A client's body at a stage start (`ror2-common/src/ClientSpawn.cs`)

UNet creates a client's own body at the server's spawn point, and its authority over it arrives in a
later message. Until then RoR2's `CharacterNetworkTransform` treats it as another player's body and,
with no snapshot of its own yet, blends it from a zeroed snapshot at the world origin toward the
server's position; when authority arrives the blend stops part way, on the line from (0, 0, 0) to the
spawn. A native sim that started there fell under the map at every stage start from stage 2 (stage 1's
pod moves the body itself); RoR2's out-of-bounds rescue then put the player on the nearest node, which on
the Moon is far away. Every native driver (Mario, Olimar, each Melee fighter) remembers where its body was
created (`ClientSpawn.Remember` in Awake) and, on a client, puts it back there before its sim first starts
(`ClientSpawn.Settle`, logged as `Client spawn: ... put back at its spawn point`).

## Transport (`ror2-common/src/Mp.cs`)

Plain UNet, no R2API: `NetworkServer.RegisterHandler` / `SendByChannel` through RoR2's public
`NetworkManagerSystem.onStartServerGlobal` / `onStartClientGlobal` events. The UNet weaver doesn't run for
our DLLs, so no `[SyncVar]` or `[Command]`; each message is a byte payload (`ByteWriter` / `ByteReader`)
in a `MessageBase`. Payloads over 1100 bytes are split into 1000-byte fragments and put back together
(`Fragments.cs`); a newer message from the same sender abandons an older unfinished one. A host plays both
ends: its own `SendToServer` runs the server handler directly, and the server's sends reach its own client
side, so relays ignore what came from their own side.

Message types: Mario `0x4D01..0x4D05`, Olimar `0x4F01..0x4F03`.

## Mario

The player's machine runs the real sim (as before). For every physics step it records the calls it made
on it (`SimOps`: the pad and camera for the step, aim, tuning, limits, refills, launches, knock-downs,
teleports) plus where the sim ended up, and sends batches of 3 frames (about 20 a second) to the server,
which relays them to everyone else (`MarioFrames`, `MarioNet`, reliable channel).

Everyone else runs a **shadow sim** of libsms on those same calls (`MarioPuppet`), about three frames
behind, and draws it with the same code as the player's own Mario: the models, FLUDD's water and
refraction, the dust and flames, and his sounds from where he is (libsms's view takes the camera as the
listener). The shadow sim has no enemies, so the recorded position pulls it back when a stomp or push
makes the two differ (more than 30 sim units, about a third of a metre). RoR2's own network transform
still moves the body; Commando's model stays hidden under the puppet.

What goes the other way, because damage is the server's:
- **Hits** (stomps, pounds and their shock waves, dives, FLUDD's drops, super-pound blasts, fall damage,
  Shuriken's trigger): the client sends them (`CombatMsg`) and the server lands them as that Mario's, with
  his damage stat, crits, on-hit items and kill credit.
- **Knock-downs**: the server sees the powerful hit and tells the owner (`KnockMsg`), whose sim does the fall.
- **Healing into water and Bandolier**: the server's copy collects what the tank is owed and sends it
  (`WaterMsg`).
- **Coins**: the server drops and picks them up; every machine draws a copy (`CoinMsg`), and the
  pickup sound and sparks play on the collector's sim wherever it is.
- **Death**: another player's Mario falls over for good like your own does.

## Olimar

The **host's sim is the only real one** (it needs RoR2's enemies and does the damage). About 10 times a
second (`Net.SnapshotHz`) libpikmin encodes every entity (captains, Pikmin, pellets, Onions, ships,
sprouts, nectar), the creatures' positions and the events since the last one (`pik_sim_snapshot`), and
`PikminWorld` wraps that with which Olimar is which captain, his Onion's state and the arrival's
progress (`PikminEnvelope`). Every other machine queues it for a **replica sim** (`pik_replica_apply`)
and plays the queue back on a steady clock (`pik_replica_update`), a little behind the newest
snapshot the server is estimated to have sent: the usual gap between snapshots plus a margin for how
unevenly they arrive (it widens quickly when they bunch up and narrows slowly). Positions blend between
the two snapshots either side of the clock and each snapshot's events play as it is reached, so the
swarm moves evenly even when the network delivers snapshots in clumps (blending toward each one as it
landed made the Pikmin move in steps). The presentation layer draws and sounds it like the real thing. A busy snapshot is about 8 KB for 60 Pikmin; `Debug.LogNet` logs the bandwidth.

A client playing Olimar **moves himself** so he feels no lag: a small local sim with only his captain
runs on his controls (the same code as the host's), his body follows it, and the camera and HUD work as
the host's do. The server's captain for him follows the pose it sends (the same machinery as VR), and
does everything with the squad: throws, whistles, swarming, the dismiss strike, the Dolphin and Onion. The
Pikmin therefore respond a round trip late, but he doesn't. His Onion commands (X, the menu) are
messages the server carries out. His model is drawn in the replica at his locally predicted pose.

## What is not done

- Never run in a game. See the test plan.
- Mario: Headstompers' blast from a client Mario, and items that read a skill slot's state on the server
  (the server's copy of a client's slots isn't the real one), are best effort.
- Mario puppets don't collide with enemies; they're corrected by the recorded position instead.
- Olimar: items that act through the sim (Pluripotent Larva's squad, wards healing Pikmin and the like) run
  on the server and use its copy of a client's body, so they follow the stats RoR2 replicates.
- A dedicated server (no local player) is not a target.
- Late joiners start from the next periodic refresh (about 3 seconds) of Mario's tuning and limits.
- Bandwidth for Olimar is about 100 KB/s with a full squad at 10 snapshots a second. There is no
  compression yet; lower `Net.SnapshotHz` on a thin connection.

## Test plan (two machines, both with the same builds and assets)

Install the same Mario and Olimar builds on both machines, with their own disc images set. Start the
game on both; check each `BepInEx/LogOutput.log` shows `libsms ABI 20` and `libpikmin ABI 25` before joining.

1. **Plain join.** Host a game, the second player joins, both pick Commando. Nothing in either log about
   Mp or version mismatch.
2. **Mario seen by others.** One picks Mario, the other Commando. The Commando player should see a Mario
   model, not Commando, moving, jumping, ground pounding; FLUDD's water and the dust. Walk away and
   check his sounds fade. The log line `Showing another player's Mario` appears on the watcher's side.
   Repeat with the roles swapped (client Mario seen by the host).
3. **Mario hits.** The client Mario stomps, sprays and ground pounds an enemy: it takes damage, and kills
   drop a yellow coin that **both** players see spin and can pick up (the sound plays for the collector).
   Fall from a height: damage applies. Get hit hard: he falls over.
4. **Mario dies.** The other player sees him fall down for good.
5. **Olimar seen by others.** The host picks Olimar, the client Commando (and vice versa). The watcher sees
   Olimar, the Pikmin, the Onion and the Dolphin on arrival, and hears them. Log: `Showing the host's Pikmin`.
6. **Client Olimar.** The client picks Olimar: he should move without lag, throw, whistle and swarm (the
   Pikmin answer a moment later), open the Onion menu (D-pad up), call Pikmin out, put them away, raise
   and land the Onion with X. Log on the client: `... moves on this machine`; on the host: `captain N is ...`.
7. **Both Olimar.** Host and client both Olimar: two squads, two Onions, whistles stealing Pikmin.
8. **Mixed.** One Mario and one Olimar.
9. **Stage change.** Advance a stage with Pikmin along; the replica should restart without errors.
10. **Bandwidth.** Set `Debug.LogNet` on the host and read the `Pikmin snapshots:` line.

If something is off, send both machines' `LogOutput.log`. Useful switches: Olimar `Debug.LogNet`,
`Net.SnapshotHz`, `Net.ReliableSnapshots`; Mario `Debug.LogStatus`.

# Melee

Status: **implemented, never run in a real two-player game.** The native record and replay are tested headless for all eight
fighters (`libssbm-ffi/tests/replay.rs`, NativeCheck); the network messages are not.

Every Melee fighter works the same way, through the shared `MeleeDriver` and `MeleeNet`:

- **The owner runs the real sim.** The machine that owns a player's body (a client for its own player, the server for the rest)
  runs the fighter exactly as in single player, so a client's fighter feels no lag. With other players in the game it records
  the calls it makes on the sim (`ssbm_sim_record`: each step's pad and time, placements, hurts, grabs, tuning, the lock-on
  target and so on, plus where the sim ended up after each step) and sends them three steps at a time (about 20 messages a
  second, reliable) to the server (`MeleeNet.FramesMsg`, `0x4C01`), which relays them to everyone else.
- **Everyone else runs a shadow sim** (`ssbm_sim_replay`) of that fighter on those calls, a few steps behind: each batch is split
  into its steps (`ssbm_ops_steps`) and one step is replayed per physics step, as the owner ran them (a backlog is caught up two
  at a time, a long one at once), and the fighter is drawn between its last two steps. Replaying a whole batch at once moved
  it three steps every third physics step, which looked like jerky 20 Hz movement. It draws it with
  the same code as the owner's own fighter: model, animation, sounds (fading with distance), effects, KO and recovery.
  The shadow makes no attacks, reads no pad and moves no body; RoR2's own network transform still moves the Commando body,
  which stays hidden under it. It has no enemies of its own, so the recorded position pulls it back when it drifts more than
  1.5 sim units (0.15 m). Settings the owner repeats every step are logged when they change and again every two seconds, so a
  late joiner catches up.
- **What only the server may do** is a message from the owner (`MeleeNet`): damage (`HitMsg` `0x4C02`, landed by the server
  as that fighter's, with his damage stat, crits, on-hit items and kill credit), holding an enemy helpless (`HoldMsg`: Yoshi's
  egg, Donkey Kong's bury and slap), capturing and carrying one (`CaptureMsg`: every grab and Samus's grapple), turning enemy
  projectiles back (`ReflectMsg`: Fox's Reflector), pulling an enemy (`PushMsg`: Luigi's Cyclone) and the fighter's
  protection buff after a KO (`ProtectMsg`). The server checks that the sender is a Melee fighter that is not its own and
  clamps what it is told (damage, force, reach, hold time).
- The attacks, shots and sparks a fighter makes are made on the owner's machine: the fighters' combat code now asks
  `MeleeNet.Acts(body)` (this machine owns the body) where it used to ask whether it was the server.

Not done:
- **Shots and other move objects are drawn only on the machine that fired them**: Fox's Blaster, Link's arrows, boomerang and
  bombs, Samus's shots and missiles, Yoshi's eggs and stars, Pikachu's Thunder bolt and Thunder Jolt, Luigi's fireballs.
  Other players see the fighter throw, but not the object (its damage is real). The move effects the sim itself
  spawns (glows, body particles, sounds) show for everyone; hit sparks and the explosions a combat class spawns show on the
  owner's machine, and everyone's only when the server spawned them.
- A shield's meter is each machine's own: the server's shadow decides whether a hit is blocked, and the owner's shield does
  not drain from it.
- The feather jump burst, and a client fighter's grab/throw knockback strength, are best effort.
- Late joiners see a fighter from the next batch, without its arrival.
- A player who does not have the Melee plugin sees a Commando; a different build is refused and logged once.

Test plan (two machines, the same Melee build and each its own Melee disc extracted):

1. **Plain join.** Host a game, the second player joins, both pick Commando. Nothing about MeleeNet or a mismatch in either log.
2. **Host fighter seen by the client.** The host picks a fighter, the client Commando. The client should see the fighter
   (not Commando) walk, jump, attack, take damage and hear it. Log: `<Fighter>: showing another player's fighter`.
3. **Client fighter seen by the host.** Swap roles. The client plays every fighter in turn: it should feel normal (no lag).
4. **Client hits.** The client's fighter hits an enemy with a jab, a smash, a special: the enemy takes damage and shows
   damage numbers; kills credit the client.
5. **Holds and grabs.** Client Yoshi lays an egg on an enemy, Donkey Kong buries and slaps, any fighter's grab carries an
   enemy and throws it, Luigi's Cyclone pulls, Fox's Reflector turns a shot.
6. **Recovery.** Fall off the map as the client: the Melee KO plays for both and the client is invulnerable while it recovers.
7. **Death.** The other player sees the fall and KO.
8. **Two Melee players**, then Melee with Mario or Olimar.
9. **Stage change.** Advance a stage; both fighters come back without errors in the log.

If something is off, send both machines' `LogOutput.log`.
