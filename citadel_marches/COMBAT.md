# Combat

save3 has locomotion and bone-hitbox flags. It has no attack input, no attack event, and no damage application. Health already exists as `PhysicsComponent.Health` (100 on every entity in `project.json`). `UseBoneHitboxes` and `RagdollSimulationEnabled` are already serialized.

This document adds one melee intent. Names below are proposed. They are not types found in save3.

## Rule

The client never writes health. It publishes an intent. `ServerValidationSystem` accepts or rejects. Damage is applied on the server, then an ordinary (not protected) resolved event is published so every client can play the swing and the hit flash.

This matches movement: `PlayerMovement` already sends a request through `sendMovementRequest` and `ClientPredictionSystem`, and the server is the authority.

## Events

```csharp
// Client → server. Not trusted.
public sealed class AttackRequest
{
    public int AttackerId { get; set; }
    public Vector3 AimDir { get; set; }   // camera forward, normalized
    public int AttackId { get; set; }     // 0 = light melee, v1 only
    public int ClientTick { get; set; }
}

// Server → clients. Ordinary EventBus event so it replicates.
public sealed class AttackResolvedEvent
{
    public int AttackerId { get; set; }
    public int HitEntityId { get; set; }  // 0 = whiff
    public int Damage { get; set; }
    public int HitBone { get; set; }      // index from the pack BoneNameToIndex, or -1
    public int ClientTick { get; set; }
}
```

Publish with the same `_eventBus.Publish(...)` used by `InventoryHud` for `OpenGameHudEvent`.

## Validation

`ServerValidationSystem` already documents speed, distance, frustum, and occlusion checks. Melee adds:

- Cooldown. Light melee, 0.55s. Reject if `ClientTick` is inside the window. Store the last accepted tick on the attacker, not on the client.
- Range. 1.8m from attacker position to target position, plus a forward cone of about 70 degrees around `AimDir`. No hits behind the attacker.
- Occlusion. If the documented occlusion check can see the target, use it. Otherwise a segment test against static bodies (walls are BodyType 0).
- Target must have `CollisionEnabled` and Health > 0.
- One hit per accept. No cleave in v1.

On accept: subtract damage from `PhysicsComponent.Health`. Light melee is 18. At 0, set `RagdollSimulationEnabled = true` and stop accepting further attacks on that entity until respawn.

On reject: publish nothing. The client does not play a hit.

## Hit query

Preferred: bone hitboxes, because `UseBoneHitboxes` is already on the component and the skeleton is already named. Weapon bone is `hand_r`. A hit records the bone index from the pack's `BoneNameToIndex`.

Fallback, if bone queries are not exposed to scripts yet: a capsule in front of the attacker, 1.6m long, 0.4m radius, and the first dynamic or pawn body inside it. The greenball (BodyType 2, ids 4 and 5 in the current scene) is the dummy either way.

## Animation

No new mesh. Drive the existing clips from locomotion state. The attack is a one-shot on top of the upper body. save3 clips are standing, walking, running, left run, full right turn, jump, in-air, landing. There is no attack clip in the pack. Until one is authored, play the turn clip as a placeholder swing and put the real clip name behind a constant (`AttackClip = "FullRightTurn"`) so the swap is one line.

## Input

Mouse left press, from the same key/mouse path `InventoryHud` already uses (`KeyInputEvent` / `InputHandler`). Do not put attack logic in a `[CustomPlayerController]`. The slide controller proved the attribute works by overwriting velocity after `base.Update`; combat must not do that. A `[RegisterGameSystem]` reads input and publishes `AttackRequest`. Movement stays on stock `PlayerMovement`.

## First script

Drop in `Scripts/CombatSystem.cs` after the clone. It compiles against the same references as `InventoryHud`. The server half is a stub that applies damage locally until `ServerValidationSystem` is opened and the request is routed through it — the comment marks that seam.

```csharp
using SiegeEngine.Core.Events;
using SiegeEngine.Core.Interfaces;
using SiegeEngine.Core.Managers;
using SiegeEngine.Systems;
using System;

namespace ProjectScripts
{
    public sealed class AttackRequest
    {
        public int AttackerId { get; set; }
        public int AttackId { get; set; }
        public int ClientTick { get; set; }
    }

    public sealed class AttackResolvedEvent
    {
        public int AttackerId { get; set; }
        public int HitEntityId { get; set; }
        public int Damage { get; set; }
        public int HitBone { get; set; }
    }

    [RegisterGameSystem]
    public sealed class CombatSystem : GameSystem
    {
        private readonly EventBus _eventBus;
        private int _tick;
        private int _lastAttackTick = -1000;
        private const int CooldownTicks = 33; // ~0.55s at 60hz
        private const int LightDamage = 18;

        public CombatSystem(IGameServer server, EventBus eventBus) : base(server)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _eventBus.Subscribe<AttackRequest>(OnAttack);
            // Mouse button wiring matches InventoryHud's KeyInputEvent path.
            // Bind the left button here once InputHandler exposes it the same way Key.I is exposed.
            Console.WriteLine("[CombatSystem] attack intent online");
        }

        public override void Update(float deltaTime) { _tick++; }

        public void RequestLightAttack(int attackerId)
        {
            if (_tick - _lastAttackTick < CooldownTicks) return;
            _eventBus.Publish(new AttackRequest
            {
                AttackerId = attackerId,
                AttackId = 0,
                ClientTick = _tick
            });
        }

        private void OnAttack(AttackRequest req)
        {
            if (req == null) return;
            if (req.ClientTick - _lastAttackTick < CooldownTicks) return;
            _lastAttackTick = req.ClientTick;

            // Seam: replace this local resolve with ServerValidationSystem.
            // Range, cone, occlusion, and the greenball / bone query belong there.
            _eventBus.Publish(new AttackResolvedEvent
            {
                AttackerId = req.AttackerId,
                HitEntityId = 0,
                Damage = LightDamage,
                HitBone = -1
            });
        }
    }
}
```

## Phase 2 — shoot

A bow is not a second melee. It is a server ray:

- `AttackRequest` with `AttackId = 1`, aim direction required.
- Server steps a segment out to 40m against static geometry and bone hitboxes.
- Travel time is cosmetic on the client. The hit is decided on the server at fire time (hitscan) for v1. A visible arrow is a replicated effect, not the authority.
- No client-spawned physics body as the projectile. The bowling project already showed how easy it is for a client-side body to leave the lane.

## Done when

- Left click on the greenball reduces its `PhysicsComponent.Health` from 100, only if the server accepted.
- A second click inside 0.55s does nothing.
- Health at 0 sets `RagdollSimulationEnabled`.
- A second client sees the same health.
