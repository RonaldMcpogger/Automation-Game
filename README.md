# First Factory prototype

Open `Assets/Scenes/MainScene.unity` in Unity **6000.5.10f1** and press Play.
MainScene is also the first enabled scene in Build Settings.

## This week's loop

1. Walk to the brown **Iron Ore Spot** and look at it. Hold **E** to gather ore
   (one every 0.4 seconds, pouch capacity 10).
2. Look at the **Smelter** and press **E** to transfer as much ore as its buffer
   can accept (capacity 10). Each ore takes 2 seconds to become one iron ingot.
3. Ingots automatically enter the existing curved conveyor and reach the
   **Submission Bin**. Deliver 10 ingots to complete the first objective.

WASD moves, mouse looks, Shift sprints, Space jumps, and Esc releases the cursor.
Click to resume. The HUD shows your pouch, smelter state, delivery count, and the
interaction prompt for the station you are looking at. The smelter lamp is yellow
while idle, green while processing, and red while finished output is waiting.

The ore spot is inexhaustible. The factory keeps running after the objective is
complete. Progress is session-only; stop and restart Play Mode to reset it.

## Scene and implementation

The original FPSCore player, ground, open spline, and three test cubes are reused.
The open spline's existing curved route is leveled and its outlet moved away from
its inlet to fit the bin. The spare loop and loose test cubes are preserved in
`Conveyor Experiments (inactive)`. Authored stations and HUD live under `First Factory`.

Factory scripts are in `Assets/scripts/Factory`. Inspector references wire the
ore spot, player, smelter, ingot prefab, conveyor, and bin; no runtime scene builder
or downloaded assets are needed. `Assets/Editor/FactoryPrototypeSetup.cs` is the
one-time authoring tool, not a startup dependency. Once configured, edit the saved
scene objects directly rather than rebuilding them.

The conveyor accepts explicit machine output, keeps a minimum gap when connected
to a receiver, and waits if that receiver rejects an item. The smelter retains
finished output while its inlet is unavailable. The bin only accepts marked iron
ingots and counts each at most once. Test cubes and raw ore cannot complete the goal.

## Verification

In Unity's Test Runner, run the PlayMode tests in `FirstFactoryTests`. They load
the saved MainScene and cover the complete 10-ingot loop, a stopped belt, a blocked
bin and item spacing, rejection of raw/test/duplicate cargo, and a full machine.

This prototype intentionally has one resource, one recipe, one machine, and one
submission objective. Building placement, power, splitting/merging, and saving
are outside this week's loop.
