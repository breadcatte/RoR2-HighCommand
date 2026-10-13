using System;
using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.Mode;
using RoR2;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// The commander's camera, made the way strategy cameras are (0 A.D., Spring, StarCraft II, Company of Heroes, Total
    /// War). It plugs into the stock CameraRigController as an override (ICameraStateProvider), so post-processing, fog,
    /// the UI camera and screen shake all keep working; only the camera pose is ours.
    ///
    ///  * It orbits a focus point. WASD and the screen edge slide the focus over the map (the edge works over the panels
    ///    there too) at a speed that grows with the zoom; nothing in the level slows it down or stops it.
    ///  * The focus never touches the real surface: its height comes from CameraGround, a copy of the ground where only
    ///    walkable ground exists, read more blurred the farther the camera is. A tree, a rock, a roof or a ledge is not
    ///    in it, so nothing catches the camera, and the camera never sinks or climbs by itself while it stands.
    ///  * A point the player flew to (a hero, the followed army, the point zoomed at) lends its own level to the focus
    ///    around it (fading out over a few dozen metres): a bridge or a cave floor is looked at at its own height.
    ///  * Only the player changes the distance (the wheel, towards the cursor) and the tilt (middle mouse, PageUp/Down).
    ///    The camera only tilts up as far as it must to stay out of big ground (smoothly), and a small piece of the level
    ///    that hides a hero is hidden (CollectHeroCovers).
    /// </summary>
    internal sealed class RtsCamera : MonoBehaviour, ICameraStateProvider
    {
        internal static RtsCamera Instance { get; private set; }

        internal CameraRigController Rig { get; private set; }

        internal Camera SceneCam => Rig ? Rig.sceneCam : null;

        /// <summary>Where the camera looks at (smoothed).</summary>
        internal Vector3 Focus => smoothFocus;

        internal float Yaw => smoothYaw;

        /// <summary>The tilt in degrees (90 = straight down), as shown now (with the zoom tilt and the lift off the ground).</summary>
        internal float Pitch => Mathf.Min(SteepestPitch, ViewPitch() + liftPitch);

        internal float Distance => smoothDistance;

        // ZoomTilt: from this far above the nearest zoom the view starts lowering, down to LowPitch at the nearest zoom
        private const float ZoomTiltRange = 30f, LowPitch = 16f;

        /// <summary>
        /// The player's tilt, lowered towards the ground as the camera comes close (Company of Heroes, Total War: far
        /// away a map, close up a side view of the heroes). A tilt already lower than that is left alone.
        /// </summary>
        private float ViewPitch()
        {
            float tilt = pitchReady ? smoothPitch : Settings.CameraPitch.Value;
            if (!Settings.ZoomTilt.Value) return tilt;
            float k = 1f - Mathf.Clamp01((smoothDistance - Settings.CameraMinDistance.Value) / ZoomTiltRange);
            k = k * k * (3f - 2f * k);
            return Mathf.Lerp(tilt, Mathf.Min(tilt, LowPitch), k);
        }

        // ---- wanted state (input) ----------------------------------------------------------------

        private Vector2 focusXZ;
        private const float StartYaw = 25f, StartDistance = 48f;
        private float yaw = StartYaw;
        private float distance = StartDistance;
        private float pitch;
        private bool pitchReady;
        private float configPitch;

        // the player may lay the camera almost on the ground (it then only tilts up as far as the ground makes it)
        private const float MinPitch = 4f, MaxPitch = 89f;

        // ---- shown state (smoothed) --------------------------------------------------------------

        private Vector2 smoothXZ, xzVelocity;
        private float smoothHeight, heightVelocity;
        private Vector3 smoothFocus;
        private float smoothYaw, yawVelocity;
        private float smoothDistance, distanceVelocity;
        private float smoothPitch, pitchVelocity;

        // ---- the height of the focus -------------------------------------------------------------

        // The ground is read blurred over a bell of HeightBlur x distance (0 A.D. filters its terrain over a radius that
        // grows with the zoom): far away only the shape of the land counts, close up the ground under the focus.
        private const float HeightBlur = 0.3f, MaxHeightSigma = 64f;

        // The level lent by a point the player flew to: how far its own height lies from the camera's ground there,
        // fading out over a bell of AnchorReach metres around it, forgotten beyond three of them.
        private const float AnchorReach = 24f, MaxAnchorOffset = 60f;
        private Vector2 anchorXZ;
        private float anchorOffset;
        private bool hasAnchor;

        private bool initialised;
        private bool rotating;
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
            ShowAllHidden();
        }

        // ---- public commands ---------------------------------------------------------------------

        /// <summary>
        /// Moves the camera to look at a real point of the world (a hero, an alert): smoothly, unless <paramref name="snap"/>.
        /// The focus takes that point's own level there (a hero on a bridge is looked at on the bridge).
        /// </summary>
        internal void FocusOn(Vector3 worldPoint, bool snap = false)
        {
            focusXZ = ClampXZ(new Vector2(worldPoint.x, worldPoint.z));
            SetAnchor(worldPoint);
            if (snap || !initialised) Snap();
        }

        /// <summary>Moves the camera over a place of the map (the minimap): only the place, its height is the ground's.</summary>
        internal void PanTo(Vector3 worldPoint)
        {
            focusXZ = ClampXZ(new Vector2(worldPoint.x, worldPoint.z));
            if (!initialised) Snap();
        }

        private void Snap()
        {
            smoothXZ = focusXZ;
            xzVelocity = Vector2.zero;
            smoothHeight = HeightAt(smoothXZ, smoothDistance);
            heightVelocity = 0f;
            smoothFocus = new Vector3(smoothXZ.x, smoothHeight, smoothXZ.y);
            initialised = true;
        }

        /// <summary>While set, the focus follows what it returns (null result = keep position). Cleared by manual panning.</summary>
        internal Func<Vector3?> FollowTarget;

        internal bool IsFollowing => FollowTarget != null;

        internal void SetDistance(float meters)
        {
            distance = Mathf.Clamp(meters, Settings.CameraMinDistance.Value, Settings.CameraMaxDistance.Value);
        }

        internal string DebugWanted() =>
            $"wanted: distance={distance:0.0} pitch={pitch:0.0} yaw={yaw:0} smoothPitch={smoothPitch:0.0} lift={liftPitch:0.0} " +
            $"anchor={(hasAnchor ? $"{anchorOffset:+0.0;-0.0} m, weight {AnchorWeight(smoothXZ):0.00}" : "none")}";

        /// <summary>Turns / tilts the camera (testing, screenshots).</summary>
        internal void SetAngles(float? newYaw, float? newPitch)
        {
            if (newYaw.HasValue) yaw = newYaw.Value;
            if (newPitch.HasValue) pitch = Mathf.Clamp(newPitch.Value, MinPitch, MaxPitch);
        }

        // ---- testing (hc_cam sweep) --------------------------------------------------------------

        private bool sweeping;
        private Vector2 sweepFrom, sweepTo;
        private float sweepTime, sweepSeconds, sweepLogAt;

        /// <summary>Pans the focus along a line the way the keys do (same smoothing, same height), logging as it goes.</summary>
        internal void Sweep(Vector3 from, Vector3 to, float seconds)
        {
            sweepFrom = new Vector2(from.x, from.z);
            sweepTo = new Vector2(to.x, to.z);
            sweepTime = 0f;
            sweepLogAt = 0f;
            sweepSeconds = Mathf.Max(0.5f, seconds);
            sweeping = true;
            FollowTarget = null;
            PanTo(from);
        }

        private void AdvanceSweep(float dt)
        {
            sweepTime += dt;
            float k = Mathf.Clamp01(sweepTime / sweepSeconds);
            focusXZ = ClampXZ(Vector2.Lerp(sweepFrom, sweepTo, k));

            if (sweepTime >= sweepLogAt)
            {
                sweepLogAt += 0.4f;
                float fine = CameraGround.Height(smoothXZ, CameraGround.FineSigma);
                Plugin.Log.LogInfo($"RtsCamera sweep t={sweepTime:0.0} xz=({smoothXZ.x:0},{smoothXZ.y:0}) wantedY={HeightAt(smoothXZ, smoothDistance):0.0} " +
                                   $"shownY={smoothFocus.y:0.0} fineY={fine:0.0} anchor={(hasAnchor ? AnchorWeight(smoothXZ) : 0f):0.00} " +
                                   $"lift={liftPitch:0} hidden={hidden.Count}");
            }
            if (k >= 1f)
            {
                sweeping = false;
                Plugin.Log.LogInfo($"RtsCamera sweep done: camera height speed max {sweepClimb:0.0} m/s, tilt speed max {sweepTurn:0.0} deg/s, " +
                                   $"lift max {sweepLift:0.0} deg, focus height {sweepLowY:0.0}..{sweepHighY:0.0}");
                sweepClimb = sweepTurn = sweepLift = 0f;
                sweepLowY = float.MaxValue;
                sweepHighY = float.MinValue;
            }
        }

        // how hard the shown camera moved during a sweep (a jump is a high speed of height or tilt)
        private float sweepClimb, sweepTurn, sweepLift, sweepLowY = float.MaxValue, sweepHighY = float.MinValue;
        private Vector3 lastPosePosition;
        private float lastPosePitch;

        private void MeasureSweep(float dt, float shownPitch)
        {
            if (!sweeping) return;
            if (sweepTime > 0.3f) // after the start (the camera may have to travel to the line first)
            {
                sweepClimb = Mathf.Max(sweepClimb, Mathf.Abs(posePosition.y - lastPosePosition.y) / dt);
                sweepTurn = Mathf.Max(sweepTurn, Mathf.Abs(shownPitch - lastPosePitch) / dt);
                sweepLift = Mathf.Max(sweepLift, liftPitch);
                sweepLowY = Mathf.Min(sweepLowY, smoothFocus.y);
                sweepHighY = Mathf.Max(sweepHighY, smoothFocus.y);
            }
            lastPosePosition = posePosition;
            lastPosePitch = shownPitch;
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

            // a turn / tilt with the middle button begun in the world goes on over the panels until the button is let go
            if (!ClientInput.MouseHeld(2) || PauseManager.isPaused) rotating = false;
            else if (ClientInput.MouseDown(2) && !ClientInput.PointerOverUi) rotating = true;

            // --- pan ---
            Vector2 move = Vector2.zero;
            if (ClientInput.Key(Settings.KeyUp.Value)) move.y += 1f;
            if (ClientInput.Key(Settings.KeyDown.Value)) move.y -= 1f;
            if (ClientInput.Key(Settings.KeyRight.Value)) move.x += 1f;
            if (ClientInput.Key(Settings.KeyLeft.Value)) move.x -= 1f;

            // The screen edge pans also where a panel touches the edge (StarCraft II, Age of Empires): the pointer is on
            // the last pixels of the screen, not on a button. Not while turning the view (the hand may reach the edge
            // then) or dragging with the left button on a panel (the minimap).
            if (Settings.EdgeScroll.Value && ClientInput.PointerInWindow && !ClientInput.MouseBlocked && !rotating
                && !(ClientInput.MouseHeld(0) && ClientInput.PointerOverUi))
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
                    if (followed.HasValue)
                    {
                        focusXZ = ClampXZ(new Vector2(followed.Value.x, followed.Value.z));
                        SetAnchor(followed.Value);
                    }
                }
            }

            if (move.sqrMagnitude > 0.0001f)
            {
                if (move.sqrMagnitude > 1f) move.Normalize();
                float speed = 38f * Settings.CameraPanSpeed.Value * Mathf.Max(0.35f, distance / 45f);
                if (ClientInput.Key(Settings.KeyFastCamera.Value)) speed *= 2.2f;
                Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
                Vector3 delta = (flat * Vector3.right * move.x + flat * Vector3.forward * move.y) * (speed * dt);
                focusXZ = ClampXZ(focusXZ + new Vector2(delta.x, delta.z));
            }

            // --- zoom ---
            float scroll = ClientInput.PointerOverUi || ClientInput.KeyboardBlocked ? 0f : ClientInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float factor = Mathf.Pow(0.88f, scroll * Settings.CameraZoomSpeed.Value);
                float before = distance;
                distance = Mathf.Clamp(distance * factor, Settings.CameraMinDistance.Value, Settings.CameraMaxDistance.Value);

                // towards the point under the mouse: it stays under the mouse (Company of Heroes, Total War); the focus
                // moves the same share of the way to it as the distance shrinks (and back out the same way)
                if (Settings.ZoomToCursor.Value && FollowTarget == null && ClientInput.PointerInWindow
                    && WorldPicker.GroundPoint(mouse, out Vector3 under))
                {
                    Vector2 shift = new Vector2(under.x, under.z) - focusXZ;
                    if (shift.magnitude < Settings.CameraMaxDistance.Value * 2f)
                    {
                        focusXZ = ClampXZ(focusXZ + shift * (1f - distance / before));
                        // zooming in on ground a hero can stand on: that ground's level (the floor of a cave, a bridge)
                        if (distance < before && Walkable(under)) SetAnchor(under);
                    }
                }
            }

            // the start view again (Age of Empires IV / Company of Heroes): turn, tilt and zoom, not the place
            if (ClientInput.KeyDown(Settings.KeyResetCamera.Value))
            {
                yaw = StartYaw;
                pitch = Settings.CameraPitch.Value;
                distance = Mathf.Clamp(StartDistance, Settings.CameraMinDistance.Value, Settings.CameraMaxDistance.Value);
            }

            // --- rotate ---
            float rotate = Settings.CameraRotateSpeed.Value;
            float tilt = Settings.CameraTiltSpeed.Value;
            if (rotating)
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
                smoothXZ = focusXZ;
                smoothYaw = yaw;
                smoothDistance = distance;
                smoothPitch = pitch;
            }
            else
            {
                smoothXZ = Vector2.SmoothDamp(smoothXZ, focusXZ, ref xzVelocity, smooth, float.PositiveInfinity, dt);
                smoothYaw = Mathf.SmoothDampAngle(smoothYaw, yaw, ref yawVelocity, smooth, float.PositiveInfinity, dt);
                smoothDistance = Mathf.SmoothDamp(smoothDistance, distance, ref distanceVelocity, smooth, float.PositiveInfinity, dt);
                smoothPitch = Mathf.SmoothDamp(smoothPitch, pitch, ref pitchVelocity, smooth, float.PositiveInfinity, dt);
            }

            // the height follows the place and the zoom as shown, a little softer than the place itself
            if (hasAnchor && FollowTarget == null && (smoothXZ - anchorXZ).sqrMagnitude > 9f * AnchorReach * AnchorReach)
                hasAnchor = false; // far from it: it no longer adds anything
            float wantHeight = HeightAt(smoothXZ, smoothDistance);
            smoothHeight = smooth <= 0.001f ? wantHeight
                : Mathf.SmoothDamp(smoothHeight, wantHeight, ref heightVelocity, Mathf.Max(0.2f, smooth * 2f), float.PositiveInfinity, dt);
            smoothFocus = new Vector3(smoothXZ.x, smoothHeight, smoothXZ.y);
        }

        // ---- the height of the focus -------------------------------------------------------------

        /// <summary>The focus height over a place, with the camera at <paramref name="atDistance"/>.</summary>
        private float HeightAt(Vector2 xz, float atDistance)
        {
            float ground = CameraGround.Height(xz, Mathf.Clamp(atDistance * HeightBlur, CameraGround.FineSigma, MaxHeightSigma));
            if (float.IsNaN(ground))
            {
                // no map data yet (the first moments of a stage): the surface under the focus
                float now = initialised ? smoothHeight : 0f;
                ground = GroundHeight(new Vector3(xz.x, now, xz.y), now);
            }
            if (hasAnchor) ground += anchorOffset * AnchorWeight(xz);
            return ground;
        }

        private float AnchorWeight(Vector2 xz)
        {
            float d2 = (xz - anchorXZ).sqrMagnitude;
            return Mathf.Exp(-d2 / (2f * AnchorReach * AnchorReach));
        }

        /// <summary>
        /// A real point of the world lends its level to the focus around it: how far it lies from the camera's finest
        /// ground there. Almost nothing on open ground; on a bridge, a ledge or a cave floor the difference of levels.
        /// </summary>
        private void SetAnchor(Vector3 point)
        {
            float ground = CameraGround.Height(new Vector2(point.x, point.z), CameraGround.FineSigma);
            if (float.IsNaN(ground)) return;
            anchorXZ = new Vector2(point.x, point.z);
            anchorOffset = Mathf.Clamp(point.y - ground, -MaxAnchorOffset, MaxAnchorOffset);
            hasAnchor = true;
        }

        private static readonly List<float> NearWalk = new List<float>(64);

        /// <summary>A hero could stand at this point (a ground node of about its height close by): not a treetop or a rock.</summary>
        private static bool Walkable(Vector3 p)
        {
            NearWalk.Clear();
            MapInfo.WalkableHeightsNear(p, 8f, NearWalk);
            for (int i = 0; i < NearWalk.Count; i++)
                if (Mathf.Abs(NearWalk[i] - p.y) < 4f) return true;
            return false;
        }

        // ---- the stock camera rig ----------------------------------------------------------------

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
            ShowAllHidden(); // a new stage: the pieces of the old level are gone
            LevelPieces.Clear();
            hasAnchor = false;
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

        private int poseFrame = -1;
        private Vector3 posePosition;
        private Quaternion poseRotation;

        public void GetCameraState(CameraRigController cameraRigController, ref CameraState cameraState)
        {
            if (!initialised) return;
            if (poseFrame != Time.frameCount)
            {
                poseFrame = Time.frameCount;
                PlaceCamera(Mathf.Max(0.0001f, Time.unscaledDeltaTime));
            }
            cameraState.position = posePosition;
            cameraState.rotation = poseRotation;
            cameraState.fov = Settings.CameraFov.Value;
        }

        private void PlaceCamera(float dt)
        {
            Vector3 lookAt = smoothFocus + Vector3.up * 1.0f;
            float basePitch = ViewPitch();

            // The distance is the player's alone (the wheel) and so is the tilt: like in an RTS, the camera never moves
            // in by itself. It only tilts up as far as it must to stay out of big ground (a low view by a hill), quickly
            // but smoothly, and back down slowly.
            float length = Mathf.Max(0.01f, smoothDistance);
            float wantLift = GroundLift(lookAt, basePitch, smoothYaw, length);
            liftPitch = Mathf.SmoothDamp(liftPitch, wantLift, ref liftVelocity, wantLift > liftPitch ? LiftUpTime : LiftDownTime,
                float.PositiveInfinity, dt);
            liftPitch = Mathf.Clamp(liftPitch, 0f, SteepestPitch);

            float shownPitch = Mathf.Min(SteepestPitch, basePitch + liftPitch);
            poseRotation = Quaternion.Euler(shownPitch, smoothYaw, 0f);
            posePosition = lookAt - (poseRotation * Vector3.forward) * length;
            MeasureSweep(dt, shownPitch);

            // a small piece of the level (a rock, a roof, a tree, a bridge) that hides a hero is hidden while it does
            Occluders.Clear();
            CollectHeroCovers(posePosition);
            HideOccluders();
        }

        private const float SteepestPitch = 88f;
        private const float LiftUpTime = 0.25f, LiftDownTime = 0.6f;
        private const float ProbeRadius = 0.6f; // m around the camera that must be out of the ground too (the near plane)
        private float liftPitch, liftVelocity;

        /// <summary>
        /// How much steeper than <paramref name="basePitch"/> the camera must look to stay out of big ground: 0 almost
        /// always. The tilt that puts the camera exactly at the free height is found directly (a few tries, since the
        /// camera's spot moves as it tilts), and it is kept small: a camera laid low by the ground tilts up over a slope
        /// behind it (under the slope it would show the empty underside of the level), a camera seen from above at most
        /// a few degrees. Beyond that, and when no tilt gets it out at all (the low roof of a cave, a rock taller than
        /// the camera's distance), it looks through the rock: from inside, the rock is not drawn (its faces look away),
        /// while a tilt to the vertical would swing the whole view each time the camera passes a cliff.
        /// </summary>
        private static float GroundLift(Vector3 lookAt, float basePitch, float yaw, float length)
        {
            if (!CameraGround.Valid || LiftMode == 0) return 0f;
            float most = Mathf.Lerp(MostLiftLow, MostLiftHigh, Mathf.InverseLerp(LowViewPitch, HighViewPitch, basePitch));

            float p = basePitch;
            for (int i = 0; i < 6; i++)
            {
                Vector3 cam = lookAt - (Quaternion.Euler(p, yaw, 0f) * Vector3.forward) * length;
                float need = FreeHeightAround(cam);
                if (need <= cam.y + 0.01f) return Mathf.Min(p - basePitch, most);
                if (p >= SteepestPitch) break;
                float s = (need - lookAt.y) / length;
                float next = s >= 1f ? SteepestPitch : Mathf.Asin(Mathf.Max(-1f, s)) * Mathf.Rad2Deg;
                p = Mathf.Min(SteepestPitch, Mathf.Max(next, p + 0.25f));
            }
            return 0f; // no way out: look through
        }

        // the most the camera tilts up by itself: this much for a view laid low by the ground, that much from above
        private const float MostLiftLow = 36f, MostLiftHigh = 8f, LowViewPitch = 16f, HighViewPitch = 50f;

        /// <summary>Testing (hc_cam lift): 0 = never tilts up, 1 = tilts up out of big ground when that gets it out.</summary>
        internal static int LiftMode = 1;

        private static float FreeHeightAround(Vector3 p)
        {
            float need = CameraGround.FreeHeight(p);
            need = Mathf.Max(need, CameraGround.FreeHeight(new Vector3(p.x + ProbeRadius, p.y, p.z)));
            need = Mathf.Max(need, CameraGround.FreeHeight(new Vector3(p.x - ProbeRadius, p.y, p.z)));
            need = Mathf.Max(need, CameraGround.FreeHeight(new Vector3(p.x, p.y, p.z + ProbeRadius)));
            need = Mathf.Max(need, CameraGround.FreeHeight(new Vector3(p.x, p.y, p.z - ProbeRadius)));
            return need;
        }

        private static readonly RaycastHit[] CastBuffer = new RaycastHit[64];

        /// <summary>
        /// Small pieces of the level between the camera and a hero on screen: they hide that hero (it walked behind a
        /// rock, the camera was turned behind a roof). Nothing is hidden while every hero can be seen.
        /// </summary>
        private void CollectHeroCovers(Vector3 cameraPosition)
        {
            behindGround.Clear();
            HcState state = HcState.Instance;
            if (state == null) return;
            Camera cam = SceneCam;
            var heroes = state.Heroes;
            for (int i = 0; i < heroes.Count; i++)
            {
                Hero hero = heroes[i];
                CharacterBody body = hero != null ? hero.Body : null;
                if (!body || !hero.IsAlive) continue;
                Vector3 target = body.corePosition;
                if (cam)
                {
                    Vector3 v = cam.WorldToViewportPoint(target);
                    if (v.z <= 0f || v.x < -0.05f || v.x > 1.05f || v.y < -0.05f || v.y > 1.05f) continue;
                }
                Vector3 to = target - cameraPosition;
                float dist = to.magnitude;
                if (dist < 1.5f || dist > 160f) continue;
                Vector3 dir = to / dist;
                int count = Physics.SphereCastNonAlloc(cameraPosition, 0.25f, dir, CastBuffer, dist - 1f,
                    LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < count; k++)
                {
                    Collider c = CastBuffer[k].collider;
                    Renderer[] parts = LevelPieces.Of(c);
                    if (parts == null) continue;
                    if (parts.Length == 0)
                    {
                        // big ground cannot be hidden: the overlay draws the hero's silhouette instead (not for the
                        // ground right at its feet, which a low view grazes)
                        if (CastBuffer[k].distance < dist - 2.5f) behindGround.Add(hero);
                        continue;
                    }
                    if (Underfoot(c.bounds, body.footPosition)) continue; // the floor the hero stands on: never
                    Occluders.AddRange(parts);
                }
            }
        }

        private readonly HashSet<Hero> behindGround = new HashSet<Hero>();

        /// <summary>A hill or a cliff (too big to hide) stands between the camera and this hero on screen.</summary>
        internal bool IsBehindGround(Hero hero) => hero != null && behindGround.Contains(hero);

        /// <summary>The piece lies under the hero and no higher than its knees: the floor there.</summary>
        private static bool Underfoot(Bounds box, Vector3 foot)
        {
            return foot.x >= box.min.x && foot.x <= box.max.x && foot.z >= box.min.z && foot.z <= box.max.z
                   && box.max.y < foot.y + 1.0f;
        }

        // ---- hiding what is in the way -------------------------------------------------------------------------

        private const float HideHold = 0.5f;     // s: a hidden piece comes back this long after it is out of the way

        private static readonly List<Renderer> Occluders = new List<Renderer>(32);
        private readonly Dictionary<Renderer, float> hidden = new Dictionary<Renderer, float>();
        private readonly List<Renderer> comeBack = new List<Renderer>(16);

        private void HideOccluders()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < Occluders.Count; i++)
            {
                Renderer r = Occluders[i];
                if (!r) continue;
                if (!hidden.ContainsKey(r) && !r.enabled) continue; // switched off by the game itself: not ours
                hidden[r] = now + HideHold;
                if (!r.forceRenderingOff) r.forceRenderingOff = true;
            }

            comeBack.Clear();
            foreach (var pair in hidden)
                if (!pair.Key || pair.Value < now) comeBack.Add(pair.Key);
            for (int i = 0; i < comeBack.Count; i++)
            {
                if (comeBack[i]) comeBack[i].forceRenderingOff = false;
                hidden.Remove(comeBack[i]);
            }
        }

        /// <summary>Shows everything hidden again (a new stage, the camera going away).</summary>
        private void ShowAllHidden()
        {
            foreach (var pair in hidden)
                if (pair.Key) pair.Key.forceRenderingOff = false;
            hidden.Clear();
        }

        public bool IsUserLookAllowed(CameraRigController cameraRigController) => false;

        public bool IsUserControlAllowed(CameraRigController cameraRigController) => false;

        public bool IsHudAllowed(CameraRigController cameraRigController) => false;

        // ---- helpers -----------------------------------------------------------------------------

        // The focus stays in the box of the walkable ground (24 m of margin around it): farther out a RoR2 level is the
        // inside of the mountains around it or the void under the sky, nothing to look at.
        private static Vector2 ClampXZ(Vector2 p)
        {
            if (!MapInfo.Valid) return p;
            Bounds b = MapInfo.Bounds;
            p.x = Mathf.Clamp(p.x, b.min.x, b.max.x);
            p.y = Mathf.Clamp(p.y, b.min.z, b.max.z);
            return p;
        }

        /// <summary>
        /// hc_cam audit: pans along 32 lines across the whole map through the real height code at the start distance and,
        /// where heroes walk, reports how steep the focus height gets (m of height per m of pan: the 95th percentile and
        /// the worst), how often it is more than 10 m above / below all walkable ground near, and, in four turns on every
        /// few points, for the usual view and the lowest one (nearest distance, lowest tilt): how often the camera tilts up
        /// out of big ground and how often it stays in it (no tilt gets it out: it looks through the rock).
        /// </summary>
        internal string AuditHeights()
        {
            if (!MapInfo.Valid || !CameraGround.Valid) return "no map data";
            bool keepAnchor = hasAnchor;
            hasAnchor = false;

            // everything is counted where heroes walk (a ground node within 10 m); the rest of the box (inside the
            // mountains around the map, over the void) only for the views in the ground
            var slopes = new List<float>(8192);
            var near = new List<float>(64);
            int total = 0, floating = 0, sunk = 0, views = 0, lifted = 0, inGround = 0;
            int otherViews = 0, otherInGround = 0, usualViews = 0, usualLifted = 0, usualInGround = 0, samples = 0;
            int otherUsualInGround = 0, playSpots = 0;
            float liftSum = 0f, usualLiftSum = 0f, usualDepthSum = 0f, worst = 0f;
            Spots.Clear();
            Vector3 worstAt = Vector3.zero;
            var detail = new System.Text.StringBuilder();
            Bounds b = MapInfo.Bounds;
            const float step = 2f, auditDistance = 31f;
            for (int line = 0; line < 32; line++)
            {
                float f = ((line % 8) + 0.5f) / 8f;
                bool alongX = line < 16;
                bool back = (line / 8) % 2 == 1;
                Vector2 a = alongX ? new Vector2(b.min.x, Mathf.Lerp(b.min.z, b.max.z, f)) : new Vector2(Mathf.Lerp(b.min.x, b.max.x, f), b.min.z);
                Vector2 z = alongX ? new Vector2(b.max.x, a.y) : new Vector2(a.x, b.max.z);
                if (back) { Vector2 t = a; a = z; z = t; }

                int steps = Mathf.CeilToInt(Vector2.Distance(a, z) / step);
                float segment = Vector2.Distance(a, z) / steps, prev = float.NaN;
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 xz = Vector2.Lerp(a, z, s / (float)steps);
                    float y = HeightAt(xz, auditDistance);
                    near.Clear();
                    MapInfo.WalkableHeightsNear(new Vector3(xz.x, y, xz.y), 10f, near);
                    bool playable = near.Count > 0;
                    if (!float.IsNaN(prev) && playable)
                    {
                        float slope = Mathf.Abs(y - prev) / segment;
                        slopes.Add(slope);
                        if (slope > worst) { worst = slope; worstAt = new Vector3(xz.x, y, xz.y); }
                    }
                    prev = y;

                    if (playable)
                    {
                        total++;
                        float hi = float.MinValue, lo = float.MaxValue;
                        for (int i = 0; i < near.Count; i++) { hi = Mathf.Max(hi, near[i]); lo = Mathf.Min(lo, near[i]); }
                        if (y > hi + 10f) floating++;
                        if (y < lo - 10f) sunk++;
                    }

                    if (s % 4 != 0) continue;
                    for (int turn = 0; turn < 4; turn++)
                    {
                        float turnYaw = turn * 90f + 25f, low = Settings.CameraMinDistance.Value;

                        // the lowest view: the nearest distance, the lowest tilt
                        Vector3 lookAt = new Vector3(xz.x, HeightAt(xz, low) + 1f, xz.y);
                        float lift = GroundLift(lookAt, MinPitch, turnYaw, low);
                        Vector3 cam = lookAt - (Quaternion.Euler(Mathf.Min(SteepestPitch, MinPitch + lift), turnYaw, 0f) * Vector3.forward) * low;
                        bool stuck = FreeHeightAround(cam) > cam.y + 0.05f;
                        if (!playable)
                        {
                            otherViews++;
                            if (stuck) otherInGround++;
                            Vector3 overAt = new Vector3(xz.x, y + 1f, xz.y);
                            float overLift = GroundLift(overAt, Settings.CameraPitch.Value, turnYaw, auditDistance);
                            Vector3 over = overAt - (Quaternion.Euler(Mathf.Min(SteepestPitch, Settings.CameraPitch.Value + overLift), turnYaw, 0f) * Vector3.forward) * auditDistance;
                            if (FreeHeightAround(over) > over.y + 0.05f) otherUsualInGround++;
                            continue;
                        }
                        views++;
                        if (lift > 0.01f) { lifted++; liftSum += lift; }
                        if (stuck)
                        {
                            inGround++;
                            if (samples++ < 4) detail.Append($" [low {cam.x:0},{cam.y:0.0},{cam.z:0} focus {lookAt.y - 1f:0.0} {CameraGround.DescribeColumn(cam)}]");
                        }

                        // the usual view: the start distance, the start tilt
                        Vector3 usualAt = new Vector3(xz.x, y + 1f, xz.y);
                        float usualLift = GroundLift(usualAt, Settings.CameraPitch.Value, turnYaw, auditDistance);
                        Vector3 usual = usualAt - (Quaternion.Euler(Mathf.Min(SteepestPitch, Settings.CameraPitch.Value + usualLift), turnYaw, 0f) * Vector3.forward) * auditDistance;
                        usualViews++;
                        if (usualLift > 0.01f) { usualLifted++; usualLiftSum += usualLift; }
                        float usualDepth = FreeHeightAround(usual) - usual.y;
                        if (usualDepth > 0.05f)
                        {
                            usualInGround++;
                            usualDepthSum += usualDepth;
                            if (Spots.Count < 6 && playSpots++ % 7 == 0) Spots.Add(new Vector3(xz.x, turnYaw, xz.y));
                            if (samples++ < 8) detail.Append($" [usual {usual.x:0},{usual.y:0.0},{usual.z:0} focus {y:0.0} {CameraGround.DescribeColumn(usual)}]");
                        }
                    }
                }
            }
            hasAnchor = keepAnchor;

            slopes.Sort();
            float p95 = slopes.Count > 0 ? slopes[(int)(0.95f * (slopes.Count - 1))] : 0f;
            return $"where heroes walk: {total} points, height slope p95 {p95:0.00} worst {worst:0.00} (at {worstAt.x:0},{worstAt.y:0},{worstAt.z:0}), " +
                   $"{floating} more than 10 m above / {sunk} below all walkable ground near; " +
                   $"usual view: {usualViews}, {usualLifted} tilted up (mean {(usualLifted > 0 ? usualLiftSum / usualLifted : 0f):0.0} deg), " +
                   $"{usualInGround} look through rock (mean {(usualInGround > 0 ? usualDepthSum / usualInGround : 0f):0} m deep); " +
                   $"lowest view: {views}, {lifted} tilted up (mean {(lifted > 0 ? liftSum / lifted : 0f):0} deg), {inGround} look through rock; " +
                   $"elsewhere: usual view through rock {otherUsualInGround}, lowest {otherInGround} of {otherViews}; lift mode {LiftMode}, " +
                   $"field {CameraGround.Describe()}; spots {Spots.Count};{detail} " +
                   $"walkable heights {MapInfo.LowestWalkable:0}..{MapInfo.HighestWalkable:0}";
        }

        /// <summary>Places found by the last audit where the usual view is in the ground: x, z and the turn (y).</summary>
        private static readonly List<Vector3> Spots = new List<Vector3>(6);

        /// <summary>Testing (hc_cam spot): the usual view over one of the audit's places, at once.</summary>
        internal string GoToSpot(int index)
        {
            if (index < 0 || index >= Spots.Count) return $"no spot {index} (the last audit found {Spots.Count})";
            Vector3 s = Spots[index];
            FollowTarget = null;
            hasAnchor = false;
            yaw = smoothYaw = s.y;
            pitch = smoothPitch = Settings.CameraPitch.Value;
            distance = smoothDistance = 31f;
            focusXZ = ClampXZ(new Vector2(s.x, s.z));
            Snap();
            return $"spot {index}: {s.x:0},{s.z:0} yaw {s.y:0}, focus height {smoothFocus.y:0.0}";
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
