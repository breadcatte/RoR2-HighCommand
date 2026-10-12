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
        internal void FocusOn(Vector3 worldPoint, bool snap = false)
        {
            focus = ClampToMap(new Vector3(worldPoint.x, worldPoint.y, worldPoint.z));
            if (snap || !initialised)
            {
                smoothFocus = focus;
                focusVelocity = Vector3.zero;
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

            if (Settings.EdgeScroll.Value && ClientInput.PointerInWindow && !ClientInput.PointerOverUi && !PauseManager.isPaused)
            {
                int e = Settings.EdgeScrollPixels.Value;
                if (mouse.x <= e) move.x -= 1f;
                else if (mouse.x >= Screen.width - 1 - e) move.x += 1f;
                if (mouse.y <= e) move.y -= 1f;
                else if (mouse.y >= Screen.height - 1 - e) move.y += 1f;
            }

            // following something (selected heroes): the focus tracks it until the player pans by hand
            if (FollowTarget != null)
            {
                if (move.sqrMagnitude > 0.0001f) FollowTarget = null;
                else
                {
                    Vector3? followed = FollowTarget();
                    if (followed.HasValue) focus = ClampToMap(followed.Value);
                }
            }

            if (move.sqrMagnitude > 0.0001f)
            {
                if (move.sqrMagnitude > 1f) move.Normalize();
                float speed = 38f * Settings.CameraPanSpeed.Value * Mathf.Max(0.35f, distance / 45f);
                if (ClientInput.Key(Settings.KeyFastCamera.Value)) speed *= 2.2f;
                Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
                Vector3 delta = (flat * Vector3.right * move.x + flat * Vector3.forward * move.y) * (speed * dt);
                focus = ClampToMap(focus + delta);
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
                smoothFocus = Vector3.SmoothDamp(smoothFocus, focus, ref focusVelocity, smooth, float.PositiveInfinity, dt);
                smoothYaw = Mathf.SmoothDampAngle(smoothYaw, yaw, ref yawVelocity, smooth, float.PositiveInfinity, dt);
                smoothDistance = Mathf.SmoothDamp(smoothDistance, distance, ref distanceVelocity, smooth * 0.8f, float.PositiveInfinity, dt);
            }
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

            // Keep the camera out of hills and walls: pull it in front of whatever is between it and the focus.
            Vector3 toCamera = position - lookAt;
            float length = toCamera.magnitude;
            if (length > 0.01f)
            {
                float blocked = NearestStaticHit(lookAt, toCamera / length, length);
                if (blocked >= 0f)
                    position = lookAt + (toCamera / length) * Mathf.Max(1.5f, blocked - 0.2f);
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
                if (!hit.collider) continue;
                if (hit.collider.GetComponentInParent<UnityEngine.Networking.NetworkIdentity>()) continue;
                if (hit.collider.GetComponentInParent<RoR2.CharacterBody>()) continue;
                if (best < 0f || hit.distance < best) best = hit.distance;
            }
            return best;
        }

        public bool IsUserLookAllowed(CameraRigController cameraRigController) => false;

        public bool IsUserControlAllowed(CameraRigController cameraRigController) => false;

        public bool IsHudAllowed(CameraRigController cameraRigController) => false;

        // ---- helpers -----------------------------------------------------------------------------

        private Vector3 ClampToMap(Vector3 p)
        {
            if (MapInfo.Valid)
            {
                Bounds b = MapInfo.Bounds;
                p.x = Mathf.Clamp(p.x, b.min.x, b.max.x);
                p.z = Mathf.Clamp(p.z, b.min.z, b.max.z);
            }
            p.y = GroundHeight(p, p.y);
            return p;
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
