using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// The commander's camera. It plugs into the stock CameraRigController as an override (ICameraStateProvider), so
    /// post-processing, fog, the UI camera and screen shake all keep working; only the camera pose is ours.
    ///
    /// The camera orbits a "focus" point on the ground: WASD / screen edge pan it, the wheel zooms, middle mouse
    /// (or Q/E) rotates. The focus follows the terrain height and stays inside the map; the camera itself is pulled
    /// in front of walls and hills.
    /// </summary>
    internal sealed class RtsCamera : MonoBehaviour, ICameraStateProvider
    {
        internal static RtsCamera Instance { get; private set; }

        internal CameraRigController Rig { get; private set; }

        internal Camera SceneCam => Rig ? Rig.sceneCam : null;

        /// <summary>Where the camera looks at on the ground (smoothed).</summary>
        internal Vector3 Focus => smoothFocus;

        internal float Yaw => smoothYaw;

        /// <summary>The tilt in degrees (90 = straight down), as shown now (with the extra tilt that looks past an obstacle).</summary>
        internal float Pitch => Mathf.Min(SteepestPitch, (pitchReady ? smoothPitch : Settings.CameraPitch.Value) + liftPitch);

        internal float Distance => smoothDistance;

        // wanted state
        private Vector3 focus;
        private float yaw = 25f;
        private float distance = 48f;
        private float pitch;
        private bool pitchReady;
        private float configPitch;

        private const float MinPitch = 25f, MaxPitch = 85f;

        // smoothed state
        private Vector3 smoothFocus;
        private float smoothYaw;
        private float smoothDistance;
        private float smoothPitch;
        private Vector3 focusVelocity;
        private float yawVelocity;
        private float distanceVelocity;
        private float pitchVelocity;

        // height of the focus (see FocusHeight)
        private const float AbyssDrop = 12f;        // ground this far below the level the camera is on counts as a chasm
        private const float AbyssDwell = 5f;        // seconds of panning over a chasm before the camera follows it down
        private const float StopDwell = 1.5f;       // seconds standing still over it before it does
        private const float MaxHeightSpeed = 45f;   // m/s, the fastest the focus may rise or sink
        private float heightVelocity;
        private float abyssTime;
        private bool overAbyss;

        // how much of the arm to the camera is left after pulling it in front of an obstacle (1 = free)
        private float armFactor = 1f;
        private float armFactorVelocity;

        private bool initialised;
        private Vector2 lastMouse;
        private GameObject listenerProxy;

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (listenerProxy) Destroy(listenerProxy);
            ReleaseRigs();
        }

        // The stock rigs keep a reference to their override camera: give them back to the game before this object goes
        // away (a destroyed provider would otherwise be called on the next frame).
        private void ReleaseRigs()
        {
            try
            {
                var rigs = CameraRigController.readOnlyInstancesList;
                for (int i = 0; i < rigs.Count; i++)
                {
                    CameraRigController rig = rigs[i];
                    if (rig && rig.IsOverrideCam(this)) rig.SetOverrideCam(null, 0f);
                    if (rig)
                    {
                        var spectate = rig.GetComponent<CameraRigControllerSpectateControls>();
                        if (spectate) spectate.enabled = true; // the game's own controls come back with the game's camera
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("RtsCamera: could not release the camera rig: " + e.Message);
            }
            Rig = null;
            silencedRig = null;
        }

        // ---- public commands ---------------------------------------------------------------------

        /// <summary>Moves the camera to look at a world point (smoothly, unless <paramref name="snap"/>).</summary>
        /// <param name="deliberate">
        /// False for a point that only slides past (dragging over the minimap): a chasm under it is then not followed
        /// down, just like when panning. A flown-to hero or alert is deliberate: its own height is the level.
        /// </param>
        internal void FocusOn(Vector3 worldPoint, bool snap = false, bool deliberate = true)
        {
            focus = ClampToMap(new Vector3(worldPoint.x, worldPoint.y, worldPoint.z), deliberate);
            if (snap || !initialised)
            {
                smoothFocus = focus;
                focusVelocity = Vector3.zero;
                heightVelocity = 0f;
                initialised = true;
            }
        }

        /// <summary>While set, the focus follows what it returns (null result = keep position). Cleared by manual panning.</summary>
        internal Func<Vector3?> FollowTarget;

        internal bool IsFollowing => FollowTarget != null;

        internal void SetDistance(float meters)
        {
            distance = Mathf.Clamp(meters, Settings.CameraMinDistance.Value, Settings.CameraMaxDistance.Value);
        }

        // ---- testing (hc_cam sweep) --------------------------------------------------------------

        private bool sweeping;
        private Vector3 sweepFrom, sweepTo;
        private float sweepTime, sweepSeconds, sweepLogAt;
        private float lowestSurface;
        private int surfaces;

        /// <summary>Moves the focus along a line through the same height logic the keyboard pan uses, logging as it goes.</summary>
        internal void Sweep(Vector3 from, Vector3 to, float seconds)
        {
            sweepFrom = from;
            sweepTo = to;
            sweepTime = 0f;
            sweepLogAt = 0f;
            sweepSeconds = Mathf.Max(0.5f, seconds);
            sweeping = true;
            FollowTarget = null;
            FocusOn(from, snap: true);
        }

        private void AdvanceSweep(float dt)
        {
            sweepTime += dt;
            float k = Mathf.Clamp01(sweepTime / sweepSeconds);
            Vector3 at = Vector3.Lerp(sweepFrom, sweepTo, k);
            var probe = new Vector3(at.x, focus.y, at.z);
            float legacy = GroundHeight(probe, focus.y); // what the camera used to do here
            focus = ClampToMap(probe, false);
            abyssTime = overAbyss ? abyssTime + dt : 0f;

            if (sweepTime >= sweepLogAt)
            {
                sweepLogAt += 0.4f;
                Plugin.Log.LogInfo($"RtsCamera sweep t={sweepTime:0.0} xz=({at.x:0},{at.z:0}) wantedY={focus.y:0.0} shownY={smoothFocus.y:0.0} " +
                                   $"surfaces={surfaces} lowestSurfaceY={(surfaces > 0 ? lowestSurface : 0f):0.0} legacyY={legacy:0.0} abyss={overAbyss} dwell={abyssTime:0.0} arm={armFactor:0.00} lift={liftPitch:0}");
            }
            if (k >= 1f) sweeping = false;
        }

        // ---- frame loop --------------------------------------------------------------------------

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            Vector2 mouse = ClientInput.Mouse;
            Vector2 mouseDelta = mouse - lastMouse;
            lastMouse = mouse;

            if (!initialised) return;

            // --- pan ---
            Vector2 move = Vector2.zero;
            if (ClientInput.Key(Settings.KeyUp.Value)) move.y += 1f;
            if (ClientInput.Key(Settings.KeyDown.Value)) move.y -= 1f;
            if (ClientInput.Key(Settings.KeyRight.Value)) move.x += 1f;
            if (ClientInput.Key(Settings.KeyLeft.Value)) move.x -= 1f;

            // (not while the middle button turns / tilts the view: the hand may reach the screen edge then)
            if (Settings.EdgeScroll.Value && ClientInput.PointerInWindow && !ClientInput.PointerOverUi && !ClientInput.MouseBlocked
                && !ClientInput.MouseHeld(2))
            {
                int e = Settings.EdgeScrollPixels.Value;
                if (mouse.x <= e) move.x -= 1f;
                else if (mouse.x >= Screen.width - 1 - e) move.x += 1f;
                if (mouse.y <= e) move.y -= 1f;
                else if (mouse.y >= Screen.height - 1 - e) move.y += 1f;
            }

            if (sweeping) AdvanceSweep(dt);

            // following something (selected heroes): the focus tracks it until the player pans by hand
            if (FollowTarget != null)
            {
                if (move.sqrMagnitude > 0.0001f) FollowTarget = null;
                else
                {
                    Vector3? followed = FollowTarget();
                    if (followed.HasValue) focus = ClampToMap(followed.Value, true);
                }
            }

            if (move.sqrMagnitude > 0.0001f)
            {
                if (move.sqrMagnitude > 1f) move.Normalize();
                float speed = 38f * Settings.CameraPanSpeed.Value * Mathf.Max(0.35f, distance / 45f);
                if (ClientInput.Key(Settings.KeyFastCamera.Value)) speed *= 2.2f;
                Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
                Vector3 delta = (flat * Vector3.right * move.x + flat * Vector3.forward * move.y) * (speed * dt);
                focus = ClampToMap(focus + delta, false);

                // a chasm is crossed at the level of its edge; only a long stay over it takes the camera down
                abyssTime = overAbyss ? abyssTime + dt : 0f;
            }
            else if (!sweeping)
            {
                abyssTime = 0f;
                if (FollowTarget == null) Settle(dt);
            }

            // --- zoom ---
            float scroll = ClientInput.PointerOverUi || ClientInput.KeyboardBlocked ? 0f : ClientInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float factor = Mathf.Pow(0.88f, scroll * Settings.CameraZoomSpeed.Value);
                distance = Mathf.Clamp(distance * factor, Settings.CameraMinDistance.Value, Settings.CameraMaxDistance.Value);
            }

            // --- rotate ---
            float rotate = Settings.CameraRotateSpeed.Value;
            float tilt = Settings.CameraTiltSpeed.Value;
            bool dragging = ClientInput.MouseHeld(2) && !ClientInput.PointerOverUi && !PauseManager.isPaused;
            if (dragging)
            {
                yaw += mouseDelta.x * 0.22f * rotate;
                // the mouse up looks more level, down looks more from above (InvertTilt swaps them)
                float sign = Settings.InvertTilt.Value ? 1f : -1f;
                pitch += mouseDelta.y * 0.2f * tilt * sign;
            }
            if (ClientInput.Key(Settings.KeyRotateLeft.Value)) yaw -= 90f * rotate * dt;
            if (ClientInput.Key(Settings.KeyRotateRight.Value)) yaw += 90f * rotate * dt;
            if (ClientInput.Key(Settings.KeyTiltUp.Value)) pitch -= 45f * tilt * dt;
            if (ClientInput.Key(Settings.KeyTiltDown.Value)) pitch += 45f * tilt * dt;

            // the start tilt of the options is applied at the start and whenever it is changed there
            float configNow = Settings.CameraPitch.Value;
            if (!pitchReady || !Mathf.Approximately(configNow, configPitch))
            {
                configPitch = configNow;
                pitch = configNow;
                if (!pitchReady) smoothPitch = configNow;
                pitchReady = true;
            }
            pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);

            // --- smoothing ---
            float smooth = Settings.CameraSmoothing.Value;
            if (smooth <= 0.001f)
            {
                smoothFocus = focus;
                smoothYaw = yaw;
                smoothDistance = distance;
                smoothPitch = pitch;
            }
            else
            {
                smoothYaw = Mathf.SmoothDampAngle(smoothYaw, yaw, ref yawVelocity, smooth, float.PositiveInfinity, dt);
                smoothDistance = Mathf.SmoothDamp(smoothDistance, distance, ref distanceVelocity, smooth * 0.8f, float.PositiveInfinity, dt);
                smoothPitch = Mathf.SmoothDamp(smoothPitch, pitch, ref pitchVelocity, smooth, float.PositiveInfinity, dt);
            }

            // Sideways the focus follows the hand closely, but its height moves slowly and at a limited speed: a step,
            // a ramp edge or a ledge must not jerk the whole picture up or down.
            float groundSmooth = Mathf.Max(0.3f, smooth * 3f);
            Vector3 flatNow = new Vector3(smoothFocus.x, 0f, smoothFocus.z);
            Vector3 flatWant = new Vector3(focus.x, 0f, focus.z);
            Vector3 flatVelocity = new Vector3(focusVelocity.x, 0f, focusVelocity.z);
            if (smooth <= 0.001f) flatNow = flatWant;
            else flatNow = Vector3.SmoothDamp(flatNow, flatWant, ref flatVelocity, smooth, float.PositiveInfinity, dt);
            focusVelocity = flatVelocity;
            float height = Mathf.SmoothDamp(smoothFocus.y, focus.y, ref heightVelocity, groundSmooth, MaxHeightSpeed, dt);
            smoothFocus = new Vector3(flatNow.x, height, flatNow.z);
        }

        private void LateUpdate()
        {
            // Take over whichever stock camera rigs exist (the rig is recreated every stage).
            var rigs = CameraRigController.readOnlyInstancesList;
            for (int i = 0; i < rigs.Count; i++)
            {
                CameraRigController rig = rigs[i];
                if (!rig || !rig.sceneCam || rig.localUserViewer == null) continue;
                if (!rig.IsOverrideCam(this)) rig.SetOverrideCam(this, 0f);
                if (rig != silencedRig)
                {
                    SilenceSpectateControls(rig);
                    silencedRig = rig;
                }
                if (Rig != rig)
                {
                    Rig = rig;
                    OnRigChanged();
                }
            }
            if (Rig && !Rig.isActiveAndEnabled) Rig = null;

            UpdateListener();
        }

        // The game's spectator controls listen to the player's buttons while there is no body: one of them opens the
        // "remote operation" drone picker (a big dark panel in the middle of the screen) on a key press the commander
        // never meant. The commander is not a spectator, so the component is switched off while the mode runs.
        private CameraRigController silencedRig;

        private void SilenceSpectateControls(CameraRigController rig)
        {
            var spectate = rig.GetComponent<CameraRigControllerSpectateControls>();
            if (!spectate || !spectate.enabled) return;
            spectate.enabled = false;
            var panels = FindObjectsOfType<RoR2.UI.RemoteOperationDronePickerPanel>();
            for (int i = 0; i < panels.Length; i++)
                if (panels[i]) Destroy(panels[i].gameObject);
        }

        private void OnRigChanged()
        {
            Plugin.Log.LogInfo("RtsCamera: took over camera rig.");
            if (!Settings.ListenerAtFocus.Value || !Rig || !Rig.sceneCam) return;
            try
            {
                // The audio listener normally sits on the camera. A camera 40 m up would make every fight sound
                // distant, so a proxy object on the ground below the focus point becomes the listener instead.
                var existing = Rig.sceneCam.GetComponent<AkAudioListener>();
                if (existing) Destroy(existing);
                if (!listenerProxy)
                {
                    listenerProxy = new GameObject("HighCommand.AudioListener");
                    DontDestroyOnLoad(listenerProxy);
                    listenerProxy.AddComponent<AkGameObj>();
                    listenerProxy.AddComponent<AkAudioListener>();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("RtsCamera: audio listener proxy not created: " + e.Message);
            }
        }

        private void UpdateListener()
        {
            if (!listenerProxy) return;
            listenerProxy.transform.position = smoothFocus + Vector3.up * 3f;
        }

        // ---- the stock camera interface ----------------------------------------------------------

        public void GetCameraState(CameraRigController cameraRigController, ref CameraState cameraState)
        {
            if (!initialised) return;

            Vector3 lookAt = smoothFocus + Vector3.up * 1.0f;
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            float basePitch = pitchReady ? smoothPitch : Settings.CameraPitch.Value;

            // A ridge, a tree or an arch between the camera and the focus: first the camera looks more from above (an
            // RTS view stays an RTS view); only when even a steep view is blocked (a tunnel, a roof) is it pulled in
            // front of the obstacle. Both are smoothed: quick to react, slow to come back, so passing a ridge does not
            // make the picture jump.
            float wantLift = 0f, wantArm = 1f;
            float length = Mathf.Max(0.01f, smoothDistance);
            for (int step = 0; step <= LiftSteps; step++)
            {
                float lift = step * (MaxLift / LiftSteps);
                float tryPitch = Mathf.Min(SteepestPitch, basePitch + lift);
                Vector3 back = -(Quaternion.Euler(tryPitch, smoothYaw, 0f) * Vector3.forward);
                // the first meters are skipped: ground rising right behind the focus (a slope, a step) would
                // otherwise pull the camera down onto the focus, which is far worse than a corner of hidden ground
                float blocked = length > SkipNear + 1f ? NearestStaticHit(lookAt + back * SkipNear, back, length - SkipNear) : -1f;
                if (blocked >= 0f) blocked += SkipNear;
                wantLift = tryPitch - basePitch;
                if (blocked < 0f) { wantArm = 1f; break; }
                wantArm = Mathf.Clamp(Mathf.Max(1.5f, blocked - 0.2f) / length, 0.02f, 1f);
                if (tryPitch >= SteepestPitch) break;
            }

            liftPitch = Mathf.SmoothDamp(liftPitch, wantLift, ref liftVelocity, wantLift > liftPitch ? 0.12f : 0.7f, float.PositiveInfinity, dt);
            liftPitch = Mathf.Clamp(liftPitch, 0f, MaxLift);
            armFactor = Mathf.SmoothDamp(armFactor, wantArm, ref armFactorVelocity, wantArm < armFactor ? 0.07f : 0.45f, float.PositiveInfinity, dt);
            armFactor = Mathf.Clamp(armFactor, 0.02f, 1f); // the smoothing may overshoot: never past the focus

            Quaternion rotation = Quaternion.Euler(Mathf.Min(SteepestPitch, basePitch + liftPitch), smoothYaw, 0f);
            Vector3 position = lookAt - (rotation * Vector3.forward) * (length * armFactor);

            cameraState.position = position;
            cameraState.rotation = rotation;
            cameraState.fov = Settings.CameraFov.Value;
        }

        // the view tilts up to this much further down to see past an obstacle, checked in this many steps
        private const float MaxLift = 30f, SteepestPitch = 88f;
        private const int LiftSteps = 3;
        private const float SkipNear = 5f;
        private float liftPitch, liftVelocity;

        private static readonly RaycastHit[] CastBuffer = new RaycastHit[64];

        /// <summary>
        /// Distance to the nearest piece of the level between the focus and the camera, or -1. Objects of the game
        /// (drop pods, interactables, bodies: anything with a network identity) never block the camera: the first hero
        /// stands inside its pod and the camera must not be dragged into it.
        /// </summary>
        private static float NearestStaticHit(Vector3 from, Vector3 direction, float length)
        {
            int count = Physics.SphereCastNonAlloc(from, 0.4f, direction, CastBuffer, length, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            float best = -1f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = CastBuffer[i];
                if (!IsLevelGeometry(hit.collider)) continue;
                if (best < 0f || hit.distance < best) best = hit.distance;
            }
            return best;
        }

        /// <summary>True for terrain and buildings; false for the game's objects (pods, interactables, bodies).</summary>
        private static bool IsLevelGeometry(Collider collider)
        {
            if (!collider) return false;
            if (collider.GetComponentInParent<UnityEngine.Networking.NetworkIdentity>()) return false;
            if (collider.GetComponentInParent<RoR2.CharacterBody>()) return false;
            return true;
        }

        public bool IsUserLookAllowed(CameraRigController cameraRigController) => false;

        public bool IsUserControlAllowed(CameraRigController cameraRigController) => false;

        public bool IsHudAllowed(CameraRigController cameraRigController) => false;

        // ---- helpers -----------------------------------------------------------------------------

        private Vector3 ClampToMap(Vector3 p, bool deliberate)
        {
            if (MapInfo.Valid)
            {
                Bounds b = MapInfo.Bounds;
                p.x = Mathf.Clamp(p.x, b.min.x, b.max.x);
                p.z = Mathf.Clamp(p.z, b.min.z, b.max.z);
            }
            p.y = FocusHeight(p, deliberate);
            return p;
        }

        private const int MaxSurfaces = 10;

        // ground nodes this far around the point tell what heights a hero can stand at there; a surface this close in
        // height to one of them is walkable ground
        private const float WalkReach = 14f, WalkSlack = 8f;
        private static readonly List<float> NearWalk = new List<float>(64);

        /// <summary>False only while hc_cam audit measures the old behaviour (any surface counted).</summary>
        private static bool walkFilter = true;

        /// <summary>
        /// The height the focus should have above <paramref name="p"/> (p.y is the level the camera is on now, or the
        /// height of the thing that is flown to).
        ///  * Only ground a hero can stand on counts (the stock node graph): a rock spire, a roof or a sky decoration
        ///    never lifts the camera, and the level never leaves the height range of the map's walkable ground.
        ///  * Of all the surfaces under the point (a ramp over a passage, a bridge over a pit, floors of a cave) the one
        ///    nearest to that level is taken, so the camera stays on its level instead of hopping between them.
        ///  * When panning, a surface far below that level is a chasm: the focus keeps its height over it (and the
        ///    camera does not dive into the pit) unless the player stays over it for a while.
        /// </summary>
        private float FocusHeight(Vector3 p, bool deliberate)
        {
            float level = p.y;
            bool filter = walkFilter && MapInfo.Valid;
            NearWalk.Clear();
            if (filter)
            {
                level = Mathf.Clamp(level, MapInfo.LowestWalkable - 4f, MapInfo.HighestWalkable + 4f);
                MapInfo.WalkableHeightsNear(p, WalkReach, NearWalk);
            }
            float top, length;
            if (MapInfo.Valid)
            {
                Bounds b = MapInfo.Bounds;
                top = Mathf.Max(b.max.y, level) + 20f;
                length = top - b.min.y + 60f;
            }
            else
            {
                top = level + 100f;
                length = 600f;
            }

            overAbyss = false;

            // One ray at a time, each starting just below the last surface: a single cast through all colliders
            // reports a collider once, so a ramp over a passage that belong to one mesh would show only the ramp.
            bool found = false;
            float best = level, bestGap = float.MaxValue;
            lowestSurface = float.MaxValue;
            surfaces = 0;
            Vector3 origin = new Vector3(p.x, top, p.z);
            float remaining = length;
            for (int n = 0; n < MaxSurfaces && remaining > 0f; n++)
            {
                if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, remaining, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    break;
                float step = hit.distance + 0.05f;
                origin.y -= step;
                remaining -= step;
                if (!IsLevelGeometry(hit.collider)) continue;
                surfaces++;
                if (hit.point.y < lowestSurface) lowestSurface = hit.point.y;
                if (filter && !Walkable(hit.point.y, level)) continue;
                float gap = Mathf.Abs(hit.point.y - level);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = hit.point.y;
                    found = true;
                }
            }
            if (!found)
            {
                // nothing walkable was hit (a slope steeper than the slack, more layers than the rays look through):
                // the height of the nearby ground nodes themselves
                if (NearWalk.Count == 0) return level;
                for (int i = 0; i < NearWalk.Count; i++)
                {
                    float gap = Mathf.Abs(NearWalk[i] - level);
                    if (gap < bestGap) { bestGap = gap; best = NearWalk[i]; }
                }
            }

            if (!deliberate && best < level - AbyssDrop)
            {
                overAbyss = true;
                if (abyssTime < AbyssDwell) return level;
            }
            return best;
        }

        private float settleTime;

        /// <summary>
        /// The camera stands still above lower ground (a chasm crossed at its edge's level, a void beside a path): after
        /// a short look it comes down onto that ground instead of hanging in the air there for good.
        /// </summary>
        private void Settle(float dt)
        {
            if (!overAbyss && !Hanging(focus, out _)) { settleTime = 0f; return; }
            settleTime += dt;
            if (settleTime < StopDwell) return;
            settleTime = 0f;

            abyssTime = AbyssDwell; // the ground under it, if there is any
            focus = ClampToMap(focus, false);
            abyssTime = 0f;
            overAbyss = false;
            if (Hanging(focus, out float ground)) focus.y = ground; // nothing under it: the walkable ground beside it
        }

        /// <summary>The focus is more than 10 m above every ground node within 30 m; <paramref name="ground"/> is the highest of them.</summary>
        private static bool Hanging(Vector3 p, out float ground)
        {
            ground = float.MinValue;
            if (!walkFilter || !MapInfo.Valid) return false;
            NearWalk.Clear();
            MapInfo.WalkableHeightsNear(p, 30f, NearWalk);
            for (int i = 0; i < NearWalk.Count; i++) ground = Mathf.Max(ground, NearWalk[i]);
            return NearWalk.Count > 0 && p.y > ground + 10f;
        }

        /// <summary>
        /// A surface at height y is ground a hero can stand on: near a ground node of about that height. Off the
        /// walkable area (no node around) nothing may pull the camera up; going down is the chasm rule's business.
        /// </summary>
        private static bool Walkable(float y, float level)
        {
            if (NearWalk.Count == 0) return y <= level + 0.5f;
            for (int i = 0; i < NearWalk.Count; i++)
                if (Mathf.Abs(y - NearWalk[i]) <= WalkSlack) return true;
            return false;
        }

        /// <summary>
        /// hc_cam audit: pans along 32 lines across the whole map through the real height code (the view itself does not
        /// move) and counts the places where the focus ends up more than 10 m above any ground a hero can stand on
        /// nearby. Runs once the way the camera used to work and once as it works now.
        /// </summary>
        internal string AuditHeights()
        {
            if (!MapInfo.Valid) return "no map data";
            Vector3 keepFocus = focus;
            float keepAbyss = abyssTime;
            string result = "";
            for (int pass = 0; pass < 2; pass++)
            {
                walkFilter = pass == 1;
                int total = 0, bad = 0, held = 0, stops = 0, stuck = 0;
                float worst = 0f;
                Vector3 worstAt = Vector3.zero;
                Bounds b = MapInfo.Bounds;
                var near = new List<float>(64);
                const float step = 3f, dt = step / 40f; // about the pan speed at the usual distance
                for (int line = 0; line < 32; line++)
                {
                    float f = ((line % 8) + 0.5f) / 8f;
                    bool alongX = line < 16;
                    bool back = (line / 8) % 2 == 1;
                    Vector3 a = alongX ? new Vector3(b.min.x, b.center.y, Mathf.Lerp(b.min.z, b.max.z, f))
                                       : new Vector3(Mathf.Lerp(b.min.x, b.max.x, f), b.center.y, b.min.z);
                    Vector3 z = alongX ? new Vector3(b.max.x, b.center.y, a.z) : new Vector3(a.x, b.center.y, b.max.z);
                    if (back) { Vector3 t = a; a = z; z = t; }

                    focus = ClampToMap(a, true);
                    abyssTime = 0f;
                    int steps = Mathf.CeilToInt(Vector3.Distance(a, z) / step);
                    for (int s = 1; s <= steps; s++)
                    {
                        Vector3 at = Vector3.Lerp(a, z, s / (float)steps);
                        float before = focus.y;
                        focus = ClampToMap(new Vector3(at.x, focus.y, at.z), false);
                        abyssTime = overAbyss ? abyssTime + dt : 0f;

                        near.Clear();
                        MapInfo.WalkableHeightsNear(focus, 30f, near);
                        float reference = MapInfo.HighestWalkable;
                        if (near.Count > 0)
                        {
                            reference = near[0];
                            for (int i = 1; i < near.Count; i++) reference = Mathf.Max(reference, near[i]);
                        }
                        float excess = focus.y - reference;
                        total++;
                        if (excess <= 10f) continue;
                        // climbed up there (the fault) or only kept its height over a drop (the chasm rule, wanted)
                        if (focus.y > before + 0.5f)
                        {
                            bad++;
                            if (excess > worst) { worst = excess; worstAt = focus; }
                        }
                        else held++;

                        // the player stops here for two seconds (the frame loop's own rule): still in the air?
                        if (s % 40 != 0) continue;
                        stops++;
                        abyssTime = 0f;
                        settleTime = 0f;
                        if (pass == 1) // the old camera never came down while standing still
                            for (float waited = 0f; waited < 2f; waited += dt) Settle(dt);
                        near.Clear();
                        MapInfo.WalkableHeightsNear(focus, 30f, near);
                        float top = float.MinValue;
                        for (int i = 0; i < near.Count; i++) top = Mathf.Max(top, near[i]);
                        if (near.Count > 0 && focus.y - top > 10f) stuck++;
                    }
                }
                result += $"{(pass == 0 ? "old" : "new")}: {total} points, {bad} climbed too high (worst +{worst:0} m at {worstAt.x:0},{worstAt.y:0},{worstAt.z:0}), " +
                          $"{held} held over a drop, {stuck} of {stops} stops left hanging; ";
            }
            walkFilter = true;
            focus = keepFocus;
            abyssTime = keepAbyss;
            return result + $"walkable heights {MapInfo.LowestWalkable:0}..{MapInfo.HighestWalkable:0}";
        }

        /// <summary>Terrain height at the XZ of <paramref name="p"/>; <paramref name="fallback"/> when nothing is below.</summary>
        internal static float GroundHeight(Vector3 p, float fallback)
        {
            int mask = LayerIndex.world.mask;

            // First look for ground just below the current height: this keeps the camera on the level the player is
            // looking at (caves, bridges, multi-storey maps) instead of snapping to a roof far above.
            Vector3 near = new Vector3(p.x, p.y + 14f, p.z);
            if (Physics.Raycast(near, Vector3.down, out RaycastHit hit, 200f, mask, QueryTriggerInteraction.Ignore))
                return hit.point.y;

            float top = (MapInfo.Valid ? MapInfo.Bounds.max.y : p.y) + 60f;
            float bottom = MapInfo.Valid ? MapInfo.Bounds.min.y : p.y - 400f;
            if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out hit, top - bottom + 200f, mask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return fallback;
        }
    }
}
