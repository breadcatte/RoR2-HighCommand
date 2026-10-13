# The commander's camera (rewritten 2026-10-13)

The user's complaint: the camera "catches on everything, jumps, sinks, stops". Asked for: study how strategy games do
it and rebuild ours on that model, with as much freedom as possible and nothing moving by itself.

## How strategy cameras work (what was studied)

**0 A.D.** (open source, `source/graphics/CameraController.cpp`, `binaries/data/config/default.cfg`)
* The camera orbits a pivot on the ground: position = pivot - forward * zoom. Pitch limited 28..60 deg (default 35).
* Pivot height = the terrain **filtered over a radius proportional to the zoom** (`GetFilteredGroundLevel(x, z, radius)`,
  radius = horizontal view length * zoom * `view.height.smoothness` 0.5). The filter is a trilinear read of a mip
  pyramid of the heightmap where every level is the 2x2 **average** of the one below (not the max). Far out the camera
  only feels the shape of the land; close in, the ground under it.
* The camera keeps `view.height.min` = 16 m above the filtered ground at its near point; height changes are smoothed.
* Every quantity has its own exponential smoothing: position 0.1 s, zoom 0.4, pitch 0.5, yaw 0.3, height 0.5.
* Pivot clamped to the map with a margin; edge scrolling 3 px; scroll speed fixed (Spring: proportional to height).

**Spring / Recoil RTS** (`rts/Game/Camera/OverheadController.cpp` + forum threads)
* "TA" overhead camera: pivot y = `CGround::GetHeightAboveWater`, height above it clamped, pan speed proportional to
  the camera height (`pixelSize` from height and fov), zoom proportional to height.
* Known problem on the forums: the camera bounces over cliffs and water. Proposed fixes: follow a **separate smoothed
  height** (the "smooth mesh" aircraft use, or a blurred copy of the heightmap built at load) instead of the raw ground;
  a speed limit on height changes was rejected (the camera then clips into terrain it cannot climb fast enough).

**Warcraft III / StarCraft II**
* The camera target height follows a smoothed terrain (cliffs give gentle slopes, not steps); doodads (trees, rocks,
  buildings) never move the camera. Mappers fight the opposite problem: the camera ignores a doodad you stand on.
* Fixed pitch and a short zoom range; "follow unit" lock; edge scrolling works over the HUD at the screen edge.

**Company of Heroes / Total War / Age of Empires IV**
* Zoom towards the cursor; the view lowers towards the ground when zoomed in (zoom tilt); a reset key for the view.
* Total War: the camera keeps a minimum height above the ground under it (pushed up, never into a hill); "debug"
  cameras without that clip into the ground. Objects in the way are faded instead of moving the camera.

**Unity RTS controllers** (Stratecam, Enemy Masses RTS camera, tutorials)
* Orbit around a ground pivot; ground read from a dedicated ground layer only; frame-rate independent smoothing
  (SmoothDamp); pan speed scaled by zoom; collision probe radius around the camera.

Common rules taken from all of them:
1. The camera never rides the real surface: a smoothed copy of the terrain, without objects, gives the pivot height.
2. The amount of smoothing grows with the zoom.
3. Smoothing per quantity, frame-rate independent; no speed limit on height (it causes clipping).
4. The camera itself stays above the ground by rising, not by zooming in; objects in the way are hidden instead.
5. Panning is in the ground plane, never blocked by anything but the map edge; speed proportional to the zoom.
6. Edge scrolling at the last pixels of the screen, also over the HUD.

## Free camera (the default since the user's verdict, 2026-10-13)

After trying the terrain-following camera the user asked for "as much freedom as possible, the less the camera does by
itself the better" (multi-level, small maps make any following visible). So `Camera/FollowGround` is off by default:
* The focus height is a plain number (`level`) changed only by the player: `FocusOn` (heroes, alert, roster), follow,
  minimap (`PanTo` -> ground there), the wheel (3D zoom to the point P under the cursor or under the screen centre:
  lookAt' = lookAt + (P - lookAt)(1 - d'/d), which keeps P exactly under the cursor), Home (ground under the view).
