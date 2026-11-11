# First Factory prototype

Open `Assets/Scenes/MainScene.unity` in Unity **6000.5.10f1** and press Play.
MainScene is also the first enabled scene in Build Settings.

MainScene starts in the construction sandbox. The original guided demo is
preserved in the disabled **First Factory** group. Enable that group in the
Hierarchy before Play if you want to use the original production loop below.

## Crafting table and shared inventory

The **Starter Crafting Table** is placed near spawn at `(-10, 0, -1)`.
Look at its worktop within interaction range and press **E** to open crafting.
Select **Iron Ingot** or **Iron Plate**, then click **CRAFT ONE**. Close the panel
with **Close** or **Esc** to resume movement. Crafting requires a nearby active table.

- Handcraft 1 iron ingot from 2 iron ore.
- Handcraft 1 iron plate from 2 iron ingots.
- Inputs are consumed from the inventory and outputs are added to it immediately.
- Missing ingredients or insufficient output space leave all quantities unchanged.
- Press **I** to view inventory. Gathering, pickups, dropping, machines, bin
  withdrawals, building costs and crafting share `Inventory` as their sole store.
  The prototype total limit remains 20 items and is editable as **Item Capacity**
  on the Inventory component. Resource stack limits and slot space also apply.

`Assets/prefabs/CraftingTabUI.prefab` contains the saved controls and recipe
entries. Runtime code only binds and updates these controls. Edit the layout in
Unity and edit recipes under `Assets/Recipes/`. Each recipe supports multiple
ingredients and an output quantity. `Assets/prefabs/Crafting Table.prefab` is the
reusable workbench. Initial setup is under **Tools > Factory Crafting** and is
only an editor authoring tool. The saved MainScene is already wired.

## Original production loop

1. Walk to the brown **Iron Ore Spot** and look at it. Hold **E** to gather ore
   (one every 0.4 seconds; total capacity is configured on the Inventory component).
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

`BuildModeTests` checks the construction sandbox and the catalog's selection/build
callback and input-mode flow. `TerrainMapTests` checks the
saved terrain collider, protected factory clearing, finite boundaries, elevated
smelter snapping, and runtime-copy isolation. EditMode `MapGenerationTests` checks
seed reproducibility, valid heights, the flat clearing, settings validation, and
isolation from gameplay's random sequence. `ResourcePlanningTests` checks deposit
spacing, safe ground, reproducibility, and bounded placement attempts.
`GeneratedResourceTests` checks gathering usable ore and replacing generated
deposits without deleting player-placed objects or modifying saved settings.

## Construction sandbox

Press **B** to toggle build mode. **1-4** or the mouse wheel selects conveyors,
smelters, bins, or ore spots. **Left click** places; belts use a start and end
point. **R** rotates buildings, **T** cycles belt width, and **right click**
cancels a belt start. Conveyor previews snap to free machine and belt endpoints.
Buildings remain upright and cannot be interactively placed on surfaces steeper
than 20 degrees. Conveyor endpoints still support snapping to machine sides.

## Terrain foundation

- One **1024 x 1024 metre** map (1 square kilometre), centered at world X/Z=0,
  with a **513 x 513** heightmap (approximately 2 metres between samples).
- Default seed: **104729**. Generator version: **2**. Version 2 changes the
  landscape for existing seeds because it adds mountain regions.
- A **112 x 96 metre** factory clearing, centered at X=-8, Z=0, stays at world
  **Y=0**. A 24-metre smooth transition leads into rolling hills up to 20 metres
  high and seven seeded mountain regions with up to 130 metres of added relief.
  Regions overlap using a maximum, so their heights do not add without limit.
- Native Terrain/TerrainCollider share the same saved TerrainData. Grass and rock
  layers use small generated tile textures; steep slopes and high elevations
  blend toward rock.
- Four colliders stop the player walking off the finite map. They use Unity's
  Ignore Raycast layer so they do not become building placement surfaces.
- The original factory, player spawn, and conveyor route remain in place.

## Generated resource deposits

The map requests **48 mixed deposits**, including a guaranteed starter iron deposit
at X=-42, Z=12. Look at a rock and hold **E** to gather its ore. Iron has orange
mineral pieces, copper has green ones, and tin has taller silver-blue pieces.
Each ore has its own inventory entry and loose-item prefab. A drill placed near a
deposit can collect that ore or feed it onto a conveyor. Existing smelting and
crafting recipes still use the iron production chain.

Other deposits stay outside the protected clearing/transition, at least 65 metres
apart and 24 metres inside the map edge. Placement checks slope (up to 16 degrees),
elevation (up to 45 metres), and the rock footprint. A coarse eight-metre walking
grid excludes regions cut off from the clearing by steep terrain; this is a
terrain-accessibility check, not a NavMesh or a generated road system.

Placement attempts are bounded. If custom settings cannot fit the requested
count, the generator reports the actual count instead of stacking deposits or
looping indefinitely. Change the settings to adjust density. The deposit weights
on **Generated Factory Map** control the mix outside the clearing. The default
weights are three parts iron, two parts copper, and one part tin. Ore assignments
repeat with the seed and are independent of the random sequence used for locations.

To change the map:

1. Edit `Assets/World/Map Settings.asset` (seed, dimensions, clearing, hills,
   mountains, resource density and suitability limits).
2. Select **Generated Factory Map** in MainScene.
3. Click **Regenerate Terrain (Undo Supported)** in its inspector, then save
   the scene/project. Regeneration replaces heightmap sculpting and surface paint;
   it replaces only the **Generated Resource Spots** group and preserves other
   station objects. Keep the clearing over any buildings you want
   to protect. Changing its footprint does not relocate them automatically.

For a different random landscape, click **New Random Seed and Regenerate** in
the map inspector. It records a new seed and rebuilds both terrain and deposits.
The inspector displays the actual generated seed and deposit count.

The scene loads the baked map and deposits in both Editor and player builds. **Generate On
Awake** is off. If explicitly enabled, or if **Regenerate Runtime Copy** is used
in Play Mode, a private TerrainData is generated without modifying saved assets.
The random-seed button also works in Play Mode, using a temporary settings copy.
Only regenerate before building: terrain changes do not reposition existing cargo
or structures. A seed is reproducible with the same settings and generator version.

`Tools > Factory Map > Set Up Main Scene` installs or refreshes the saved map
assets and scene wiring. It reuses the existing settings and terrain asset.
The **Upgrade to Large Landscape** menu applies this version's recommended
dimensions and mountain/resource defaults; normal regeneration preserves custom settings.
Runtime generation code is in `Assets/scripts/World`; editor authoring is in
`Assets/Editor/TerrainMapEditor.cs`.

This remains a small prototype: one resource type, one recipe, and one submission
objective, with free construction. Infinite chunks, biomes, trees, water, additional
ore types, roads, foundations, power, splitters/mergers, and factory saving are future work.
