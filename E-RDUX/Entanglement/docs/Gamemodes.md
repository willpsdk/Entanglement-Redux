# Writing a Gamemode

Want to build a custom gamemode—deathmatch, capture the flag, king of the hill, whatever you want? This doc shows you how to do it without touching Entanglement Redux's source code.

Your mode shows up automatically in every host's BoneMenu once you register it, and everything syncs across the network for you.

**Not sure about the module system?** Go read [Modding.md](Modding.md) first—that covers the basics of hooking into Entanglement.

## The Quick Version: A Gamemode You Can Copy

Before we dive deep, here's the simplest possible working example. Copy this and you have a working mode:

```csharp
using Entanglement.Gamemodes;
using UnityEngine;

public class SimpleDeathMatchGamemode : EntanglementGamemode
{
    public override string Id => "simple_dm";
    public override string DisplayName => "Simple Deathmatch";
    public override Color MenuColor => Color.red;
    public override float DefaultRoundSeconds => 5f * 60f;  // 5 minutes

    public override void OnPlayerKilled(long killerId, long victimId) {
        // Ignore suicides and environmental deaths
        if (killerId == victimId || killerId == 0) return;
        
        // Give the killer one point
        AddScore(killerId, 1);
    }
}
```

Register it once in your mod's startup:

```csharp
public override void OnApplicationStart() {
    Entanglement.Gamemodes.GamemodeHandler.RegisterGamemode(
        new SimpleDeathMatchGamemode()
    );
}
```

That's it. It shows up in BoneMenu under `Entanglement Redux > Gamemodes`, and when someone starts it, kills are counted automatically.

Real examples are in `src/Gamemodes/BuiltIn/`—`Deathmatch.cs`, `TeamBattle.cs`, and `LastManStanding.cs` all show more advanced patterns.

## How Gamemodes Actually Work

### Only One Mode Runs at a Time

The host picks an active gamemode from BoneMenu. Once started, that mode's code runs **only on the host's machine**. Here's the crucial part:

> The host runs the game logic. Clients don't. The host broadcasts decisions, and everyone reacts to them the same way.

This is called host-authoritative networking. If you let clients decide who killed who, score changes, or anything else that matters, cheating becomes trivial. Instead:

1. Host detects something happened
2. Host decides the result
3. Host broadcasts it
4. Everyone (including the host) applies that result

You won't notice this when building your mode—you just write `OnPlayerKilled()` and `AddScore()`, and they work. But knowing *why* it's this way helps when you're thinking about what your mode should do.

### Before You Start

- **You need at least 2 players to start a round** (the host counts, so 1 host + 1 client). Solo testing doesn't work.
- **You can't stack rounds.** If one's running, the button refuses until you force stop it.
- **Scores, teams, and eliminated players live in `GamemodeHandler`**, not in your mode. Read/write through the official methods (`SetScore`, `SetTeam`, etc.) so the scoreboard and other systems see the changes.

## Round Length (With On-the-Fly Adjustments)

Override `DefaultRoundSeconds` to set how long your round is:

```csharp
public override float DefaultRoundSeconds => 3f * 60f;  // 3 minutes
```

The host can override this per-lobby without editing code:

`Entanglement Redux > Gamemodes > Host Controls > Round Timer`

The options there are:
- **`Default Length`** — sets a fixed time for all future rounds
- **`Set Time Left`** — changes the timer on a round that's already running
- **`Add/Remove 60 seconds`** — nudge the clock up or down

Your mode doesn't need to do anything—this is all handled for you.

## The Gamemode Lifecycle

These methods are **host-only** (they only run on the host):

| Method | When it fires |
|---|---|
| `OnModeStart()` | Your mode was just selected |
| `OnRoundStart()` | A round was started (by menu, by you, by timer) |
| `HostTick(float deltaTime)` | Every frame, only while your mode is active |
| `OnPlayerKilled(long killerId, long victimId)` | Someone died (see details below) |
| `OnPlayerJoined(long userId)` | A player joined the lobby |
| `OnPlayerLeft(long userId)` | A player left the lobby |
| `OnRoundEnd()` | Round timer hit zero or you called `EndRound()` |
| `OnModeStop()` | The mode was switched off or force-stopped |

These run on **every player's machine**:

| Method | When it fires |
|---|---|
| `OnStateApplied(GamemodeState state)` | Something changed (active mode, round state, timer) |
| `OnEventReceived(GamemodeEventType type, ...)` | A custom event arrived |

**Important:** Don't spawn/despawn things or apply damage in the "every player" methods. Those run once per player in the lobby, so you'd end up doing it N times instead of once. Keep your side-effects in the host-only methods.

## What You Can Call From Your Mode

```csharp
SetScore(userId, score);      // Set a player's score
AddScore(userId, delta);       // Add points to a player
SetTeam(userId, team);         // Assign a player to a team
BroadcastEvent(type, a, b, value, message);  // Send a custom event
StartRound();
EndRound();
```

All of these are host-only. If a client calls them, they silently do nothing.

## Teams and Friendly Fire

Override `UsesTeams => true` and set `TeamCount`:

```csharp
public override bool UsesTeams => true;
public override byte TeamCount => 2;  // Red vs Blue
```

Now call `SetTeam(userId, teamIndex)` to put players on teams. Friendly fire automatically blocks damage between teammates—you don't have to check for it.

### Team Colors

Override `GetTeamColor(byte team)` to give each team a color:

```csharp
public override Color GetTeamColor(byte team) {
    return team switch {
        0 => Color.red,
        1 => Color.blue,
        _ => Color.white  // fallback
    };
}
```