* `StepUp`: if the look-at point AND the camera are inside big ground whose top is walkable (a plateau higher than the
  camera), the level climbs onto that top at 40 m/s. Never down by itself.
* No tilt by itself (`GroundLift` only in FollowGround mode).
* Rejected: following the averaged walk field (lifted the camera 40 m above heroes standing under another floor in
  Sundered Grove); climbing onto any big ground (climbed 80 m onto a giant root); the "lowest level as the plane" idea
  (heroes on upper floors would be above the camera).
* Check: `qa-run.ps1 camfree` (sweeps "focus height a..a", "tilt speed max 0"), screenshots `camfree_*`.

## How ours works now (FollowGround on)

`src\Client\CameraGround.cs` - the camera's own ground, built at stage start (`MapInfo.Rebuild`):
* **Height field**: the stock ground node graph (nodes + points every 2 m along links not steeper than 45 deg) splatted
  into 4 m cells; empty cells (chasms, void, the 100 m margin) filled from around (push-pull pyramid); six blur levels
  (3 box passes, sigma 2 / 5.7 / 9.8 / 18 / 34 / 66 m). `Height(xz, sigma)` reads it trilinearly (log-sigma between
  levels) like 0 A.D. Typical build: a few ms. No trees, rocks, roofs, props, steps: they are not walkable nodes.
* **Free height**: for the camera position, columns of 2 m read lazily with vertical rays against **big** level pieces
  only (`LevelPieces.IsBig`: colliders > 60 m, the terrain and cliffs): tops (facing up, rays from above) and undersides
  (facing down, rays from below). Inside = the first surface above is a top. `FreeHeight(p)` = the lowest y >= p.y out
  of the ground with 1 m clearance. Caves and bridges work (the first surface above is an underside there).

`src\Client\RtsCamera.cs`:
* Wanted state from input: focus XZ, yaw, pitch, distance. Shown state: SmoothDamp of each (`Camera/Smoothing`).
* Focus height = `CameraGround.Height(shownXZ, clamp(0.3 * distance, 4, 64))` + the anchor, smoothed (~0.24 s).
* **Anchor**: a real point the player flew to (FocusOn a hero / alert, the followed army, ground zoomed at that a hero
  could stand on) stores `offset = its y - field at sigma 4`, applied around it with a bell of 24 m, forgotten beyond
  72 m. On open ground the offset is ~0 (nothing changes); on a bridge or a cave floor it is the difference of levels,
  so the camera looks at that level there. The minimap and `hc_cam at` only move the place (`PanTo`).
* **Lift**: `GroundLift` solves directly for the tilt that puts the camera at the free height (5 probes: centre and
  +-0.6 m), smoothed up in 0.08 s, down in 0.6 s. Only big ground counts.
* Small pieces between the camera and a hero are hidden (`CollectHeroCovers`, unchanged); big ground -> silhouette.
* Edge scrolling works over the panels at the edge; not while turning with the middle button or dragging the minimap.
  A middle-button turn begun in the world goes on over the panels until the button is released.
* Pan margin: 40 m past the walkable box (which has 24 m of its own).

Removed: the per-frame ray scan of all surfaces under the focus (caught trees, rocks, roofs), the "chasm" hold and the
"settle" drop after 1.5 s standing still (the camera sank by itself), the stepwise tilt search.

## Checks
* `qa-run.ps1 camheight -TimeoutMin 30` - `hc_cam audit` on 11 stages: slope of the focus height while panning (m/m),
  places far above/below the walkable ground, lowest views still in the ground.
* `qa-run.ps1 camvillage` - sweeps on Shattered Abodes (roofs, canopies), log lines `RtsCamera sweep`.
* `hc_cam info` prints the anchor too.
