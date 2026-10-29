# First Factory prototype

Open `Assets/Scenes/MainScene.unity` in Unity **6000.5.10f1** and press Play.
MainScene is also the first enabled scene in Build Settings.

MainScene starts in the construction sandbox. The original guided demo is
preserved in the disabled **First Factory** group. Enable that group in the
Hierarchy before Play if you want to use the original production loop below.

## Original production loop

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

The original FPSCore player, open spline, and three test cubes are reused.
The open spline's existing curved route is leveled and its outlet moved away from
its inlet to fit the bin. The spare loop and loose test cubes are preserved in
`Conveyor Experiments (inactive)`. Authored stations and HUD live under `First Factory`.

The old `Land` plane is preserved but disabled. A seeded native Unity Terrain now
supplies the ground, with the existing factory kept on a flat clearing at Y=0.

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

`BuildModeTests` checks the construction sandbox. `TerrainMapTests` checks the
saved terrain collider, protected factory clearing, finite boundaries, elevated
smelter snapping, and runtime-copy isolation. EditMode `MapGenerationTests` checks
seed reproducibility, valid heights, the flat clearing, settings validation, and
isolation from gameplay's random sequence.

## Construction sandbox

Press **B** to toggle build mode. **1-4** or the mouse wheel selects conveyors,
smelters, bins, or ore spots. **Left click** places; belts use a start and end
point. **R** rotates buildings, **T** cycles belt width, and **right click**
cancels a belt start. Conveyor previews snap to free machine and belt endpoints.
Buildings remain upright and cannot be interactively placed on surfaces steeper
than 20 degrees. Conveyor endpoints still support snapping to machine sides.

## Terrain foundation

- One **256 x 256 metre** map, centered at world X/Z=0, with a **257 x 257** heightmap.
- Default seed: **104729**. Generator version: **1**.
- A **112 x 96 metre** factory clearing, centered at X=-8, Z=0, stays at world
  **Y=0**. A 24-metre smooth transition leads into gentle hills up to 14 metres high.
- Native Terrain/TerrainCollider share the same saved TerrainData. Grass and rock
  layers use small generated tile textures; steeper slopes blend toward rock.
- Four colliders stop the player walking off the finite map. They use Unity's
  Ignore Raycast layer so they do not become building placement surfaces.
- The factory, resource spot, player spawn, and conveyor route remain in place.
  Map generation does not spawn extra resources or move buildings.

To change the map:

1. Edit `Assets/World/Map Settings.asset` (seed, map dimensions, clearing, hills).
2. Select **Generated Factory Map** in MainScene.
3. Click **Regenerate Terrain (Undo Supported)** in its inspector, then save
   the scene/project. Regeneration replaces heightmap sculpting and surface paint;
   it preserves station objects. Keep the clearing over any buildings you want
   to protect. Changing its footprint does not relocate them automatically.

The scene loads the baked map in both Editor and player builds. **Generate On
Awake** is off. If explicitly enabled, or if **Regenerate Runtime Copy** is used
in Play Mode, a private TerrainData is generated without modifying saved assets.
Only regenerate before building: terrain changes do not reposition existing cargo
or structures. A seed is reproducible with the same settings and generator version.

`Tools > Factory Map > Set Up Main Scene` installs or refreshes the saved map
assets and scene wiring. It reuses the existing settings and terrain asset.
Runtime generation code is in `Assets/scripts/World`; editor authoring is in
`Assets/Editor/TerrainMapEditor.cs`.

This remains a small prototype: one resource type, one recipe, and one submission
objective, with free construction. Infinite chunks, biomes, trees, water, resource
distribution, foundations, power, splitters/mergers, and factory saving are future work.
