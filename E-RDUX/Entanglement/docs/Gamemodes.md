# Writing a Gamemode

This is how you add a competitive mode - deathmatch, capture the flag, whatever you want -
without touching Entanglement Redux's own source. Register your mode from your own
MelonLoader mod and it shows up in every host's Gamemodes menu on its own.

Three modes ship with the mod as working examples - `Deathmatch`, `TeamBattle`, and
`LastManStanding` - sitting in `src/Gamemodes/BuiltIn/`. If something below is unclear, those
are short and probably answer it faster than this doc will.

If you haven't read `Modding.md` yet, do that first - it covers the module system and custom
network messages, which this builds on top of.

## The basic idea

Only one gamemode is active at a time. The host picks it from BoneMenu
(`Entanglement Redux > Gamemodes > Host Controls`) or by calling `GamemodeHandler.StartMode(id)`
directly.

The important thing to understand: **only the host's copy of your mode actually runs the
game logic.** `HostTick`, `OnPlayerKilled`, `OnRoundStart` - none of that fires on a client.
What happens instead is the host decides something, broadcasts the result, and every machine
(including the host itself) reacts to that broadcast the same way. It's the same pattern this
mod already uses for who owns a grabbed object or who's allowed to spawn what - one source of
truth, everyone else just applies what it says. If you've worked with authoritative-host
networking before this will feel familiar; if you haven't, the short version is: don't trust
a client to decide anything that matters, only to report what happened to it and react to
what the host says.

Scores and teams live in `GamemodeHandler`, not in your mode. Call `SetScore`/`AddScore`/
`SetTeam` and read them back from `GamemodeHandler.scores`/`GamemodeHandler.teams` - don't
keep your own copies, because other things (the scoreboard, friendly fire) read directly from
those dictionaries and won't know about a copy you're keeping on the side.

## A small example

```csharp
using Entanglement.Gamemodes;
using UnityEngine;

public class KingOfTheHillGamemode : EntanglementGamemode
{
    public override string Id => "koth";
    public override string DisplayName => "King of the Hill";
    public override Color MenuColor => Color.yellow;
    public override float DefaultRoundSeconds => 5f * 60f;

    public override void OnPlayerKilled(long killerId, long victimId) {
        if (killerId == victimId || killerId == 0) return;
        AddScore(killerId, 1);
    }
}
```

And register it, once, from your mod's startup:

```csharp
Entanglement.Gamemodes.GamemodeHandler.RegisterGamemode(new KingOfTheHillGamemode());
```

Registration just adds to a dictionary, so there's no strict ordering requirement against
Entanglement's own startup - it only needs to happen before a host tries to pick your mode
from the menu, which in practice means "call it from your own `OnApplicationStart`" is fine.

## Round length

Override `DefaultRoundSeconds` to set how long your round runs - `RoundTimeRemaining` gets
set from it automatically whenever `StartRound()` is called, so you don't touch the timer
yourself (its setter is private to `GamemodeHandler` on purpose - a mode writing to it
directly would fight with the BoneMenu controls below).

A host can override this per-lobby without editing any code: `Gamemodes > Host Controls >
Round Duration` sets a fixed length that wins over your mode's default for every round after
that, until it's set back to 0. There's also a `Set Time Remaining` field and `+60s`/`-60s`
buttons for nudging the clock on a round that's already running - useful if a round's dragging
on or you want to cut one short without ending it outright. None of this requires your mode to
do anything; it's handled entirely in `GamemodeHandler`.

## The lifecycle

Host-only - these never run on a client's machine:

| Method | Fires when |
|---|---|
| `OnModeStart()` | your mode was just selected |
| `OnModeStop()` | the host switched away or turned gamemodes off |
| `OnRoundStart()` | someone called `StartRound()` - the menu, you, your own timer, doesn't matter |
| `OnRoundEnd()` | the round timer hit zero, or you called `EndRound()` yourself |
| `HostTick(float deltaTime)` | every frame, only while your mode is the active one |
| `OnPlayerKilled(long killerId, long victimId)` | see "kill attribution" below |
| `OnPlayerJoined` / `OnPlayerLeft` | someone joined or left the lobby while your mode is running |

Everywhere - host and every client, once something round-trips back down:

| Method | Fires when |
|---|---|
| `OnStateApplied(GamemodeState state)` | the active mode, round state, or timer changed |
| `OnEventReceived(...)` | a `BroadcastEvent` call landed |

Don't spawn or despawn anything, or apply damage, from `OnStateApplied`/`OnEventReceived`.
Those run on every machine in the lobby at once - anything with a real side effect belongs in
the host-only methods, or you'll end up doing it once per player instead of once.

## What you get to call

```csharp
SetScore(userId, score);
AddScore(userId, delta);
SetTeam(userId, team);
BroadcastEvent(type, a, b, value, message);
StartRound();
EndRound();
```

All of these are host-only - calling them from a client build of your mode just does nothing,
since `GamemodeHandler` checks `Node.isServer` before touching any state.

## Teams and friendly fire

Set `UsesTeams => true` and pick a `TeamCount`. Once you've called `SetTeam` on a player,
damage between two players on the same team gets blocked automatically, before it's even
applied - you don't have to check for it yourself in `OnPlayerKilled` or anywhere else. Who
goes on which team is entirely up to you; `TeamBattleGamemode` just round-robins new arrivals,
which is about as simple as it gets, but you could just as easily let players pick.

Override `GetTeamColor(byte team)` to give each team its own nametag color - return whatever
`Color` you want for a given team index, and every player on that team shows up tinted that
way to everyone else. `TeamBattleGamemode` does this with a small fixed array (red, blue,
green, yellow); if `team` is out of range for whatever you return, players just default to
white. This only touches the nametag - it doesn't recolor the player model itself. If someone's
talking, the green talking-indicator color takes over their nametag temporarily and reverts to
their team color once they stop, so the two don't fight each other.