Everyone sees that team's nametags in that color. If someone's talking, the green talking indicator temporarily takes over their nametag, then switches back to their team color when they stop.

## Elimination Mode: "Dead Players Become Invisible"

Override `EliminationMode => true` and when players die, they disappear:

```csharp
public override bool EliminationMode => true;
```

What this actually does:
- Eliminated players' bodies don't render for other players
- Their nametags disappear
- They can still move, talk, and exist in the world—other players just can't see them
- Everyone reappears automatically when the round ends

It's visual only—colliders, physics, and voice chat are untouched.

The current eliminated set is `GamemodeHandler.eliminated`, a `HashSet<long>` of user IDs. Read it if you need to (e.g., to check "how many players are still alive?").

**Example:** `LastManStandingGamemode` uses this—free-for-all with elimination. After every death, it checks if only one player's still standing. If so, that player wins and the round ends (which reappears everyone else).

## Broadcasting Custom Events

Kills, scores, and round start/end cover the basics, but your mode probably has something specific—a flag got captured, a point got scored on the hill. Rather than making a new network message for every possible mode, there's one reserved for this:

```csharp
// Send an event
BroadcastEvent(GamemodeEventType.Custom, a: 0, b: 0, value: 1, message: "flag_captured_red");

// Receive it
public override void OnEventReceived(GamemodeEventType type, long a, long b, int value, string message) {
    if (type != GamemodeEventType.Custom) return;
    
    if (message == "flag_captured_red") {
        // Do something—play a sound, update the UI, whatever
    }
}
```

`a`, `b`, `value`, and `message` mean whatever you want them to. They're yours to use.

## How Kills Actually Get Attributed

You don't send "player A shot player B" messages. Instead, Boneworks itself already knows:
- When you take damage, you know who hit you
- When your health hits zero, you know you died

If those two things happen within 8 seconds of each other, the hit gets credited as the kill.

**If you're the host:** `OnPlayerKilled()` fires directly.

**If you're a client:** Your machine tells the host about the hit first, then the host calls `OnPlayerKilled()` and tells everyone.

`OnPlayerKilled()` fires for *every* death while your mode is active—falls, NPCs, environmental damage, all of it. Deaths with no clear attacker have `killerId == 0`. Check for that if you only want PvP kills:

```csharp
public override void OnPlayerKilled(long killerId, long victimId) {
    if (killerId == 0 || killerId == victimId) return;  // ignore environmental deaths and suicides
    
    AddScore(killerId, 1);
}
```

## What Happens When Players Die

Physically, nothing special happens. They ragdoll the same way they always do, Boneworks respawns them however it normally does—the gamemode framework just watches and quietly scores kills in the background.

**Big gap:** There's currently no respawn system in the framework. No "wait 5 seconds and come back," no spawn points. If your mode needs respawning, you build it yourself. Hook `PlayerDeathManager.OnLocalPlayerDied` the same way `GamemodeHandler` does, then drive your own timer and teleport.

This is the biggest thing missing right now, and if you want it as a shared feature instead of something every mode reinvents, that's worth asking for.

## Stopping a Mode

The host can force-stop from BoneMenu or by calling `GamemodeHandler.StopMode()`:

```csharp
if (someCondition) {
    Entanglement.Gamemodes.GamemodeHandler.StopMode();
}
```

When a mode stops:
1. If a round is running, `OnRoundEnd()` fires first (so you can clean up)
2. Then `OnModeStop()` fires
3. All scores, teams, and eliminated players are wiped
4. Switching to a new mode starts fresh

## What's Not There (Yet)

- **No respawn system** (you can build one—see above)
- **No custom HUD.** The scoreboard is text-based, refresh-on-demand from BoneMenu
- **No win/lose screen** (you trigger it from `OnRoundEnd`, then you build what it looks like)

These aren't bugs—they're intentionally minimal so each mode can do its own thing instead of fighting a one-size-fits-all framework.

## Putting It All Together

Here's a slightly more complete example—a team deathmatch mode:

```csharp
public class TeamDeathMatchGamemode : EntanglementGamemode
{
    public override string Id => "team_dm";
    public override string DisplayName => "Team Deathmatch";
    public override Color MenuColor => Color.cyan;
    public override float DefaultRoundSeconds => 10f * 60f;
    
    public override bool UsesTeams => true;
    public override byte TeamCount => 2;
    
    public override Color GetTeamColor(byte team) {
        return team == 0 ? Color.red : Color.blue;
    }

    public override void OnRoundStart() {
        // Put players on teams when the round starts
        int playerCount = 0;
        foreach (long userId in Node.activeNode.connectedUsers) {
            SetTeam(userId, (byte)(playerCount % 2));
            playerCount++;
        }
    }

    public override void OnPlayerKilled(long killerId, long victimId) {
        if (killerId == victimId || killerId == 0) return;
        AddScore(killerId, 1);
    }

    public override void OnPlayerJoined(long userId) {
        // Assign new players to the team with fewer people
        int team0Count = GamemodeHandler.teams.Values.Count(t => t == 0);
        int team1Count = GamemodeHandler.teams.Count - team0Count;
        SetTeam(userId, (byte)(team1Count > team0Count ? 0 : 1));
    }
}
```

Register it, start it, and watch it work.

---

**Have questions?** Check out the built-in modes in `src/Gamemodes/BuiltIn/` for real-world examples. They're well-commented and show patterns for elimination, teams, scoring, and events.
