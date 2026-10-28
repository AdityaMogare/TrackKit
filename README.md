# TrackKit

Build race tracks from modular road pieces in Unity, on top of [Prefab Builder](https://github.com/ProfessorEasley/Unity_Trackmaker).

Each road piece carries its own gameplay: entry/exit points, walls, checkpoint (gate) markers and, on the start piece, the start/finish gate and start grid. Pieces snap end to end, a checker finds gaps before you play, and **Bake** turns the pieces into the game's race objects (waypoints, spawns, race-manager wiring) through a small game-specific **adapter**.

```
road prefabs ──1. Build Kit──▶ kit pieces ──3. Place Next──▶ track ──4. Check Joints──▶ 6. Bake ──▶ race
                                 (sockets, walls,             (PREFAB_LAYOUT,          (adapter creates the
                                  gate + spawn markers)        Prefab Builder SAVE)     game's waypoints/spawns)
```

## Requirements

- Unity 2022.3 or newer
- **Prefab Builder** installed (TrackKit uses its `PlacedPrefabIdentity` and catalog so layouts SAVE/LOAD):
  `"com.prefabbuilder.tool": "https://github.com/ProfessorEasley/Unity_Trackmaker.git"`
- Road prefabs with a **solid (non-trigger) collider** (TrackKit does not ship road art)
- A floor layer for drivable surfaces and a wall tag (defaults: layer 8, tag `wall`)
- An **adapter** for your game if you want to Bake (see below). Building and checking work without one.

## Install

**Package Manager → + → Add package from git URL**, then paste:

```
https://github.com/<your-username>/TrackKit.git
```

or add to `Packages/manifest.json`:

```json
"com.adityamogare.trackkit": "https://github.com/<your-username>/TrackKit.git"
```

Pin a version with a tag: `...TrackKit.git#v0.1.0`.

## Steps

All menus are under **Tools ▸ TrackKit** (the **3. Track Builder Window** has every step as a button).

| Step | What it does | Output |
|---|---|---|
| **Settings** | One asset per project: road prefabs to use, floor layer, wall tag/size, start-grid layout, folders | `Assets/TrackKit/TrackKitSettings.asset` |
| **1. Build Kit** | Makes a kit piece from each listed road prefab: measures the two road ends (sockets), adds walls along the edges, gate markers along the centre, and the start gate + spawn markers on the start piece. Adds the pieces to the Prefab Builder catalog | `Assets/TrackKit/Prefabs/*.prefab`, `TrackKit_BuildKit_Report.txt` |
| **2. Validate Kit** | Checks every piece: solid floor collider, walls on both sides, sockets on the road, gate markers, start grid, Prefab Builder id | `TrackKit_ValidateKit_Report.txt` |
| **3. Track Builder Window → Place Next** | Select a placed piece (or nothing, to start) and click a kit piece: it attaches socket to socket. **Flip** uses a piece the other way round (bend turns the other way, incline goes the other way) | Pieces under `PREFAB_LAYOUT` |
| **4. Check Joints** | Walks the chain from the start piece; reports open, bent or stepped joints, gaps (down-rays on centre and shoulders), overlapping pieces, walls on the road, old floor under the track | `TrackKit_CheckJoints_Report.txt` |
| **6. Bake Race** | Re-runs Check Joints, orders gate and spawn markers in driving order, then calls the adapter to create the game's race objects and validate them. Saves the scene only if everything passes | Game objects + `TrackKit_Bake_Report.txt` |

Prefab Builder **SAVE LAYOUT / LOAD LAYOUT** work on the result unchanged: pieces are direct children of `PREFAB_LAYOUT` with `PlacedPrefabIdentity`. Driving order and direction are worked out from how pieces touch, so a re-loaded layout bakes the same way.

### How pieces join
Every piece has an `entry` and an `exit` transform at the centre of each road end, +Z pointing out of the piece. Place Next turns the new piece so its entry faces the previous exit, then moves it so the two points coincide. Joints are accepted within `joinTolerance` (0.35 m), 2° and 0.1 m height.

### How the start is found
Exactly one piece (the start piece) has a gate marker with **isStartGate**. The chain starts there, runs the way that gate faces, and that marker becomes the first gate.

## Writing an adapter

TrackKit knows nothing about a game's waypoint or race scripts. Add one class to an **Editor** folder of your project:

```csharp
using System.Collections.Generic;
using TrackKit;
using TrackKit.EditorTools;

public sealed class MyGameTrackKitAdapter : ITrackKitAdapter
{
    public string DisplayName => "My Game";

    public void ConfigureDefaults(TrackKitSettings s)
    {
        // Fill road prefabs, layers, bakeSceneFolder... for a new settings asset.
    }

    public void Bake(TrackBakeContext ctx, TrackReport report)
    {
        // ctx.gates  : markers in driving order, start first (position, rotation, HalfWidth, marker values)
        // ctx.spawns : start-grid markers in slot order
        // ctx.chain  : pieces in driving order (piece + reversed)
        // Create your checkpoints / waypoints / spawns here, link them, validate, report.Error(...) on problems.
    }

    public IEnumerable<TrackKitAction> Actions { get { yield break; } } // extra window buttons
}
```

TrackKit finds it automatically. `TrackChain` (runtime, no editor code) has the ordering and ground-sampling helpers if you want the same logic in a player build.

## Package layout

```
Runtime/   TrackKit.Runtime   TrackPiece, TrackGateMarker, TrackSpawnMarker, TrackChain, TrackReport
Editor/    TrackKit.Editor    Settings, Build Kit, Validate Kit, Track Builder window, Check Joints, Bake, adapter interface
```

The runtime components are data only (no Update) and are safe to ship in a build.

## Limitations (0.1.0)

- Pieces have exactly two ends (no junctions or branches); joints check position, not road width.
- Socket measuring expects straights/inclines to run along one axis and bends to have two equal-width ends.
- Rebuilding the kit regenerates walls and markers; hand edits to kit prefabs are overwritten.
- Per-gate tuning done in a scene is not stored by Prefab Builder layouts.