## Elimination mode

Override `EliminationMode => true` and dying removes a player from the round: their body
stops rendering for everyone else and their nametag disappears, until the next round starts.
It's opt-in and off by default on both built-in modes - nothing about existing games changes
unless a mode asks for it.

A few things worth knowing about what this actually does:

- It's visual only. The eliminated player can still move around, talk, and take up space in
  the world - other players just can't see or hear-locate them by nametag anymore. It doesn't
  touch colliders, physics, or voice chat.
- It's tracked separately from kills. A death always calls `OnPlayerKilled` the same as
  before; elimination is layered on top of that, not instead of it.
- Everyone reappears automatically the moment a round ends - naturally, via `EndRound()`, or
  because the host force-stopped the mode - so nobody's left invisible after the fact waiting
  for a round that isn't coming.
- `GamemodeEventType.PlayerEliminated` fires (with `a` set to the eliminated player's id)
  whenever this happens, if you want to react to it - print something, play a sound, whatever.
- The current eliminated set is `GamemodeHandler.eliminated`, a plain `HashSet<long>` of user
  ids, same pattern as `scores` and `teams`.

This pairs naturally with something like a last-man-standing mode, but nothing stops you from
using it for anything where "dead players shouldn't be visible until the round resets" makes
sense.

`LastManStandingGamemode` is the built-in example of this - it's a free-for-all with
`EliminationMode => true`, and it watches the alive count after every death. Once only one
player is still standing, they get a survival bonus on top of whatever kills they scored and
the round ends on the spot, which reappears everyone (see "Stopping a mode" below - a round
ending always clears elimination, whether it ends on a timer, by hitting the win condition, or
by force stop). It won't auto-end a solo lobby, since there's nobody left to call a winner -
useful if you just want to trigger an elimination by hand and watch a player vanish/reappear
without needing a second person connected.

## Your own events

Kills, scores, and round start/end cover the obvious cases, but your mode is probably going
to need something specific to it - a flag got captured, a hill changed hands. Rather than
inventing a new network message for every possible gamemode, there's one type reserved for
exactly this:

```csharp
BroadcastEvent(GamemodeEventType.Custom, value: someInt, message: "flag_captured:red");
```

and on the receiving end:

```csharp
public override void OnEventReceived(GamemodeEventType type, long a, long b, int value, string message) {
    if (type != GamemodeEventType.Custom) return;
    if (message == "flag_captured:red") { /* whatever you want to happen */ }
}
```

`a`, `b`, `value`, and `message` don't mean anything to the framework itself - they're yours
to use however your mode needs.

## How kill attribution actually works

There's no "who shot who" message. Two things that already exist in the core PvP code do the
work:

- when you take damage from another player, your machine finds out who hit you
- when your health hits zero, your machine finds out you died

If those two happen within 8 seconds of each other, the earlier hit gets credited as the
kill. If you're the host, `OnPlayerKilled` just fires directly. If you're a client, a short
report goes to the host first, and the host is the one who actually calls `OnPlayerKilled`
and tells everyone what happened - same reasoning as everywhere else in this framework, the
host decides, nobody else gets to.

`OnPlayerKilled` fires for every death while your mode is active, not just ones another
player caused - fall damage, an NPC, walking off a ledge, all of it. A death with no recent
hit behind it just reports `killerId == 0`. Check for that if you only want to count actual
PvP kills; both built-in modes do exactly this. It fires for everything, rather than only
attributed kills, because elimination mode (above) needs to catch every death a player has,
not just the ones another player gets credit for.

## What happens when you actually die

Physically, nothing changes. Dying in a gamemode looks exactly like dying anywhere else in
Entanglement - your ragdoll spawns for everyone the same way it always does, and whatever
happens after that (a level reload, a checkpoint, however Boneworks itself handles it) is
untouched by any of this. The gamemode framework doesn't sit in that path; it just watches it
go by and, if it can attribute the death to another player, quietly scores a kill in the
background. You won't notice anything different in the moment you die - unless the active
mode has `EliminationMode` turned on, in which case your body and nametag disappear for
everyone else right away. See "Elimination mode" above for exactly what that does and doesn't
cover.

That also means there's currently **no respawn system** - no "you come back in 5 seconds," no
spawn points, nothing like that. If your mode needs one, you'd build it yourself: hook
`PlayerDeathManager.OnLocalPlayerDied` the same way `GamemodeHandler` already does, and drive
your own timer/teleport from there. This is the single biggest gap in the framework right
now, and if you'd rather it existed as a shared thing instead of something every mode
reinvents on its own, that's a reasonable thing to ask for.

## Stopping a mode

The host can shut a mode off from `Gamemodes > Host Controls > Force Stop Gamemode`, or by
calling `GamemodeHandler.StopMode()` directly. It works whether or not a round is currently
running - if one is, your mode gets `OnRoundEnd()` first so it can clean up (stop a capture
timer, whatever it needs to do) before `OnModeStop()` runs and everything gets cleared.
Scores, teams, and the eliminated list are all wiped once the mode's stopped, so switching to
a different mode afterward starts from a clean slate.

## What's missing on purpose (for now)

- **No respawn/spawn-point system** - covered above.
- **No UI beyond the BoneMenu scoreboard.** `Entanglement Redux > Gamemodes > Scores` is a
  plain text list you refresh by pressing a button - BoneMenu doesn't do live-updating
  elements, so that's about as fancy as it gets without you building your own HUD.
- **No win/lose screen.** `OnRoundEnd` is where you'd trigger one; what it looks like is up to
  you.
