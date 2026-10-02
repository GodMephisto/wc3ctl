# FarmerVsHunterX 2.68, Red never gets a farmer

## Symptom

With Hunter Assistants enabled, Player 1 (Red) spawns no farmer. Chat also shows Red's own name
leaving the game once per assistant, while Red is still playing.

## Cause

Trigger `pr` (built at `war3map.j` line 31490) carries two kinds of event, and its handler
assumes a triggering player exists in both.

```jass
31493  call TriggerRegisterPlayerEvent(LQ,FQ,EVENT_PLAYER_LEAVE)   // players 0..8 and 11
31520  call TriggerRegisterPlayerEvent(LQ,FQ,EVENT_PLAYER_LEAVE)
31521  call Z7(pr,"Eu",GREATER_THAN,0.)                            // TriggerRegisterVariableEvent
31522  call TriggerAddAction(pr,up)                                // up = function GX (set at 26898)
```

A variable event has no triggering player, so `GetTriggerPlayer()` returns null and
`GetPlayerId(null)` reads as **0**, which is Red.

Handler `GX` at line 6086 takes the leave branch whenever `Eu` is zero, without checking that
anyone left.

```jass
6098      set PX=GetTriggerPlayer()          // null here, resolves to Red
6099      call DisplayTextToForce(... "|r has left the game.")
...
6120  call SmJ(PX)
```

`SmJ` at line 6045 then removes that player from the farmer force.

```jass
6055  call ForceRemovePlayerSimple(Ht,Yt)     // Yt is the farmer force
```

Four assistants means four spurious fires, so the force goes from one member to zero before the
spawn. `ForForce` over an empty force is a silent no-op, which is why nothing errors.

## Fix

Guard at line 6098.

```jass
set PX=GetTriggerPlayer()
if PX==null or GetPlayerSlotState(PX)==PLAYER_SLOT_STATE_PLAYING then
    return
endif
```

A player still playing has not left. This cannot suppress a real departure, because the engine
moves a genuine leaver out of `PLAYER_SLOT_STATE_PLAYING` before `EVENT_PLAYER_LEAVE` fires.

In the World Editor this is the trigger holding both `Player - Player N leaves the game` events
and a `Game - Eu becomes Greater than 0.00` variable event. Best fix is to split those into two
triggers, since only the player events have a triggering player. Quick fix is an `If` at the top
of the leave branch, skip remaining actions when `Player slot status of (Triggering player)`
equals `Is playing`.

## Evidence

Measured in game with an instrumented build.

```
[FVH] FiJ swept 1 player(s), added 1        <- force built correctly, Red in it
Hunter Assistant(s): 4
[FVH] leave fired, Eu=0 triggerPlayerId=0   <- four times
[FVH] force holds 0 player(s) at spawn time <- force emptied before the spawn
```

Confirmed fixed by a build carrying the guard above and nothing else changed.
