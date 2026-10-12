using System;
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

        internal float Distance => smoothDistance;

        // wanted state
        private Vector3 focus;
        private float yaw = 25f;
        private float distance = 48f;

        // smoothed state
        private Vector3 smoothFocus;
        private float smoothYaw;
        private float smoothDistance;
        private Vector3 focusVelocity;
        private float yawVelocity;
        private float distanceVelocity;

        // height of the focus (see FocusHeight)
        private const float AbyssDrop = 12f;        // ground this far below the level the camera is on counts as a chasm
        private const float AbyssDwell = 5f;        // seconds of panning over a chasm before the camera follows it down
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
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("RtsCamera: could not release the camera rig: " + e.Message);
            }
            Rig = null;
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
                                   $"surfaces={surfaces} lowestSurfaceY={(surfaces > 0 ? lowestSurface : 0f):0.0} legacyY={legacy:0.0} abyss={overAbyss} dwell={abyssTime:0.0} arm={armFactor:0.00}");
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

            if (Settings.EdgeScroll.Value && ClientInput.PointerInWindow && !ClientInput.PointerOverUi && !ClientInput.MouseBlocked)
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
            else if (!sweeping) abyssTime = 0f;

            // --- zoom ---
            float scroll = ClientInput.PointerOverUi || ClientInput.KeyboardBlocked ? 0f : ClientInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float factor = Mathf.Pow(0.88f, scroll * Settings.CameraZoomSpeed.Value);
                distance = Mathf.Clamp(distance * factor, Settings.CameraMinDistance.Value, Settings.CameraMaxDistance.Value);
            }

            // --- rotate ---
            float rotate = Settings.CameraRotateSpeed.Value;
            if (ClientInput.MouseHeld(2) && !ClientInput.PointerOverUi && !PauseManager.isPaused)
                yaw += mouseDelta.x * 0.22f * rotate;
            if (ClientInput.Key(Settings.KeyRotateLeft.Value)) yaw -= 90f * rotate * dt;
            if (ClientInput.Key(Settings.KeyRotateRight.Value)) yaw += 90f * rotate * dt;

            // --- smoothing ---
            float smooth = Settings.CameraSmoothing.Value;
            if (smooth <= 0.001f)
            {
                smoothFocus = focus;
                smoothYaw = yaw;
                smoothDistance = distance;
            }
            else
            {
                smoothYaw = Mathf.SmoothDampAngle(smoothYaw, yaw, ref yawVelocity, smooth, float.PositiveInfinity, dt);
                smoothDistance = Mathf.SmoothDamp(smoothDistance, distance, ref distanceVelocity, smooth * 0.8f, float.PositiveInfinity, dt);
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
                if (Rig != rig)
                {
                    Rig = rig;
                    OnRigChanged();
                }
            }
            if (Rig && !Rig.isActiveAndEnabled) Rig = null;

            UpdateListener();
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

            float pitch = Settings.CameraPitch.Value;
            Quaternion rotation = Quaternion.Euler(pitch, smoothYaw, 0f);
            Vector3 forward = rotation * Vector3.forward;

            Vector3 lookAt = smoothFocus + Vector3.up * 1.0f;
            Vector3 position = lookAt - forward * smoothDistance;

            // Keep the camera out of hills and walls: pull it in front of whatever is between it and the focus. The
            // pull-in is smoothed: quick to go in, slow to come back out, so passing under a bridge or a ramp does not
            // make the picture jump in and out.
            Vector3 toCamera = position - lookAt;
            float length = toCamera.magnitude;
            if (length > 0.01f)
            {
                float blocked = NearestStaticHit(lookAt, toCamera / length, length);
                float want = blocked >= 0f ? Mathf.Clamp(Mathf.Max(1.5f, blocked - 0.2f) / length, 0.02f, 1f) : 1f;
                float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                armFactor = Mathf.SmoothDamp(armFactor, want, ref armFactorVelocity, want < armFactor ? 0.07f : 0.45f, float.PositiveInfinity, dt);
                armFactor = Mathf.Clamp(armFactor, 0.02f, 1f); // the smoothing may overshoot: never past the focus
                position = lookAt + (toCamera / length) * (length * armFactor);
            }

            cameraState.position = position;
            cameraState.rotation = rotation;
            cameraState.fov = Settings.CameraFov.Value;
        }

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

        private static readonly RaycastHit[] LevelHits = new RaycastHit[32];

        /// <summary>
        /// The height the focus should have above <paramref name="p"/> (p.y is the level the camera is on now, or the
        /// height of the thing that is flown to).
        ///  * Of all the surfaces under the point (a ramp over a passage, a bridge over a pit, floors of a cave) the one
        ///    nearest to that level is taken, so the camera stays on its level instead of hopping between them.
        ///  * When panning, a surface far below that level is a chasm: the focus keeps its height over it (and the
        ///    camera does not dive into the pit) unless the player stays over it for a while.
        /// </summary>
        private float FocusHeight(Vector3 p, bool deliberate)
        {
            float level = p.y;
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
            int count = Physics.RaycastNonAlloc(new Vector3(p.x, top, p.z), Vector3.down, LevelHits, length,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float best = level, bestGap = float.MaxValue;
            lowestSurface = float.MaxValue;
            surfaces = 0;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = LevelHits[i];
                if (!IsLevelGeometry(hit.collider)) continue;
                surfaces++;
                if (hit.point.y < lowestSurface) lowestSurface = hit.point.y;
                float gap = Mathf.Abs(hit.point.y - level);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = hit.point.y;
                    found = true;
                }
            }
            if (!found) return level;

            if (!deliberate && best < level - AbyssDrop)
            {
                overAbyss = true;
                if (abyssTime < AbyssDwell) return level;
            }
            return best;
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
