using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using System.Collections.Generic;
using System;
using VRage.Utils;
using VRage.Game;
using Sandbox.Game.Entities.Character.Components;
using VRage.Input;
using System.Linq;

namespace mz_00956.ImprovisedEngineering
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ClientGridHandler : MySessionComponentBase
    {


        // the ID in this must be unique between other mods.
        // usually suggested to be the last few numbers of your workshopId.
        public NetworkProtobuf Networking = new NetworkProtobuf(56161);
        public const int MAX_TORQUE = 10000;

        private IMyCubeGrid heldGrid;
        private IMyCubeGrid relativeGrid; // null = use camera
        private Vector3D hitPos;
        private int delay;
        private double desiredDistance;

        private int gridReleaseTimer;
        private int resetTimer;
        private bool blinking = false;

        private static readonly int[] OppositeFace = { 2, 4, 0, 5, 1, 3 };

        private bool grabCenter       = false;
        private bool alignToReference = false; // Shift toggle
        private bool rotateMode       = false; // LMB toggle
        private int  faceTowardsIndex = 0;     // +F
        private int  faceUpIndex      = 3;     // +U

        public struct GridHoldSettings
        {
            public bool GrabCenter;
            public bool AlignToReference;
            public bool RotateMode;
            public int  FaceTowardsIndex;
            public int  FaceUpIndex;
        }

        public static readonly Dictionary<long, GridHoldSettings> GridSettings =
            new Dictionary<long, GridHoldSettings>();

        private static HandHeldConfig activeConfig = null;

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent) 
        {
            MyAPIGateway.Utilities.MessageEnteredSender += Utilities_MessageReceived;
        }

        private static void Utilities_MessageReceived(ulong sender, string messageText, ref bool sendToOthers)
        {
            CommandHelper.HandleCommands(sender, messageText, ref sendToOthers);
        }

        public override void BeforeStart()
        {
            Networking.Register();
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Utilities.MessageEnteredSender -= Utilities_MessageReceived;
            Networking?.Unregister();
            Networking = null;
        }

        public override void UpdateAfterSimulation()
        {
            int validMode = ValidContext();
            if (validMode < 0)
            {
                if (heldGrid != null)
                {
                    Debug.Log($"ClientGridHandler.UpdateAfterSimulation: Context not valid, dropping grid", informUser: true);
                    DropGrid();
                }
                return;
            }

            if (delay-- > 0)
                return;

            ProcessUserInteraction(validMode);
        }

        private void ProcessUserInteraction(int validMode)
        {
            MatrixD headMat = MyAPIGateway.Session.Player.Character.GetHeadMatrix(true);

            if (GetInputDown(MyControlsSpace.RELOAD, force: true) && validMode > 0)
            {
                if (MyAPIGateway.Session.Player?.Character.EquippedTool != null && Config.GrabTool == null)
                {
                    Utils.Notify("'grabTool' config is not set. \n" + 
                    "Defaulting to FALSE. \n'/IME grabTool' to toggle it to your preference.", 3000, "Red");
                    return;
                }

                Debug.Log($"ClientGridHandler.ProcessUserInteraction: RELOAD action triggered (pickup/drop)", informUser: true);
                if (heldGrid != null)
                {
                    DropGrid();
                    return;
                }

                PickUpGrid(headMat);
            }

            if (heldGrid != null && IsMouse(MyMouseButtonsEnum.Middle) && validMode > 0)
            {
                UpdateRelativeGrid(headMat);
                return;
            }

            if (heldGrid != null && !heldGrid.Closed && heldGrid.Physics != null)
                MoveGrid(headMat, validMode);

            if(heldGrid == null)
                gridReleaseTimer = MathHelper.Clamp(gridReleaseTimer + 2, 0, Config.GrabTimeout);
        }

        private void DropGrid()
        {
            Debug.Log($"ClientGridHandler.DropGrid: Dropping grid", informUser: true);
            if (heldGrid != null)
            {
                try
                {
                    Networking.SendToServer(new GridMovePacket(
                        heldGrid.EntityId, 
                        Vector3D.Zero, 
                        0, 
                        false,
                        Vector3D.Zero, 
                        false, 
                        false, 
                        MyAPIGateway.Session.Player.IdentityId, 
                        -1,
                        Vector3D.Zero,
                        0f,
                        Config.LockUse,
                        MyAPIGateway.Session.Player?.Character.EquippedTool != null,
                        false
                    ));
                    Debug.Log($"ClientGridHandler.DropGrid: Sent drop packet to server");
                }
                catch (Exception e)
                {
                    Debug.Error("Error releasing grid: " + e);
                }
            }
            DropGridLocal();
        }

        private void PickUpGrid(MatrixD headMat)
        {
            List<IHitInfo> hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(headMat.Translation, headMat.Translation + headMat.Forward * Config.Reach, hits);
            Debug.Log($"ClientGridHandler.PickUpGrid: Hits found: {hits.Count}", informUser: true);
            IHitInfo hit = null;
            foreach (IHitInfo h in hits)
            {
                if (h != null && h.HitEntity != null && h.HitEntity is IMyCubeGrid)
                {
                    hit = h;
                    break;
                }
            }

            if (hit == null || hit.HitEntity == null || !(hit.HitEntity is IMyCubeGrid))
            {
                Debug.Log($"ClientGridHandler.PickUpGrid: No valid grid hit");
                return;
            }
            Debug.Log($"ClientGridHandler.PickUpGrid: {hit.HitEntity.Name} valid for pickup");

            IMyCubeGrid grid = hit.HitEntity as IMyCubeGrid;

            Vector3D dimensions = (grid.Max - grid.Min + Vector3.One) * grid.GridSize;
            double maxDimensions = Config.Reach * 2;

            if (grid.IsStatic || dimensions.X >= maxDimensions || dimensions.Y >= maxDimensions || dimensions.Z >= maxDimensions || grid.MarkedForClose || grid.Closed)
            {
                Debug.Log($"ClientGridHandler.PickUpGrid: {grid.Name} is too large or static to pick up");
                return;
            }

            heldGrid = grid;
            heldGrid.OnGridMerge += OnGridMerge;
            hitPos = Vector3D.Transform(hit.Position, heldGrid.PositionComp.WorldMatrixNormalizedInv);
            desiredDistance = (headMat.Translation - hit.Position).Length();
            MyVisualScriptLogicProvider.SetHighlightLocal(heldGrid.Name, 20, 0, Color.Yellow * .1f);

            GridHoldSettings state;
            if (GridSettings.TryGetValue(heldGrid.EntityId, out state))
            {
                grabCenter       = state.GrabCenter;
                alignToReference = state.AlignToReference;
                rotateMode       = state.RotateMode;
                faceTowardsIndex = state.FaceTowardsIndex;
                faceUpIndex      = state.FaceUpIndex;
            }
            else
            {
                grabCenter       = false;
                alignToReference = false;
                rotateMode       = false;
                faceTowardsIndex = 0;
                faceUpIndex      = 3;
            }

            Debug.Log($"ClientGridHandler.PickUpGrid: Picked up {heldGrid.Name}", informUser: true);

            activeConfig = HandHeldManager.TryGet(heldGrid, true);
            if (activeConfig != null)
            {
                Utils.Notify($"Holding Hand-Held Grid: {heldGrid.DisplayName}", 2000);
                faceTowardsIndex = activeConfig.FwdFace;
                faceUpIndex      = activeConfig.UpFace;
                grabCenter       = true;
                rotateMode       = true;
                alignToReference = true;
            }

            if (CommandHelper.newsVersion > Config.NewsVer)
            {
                Utils.Message("IME", CommandHelper.MESSAGE);
                Config.NewsVer = CommandHelper.newsVersion;
            }
        }

        private void MoveGrid(MatrixD headMat, int validMode)
        {
            if (activeConfig != null)
                HandHeldManager.HandleHandHeldInputs(activeConfig, validMode);

            if (GetInputDown(MyControlsSpace.SECONDARY_TOOL_ACTION) && validMode > 0)
            {
                grabCenter = !grabCenter;
                Debug.Log($"ClientGridHandler.MoveGrid: Toggled grabCenter to {grabCenter}", informUser: true);
                if (Config.ShowModes)
                    Utils.Notify(grabCenter
                        ? "Snap to Center: ON"
                        : "Snap to Center: OFF");
            }

            if (!Config.RotToggle)
            {
                rotateMode = GetInput(MyControlsSpace.PRIMARY_TOOL_ACTION) && validMode > 0;
                if (rotateMode)
                    Utils.Notify("Holding LMB to stop rotation", 100, "Orange"); // orange breaks it in such a way so that the messages stack inside of each other which saves space. xD
            } else if (GetInputDown(MyControlsSpace.PRIMARY_TOOL_ACTION) && validMode > 0)
            {
                rotateMode = !rotateMode;
                Debug.Log($"ClientGridHandler.MoveGrid: Toggled rotateMode to {grabCenter}", informUser: true);
                if (Config.ShowModes)
                    Utils.Notify(rotateMode
                        ? "Fixed Rotate: ON"
                        : "Fixed Rotate: OFF");
            }

            if (heldGrid.MarkedForClose || heldGrid.Closed)
            {
                Debug.Log($"ClientGridHandler.MoveGrid: heldGrid is closed, dropping", informUser: true);
                DropGrid();
                return;
            }

            desiredDistance = MathHelper.Clamp(desiredDistance + MyAPIGateway.Input.DeltaMouseScrollWheelValue() / 1000f, 1.2f, Config.Reach);

            MatrixD heldGridMatrix = heldGrid.WorldMatrix;            
            Vector3D transformedOff = Vector3D.Transform(hitPos, heldGridMatrix) - heldGridMatrix.Translation;
            Vector3D desiredPos = headMat.Translation + (headMat.Forward * desiredDistance);
            
            Vector3D handOffset = Vector3D.Zero;
            if (relativeGrid == null && Config.OffsetHand)
            {
                const float HAND_OFFSET_START_DIST = 2.5f;
                const float HAND_OFFSET_FULL_DIST  = 1.5f;
                const float HAND_OFFSET_DOWN       = 0.8f;
                const float HAND_OFFSET_RIGHT      = 0.5f;

                float t = MathHelper.Clamp(
                    (HAND_OFFSET_START_DIST - (float)desiredDistance) / (HAND_OFFSET_START_DIST - HAND_OFFSET_FULL_DIST),
                    0f, 1f
                );

                if (t > 0f)
                {
                    handOffset =
                        (-headMat.Up * HAND_OFFSET_DOWN + headMat.Right * HAND_OFFSET_RIGHT) * t;
                    desiredPos += handOffset;
                }
            }

            if (grabCenter && activeConfig != null)
            {
                var gridMatrix = heldGrid.WorldMatrix;
                var offsetWorld = Vector3D.TransformNormal(activeConfig.CoMOffsetLocal, gridMatrix);
                handOffset += offsetWorld;
            }

            Vector3D currentPos = heldGridMatrix.Translation + transformedOff;

            if (grabCenter)
                currentPos = heldGrid.Physics.CenterOfMassWorld;

            if ((desiredPos - currentPos).LengthSquared() > Math.Pow(Config.Reach, 2))
            {
                Debug.Log($"ClientGridHandler.MoveGrid: HoldLine stretched to far {(desiredPos - currentPos).LengthSquared()} > {Math.Pow(Config.Reach, 2)}. dropping grid", informUser: true);
                DropGrid();
                return;
            }

            if(Utils.TouchingPlayer(MyAPIGateway.Session.Player, heldGrid) && Config.GrabTimeout > 0)
            {
                gridReleaseTimer = MathHelper.Clamp(gridReleaseTimer - 15, 0, Config.GrabTimeout);
                Debug.Log($"ClientGridHandler.MoveGrid: heldGrid touching player. Dropping in {gridReleaseTimer} ticks");

                resetTimer = 20;

                if(!blinking)
                {
                    blinking = true;
                    MyVisualScriptLogicProvider.SetHighlightLocal(heldGrid.Name, 0, 0, Color.Yellow * .1f);
                    MyVisualScriptLogicProvider.SetHighlightLocal(heldGrid.Name, 20, 2, Color.Red * .2f);
                }

                if (gridReleaseTimer <= 0)
                {
                    Debug.Log($"ClientGridHandler.MoveGrid: heldGrid touched player too long, dropping", informUser: true);
                    DropGrid();
                    return;
                }
            }
            else
            {
                if(resetTimer <= 0)
                {
                    gridReleaseTimer = MathHelper.Clamp(gridReleaseTimer + 10, 0, Config.GrabTimeout);
                    
                    if(blinking)
                    {
                        blinking = false;
                        MyVisualScriptLogicProvider.SetHighlightLocal(heldGrid.Name, 0, 0, Color.Red * .1f);
                        MyVisualScriptLogicProvider.SetHighlightLocal(heldGrid.Name, 20, 0, Color.Yellow * .1f);
                    }
                }
                else
                    resetTimer--;
            }

            // ====== ROTATION / ALIGNMENT ======
            
            Vector3D torque = Vector3D.Zero;
            float dampening = 1f;
            bool rotating = false;

            if (rotateMode)
            {
                rotating = true;

                torque = Rotate(ref dampening, validMode);
                Debug.Log($"ClientGridHandler.MoveGrid: Rotate calculated torque: {torque}");
            }

            var surfaceEntity = Utils.OnSurface(MyAPIGateway.Session.Player);
            long surfaceEntityID = surfaceEntity != null ? surfaceEntity.EntityId : -1;

            // looking at held grid?

            List<IHitInfo> hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(headMat.Translation, headMat.Translation + headMat.Forward * Config.Reach, hits);
            IHitInfo hit = null;
            foreach (IHitInfo h in hits)
            {
                if (h != null && h.HitEntity != null && h.HitEntity is IMyCubeGrid)
                {
                    hit = h;
                    break;
                }
            }

            IMyCubeGrid foundGrid = null;
            if (hit != null && hit.HitEntity != null && hit.HitEntity is IMyCubeGrid)
            {
               foundGrid = hit.HitEntity as IMyCubeGrid;
            }

            bool lookingAtGrid = foundGrid == heldGrid;

            // ===================

            try
            {
                Networking.SendToServer(new GridMovePacket(
                    heldGrid.EntityId, 
                    hitPos, 
                    desiredDistance, 
                    grabCenter, 
                    torque, 
                    rotating, 
                    true, 
                    MyAPIGateway.Session.Player.IdentityId, 
                    surfaceEntityID,
                    handOffset,
                    dampening,
                    Config.LockUse,
                    MyAPIGateway.Session.Player?.Character.EquippedTool != null,
                    lookingAtGrid
                ));
                Debug.Log($"ClientGridHandler.MoveGrid: Sent move&rotate packet to server");
            }
            catch (Exception e)
            {
                Debug.Error("Error moving grid: " + e);
            }

            if (GetInput(MyControlsSpace.PRIMARY_TOOL_ACTION) && GetInputDown(MyControlsSpace.SECONDARY_TOOL_ACTION))
            {
                try 
                {
                    Networking.SendToServer(new GridMovePacket(
                        heldGrid.EntityId, 
                        hitPos, 
                        200, 
                        grabCenter, 
                        Vector3D.Zero, 
                        rotating, 
                        true, 
                        MyAPIGateway.Session.Player.IdentityId, 
                        -2,
                        handOffset,
                        dampening,
                        Config.LockUse,
                        MyAPIGateway.Session.Player?.Character.EquippedTool != null,
                        lookingAtGrid
                    ));
                    Debug.Log($"ClientGridHandler.MoveGrid: Sent throw packet to server");
                    DropGridLocal();
                }
                catch (Exception e)
                {
                    Debug.Error("Error throwing grid: " + e);
                }
            }
        }

        private void OnGridMerge(IMyCubeGrid mergedGrid, IMyCubeGrid otherGrid)
        {
            Debug.Log($"ClientGridHandler.OnGridMerge: Held grid triggered OnGridMerge", informUser: false);
            if (heldGrid != null && ( otherGrid.EntityId == heldGrid.EntityId || mergedGrid.EntityId == heldGrid.EntityId))
            {
                Debug.Log($"ClientGridHandler.OnGridMerge: {heldGrid.Name} merged, dropping held grid", informUser: true);
                DropGrid();
            }
        }
        
        private void DropGridLocal()
        {
            Debug.Log($"ClientGridHandler.DropGridLocal: Dropping grid locally", informUser: true);

            if (heldGrid != null)
            {
                GridSettings[heldGrid.EntityId] = new GridHoldSettings
                {
                    GrabCenter       = grabCenter,
                    AlignToReference = alignToReference,
                    RotateMode       = rotateMode,
                    FaceTowardsIndex = faceTowardsIndex,
                    FaceUpIndex      = faceUpIndex
                };
                heldGrid.OnGridMerge -= OnGridMerge;

                MyVisualScriptLogicProvider.SetHighlightLocal(heldGrid.Name, 0, 0, Color.Yellow * .1f);

                if (relativeGrid != null)
                {
                    MyVisualScriptLogicProvider.SetHighlightLocal(relativeGrid.Name, 0, 0, Color.Blue * .1f);
                    relativeGrid = null;
                }

                heldGrid = null;
                alignToReference = false;
                grabCenter = false;
                rotateMode = false;
                delay = 30;
            }
        }

        private int ValidContext()
        {
            int ableToGrab = 1;

            if (MyAPIGateway.Utilities.IsDedicated)
                return -1;

            if (MyAPIGateway.Session == null)
                return -1;

            if (MyAPIGateway.Session.Player?.Character == null)
                return -1;

            if (MyAPIGateway.Session.IsCameraUserControlledSpectator)
                return -1;

            if (MyAPIGateway.Session.ControlledObject as IMyCharacter == null)
                return -1;

            // List<MyKeys> pressedKeys = new List<MyKeys>();
            // MyAPIGateway.Input.GetPressedKeys(pressedKeys);
            // Utils.Notify(string.Join("\n", pressedKeys.Select(k => k.ToString())), 100, "Orange");

            if (MyAPIGateway.Session.Player?.Character.EquippedTool != null)
            {
                if (Config.GrabTool != null && !Config.GrabToolSetting)
                    return -1;
            }

            if (MyAPIGateway.Gui.ChatEntryVisible)
            {
                if (!Config.HoldUi)
                    return -1;
                ableToGrab = 0;
            }

            if (MyAPIGateway.Gui.IsCursorVisible)
            {
                if (!Config.HoldUi)
                    return -1;
                ableToGrab = 0;
            }

            if (MyAPIGateway.Gui.GetCurrentScreen != MyTerminalPageEnum.None)
            {
                if (!Config.HoldUi)
                    return -1;
                ableToGrab = 0;
            }

            return ableToGrab;
        }

        private void UpdateRelativeGrid(MatrixD headMat)
        {
            if (relativeGrid != null)
            {
                MyVisualScriptLogicProvider.SetHighlightLocal(relativeGrid.Name, 0, 0, Color.Blue * .1f);
                relativeGrid = null;
            }

            List<IHitInfo> hits = new List<IHitInfo>();
            MyAPIGateway.Physics.CastRay(headMat.Translation, headMat.Translation + headMat.Forward * 20f, hits);
            Debug.Log($"ClientGridHandler.UpdateRelativeGrid: Hits found: {hits.Count}", informUser: true);

            if (hits == null || hits.Count == 0)
                return;

            IHitInfo hit = null;
            foreach (var h in hits)
            {
                if (h.HitEntity == null || !(h.HitEntity is IMyCubeGrid))
                    continue;

                var grid = h.HitEntity as IMyCubeGrid;
                if (grid == null)
                    continue;

                if (heldGrid != null && grid.EntityId == heldGrid.EntityId)
                    continue;

                hit = h;
                break;
            }

            if (hit == null)
            {
                Debug.Log($"ClientGridHandler.UpdateRelativeGrid: No valid grid hit for relativeGrid", informUser: true);
                return;
            }

            var g = (IMyCubeGrid)hit.HitEntity;
            Vector3 dims = (g.Max - g.Min + Vector3.One) * g.GridSize;

            relativeGrid = g;
            Debug.Log($"ClientGridHandler.UpdateRelativeGrid: Set relative grid to {relativeGrid.Name}", informUser: true);
            if (dims.X < 20 && dims.Y < 20 && dims.Z < 20)
                MyVisualScriptLogicProvider.SetHighlightLocal(relativeGrid.Name, 20, 0, Color.Blue * .1f);
        }

        private bool GetInputDown(MyStringId keyType, bool force = false)
        {
            List<MyKeys> keys;
            MyMouseButtonsEnum? mouseButton;
            ResolveControls(keyType, out keys, out mouseButton);

            foreach (var key in keys)
            {
                if (IsKeyDown(key, force))
                    return true;
            }

            if (mouseButton.HasValue && IsMouseDown(mouseButton.Value, force))
                return true;

            return false;
        }

        private bool GetInput(MyStringId keyType, bool force = false)
        {
            List<MyKeys> keys;
            MyMouseButtonsEnum? mouseButton;
            ResolveControls(keyType, out keys, out mouseButton);

            foreach (var key in keys)
            {
                if (IsKey(key, force))
                    return true;
            }

            if (mouseButton.HasValue && IsMouse(mouseButton.Value, force))
                return true;

            return false;
        }

        private void ResolveControls(MyStringId keyType, out List<MyKeys> keys, out MyMouseButtonsEnum? mouseButton)
        {
            keys = new List<MyKeys>();

            var gameControl = MyAPIGateway.Input.GetGameControl(keyType);
            if (gameControl == null)
            {
                mouseButton = null;
                return;
            }

            var primaryKey = gameControl.GetKeyboardControl();
            if (primaryKey != MyKeys.None)
                keys.Add(primaryKey);

            if (Config.Keyboard2)
            {
                var secondaryKey = gameControl.GetSecondKeyboardControl();
                if (secondaryKey != MyKeys.None)
                    keys.Add(secondaryKey);
            }

            var mouse = gameControl.GetMouseControl();
            mouseButton = mouse != MyMouseButtonsEnum.None ? mouse : (MyMouseButtonsEnum?)null;
        }

        private bool IsKeyDown(MyKeys key, bool force = false)
        {
            if (!IsKeyAllowed(key, force))
                return false;

            return MyAPIGateway.Input.IsNewKeyPressed(key);
        }

        private bool IsKey(MyKeys key, bool force = false)
        {
            if (!IsKeyAllowed(key, force))
                return false;

            return MyAPIGateway.Input.IsKeyPress(key);
        }

        private bool IsMouseDown(MyMouseButtonsEnum button, bool force = false)
        {
            if (!IsMouseAllowed(button, force))
                return false;

            return MyAPIGateway.Input.IsNewMousePressed(button);
        }

        private bool IsMouse(MyMouseButtonsEnum button, bool force = false)
        {
            if (!IsMouseAllowed(button, force))
                return false;

            return MyAPIGateway.Input.IsMousePressed(button);
        }

        private bool IsKeyAllowed(MyKeys key, bool force)
        {
            if (force || activeConfig == null)
                return true;

            return !activeConfig.DisabledKeys.Contains(key);
        }

        private bool IsMouseAllowed(MyMouseButtonsEnum button, bool force)
        {
            if (force || activeConfig == null)
                return true;

            // Map mouse button to its MyKeys equivalent used in DisabledKeys
            MyKeys asKey;
            switch (button)
            {
                case MyMouseButtonsEnum.Left:
                    asKey = MyKeys.LeftButton;
                    break;
                case MyMouseButtonsEnum.Right:
                    asKey = MyKeys.RightButton;
                    break;
                case MyMouseButtonsEnum.Middle:
                    asKey = MyKeys.MiddleButton;
                    break;
                case MyMouseButtonsEnum.XButton1:
                    asKey = MyKeys.ExtraButton1;
                    break;
                case MyMouseButtonsEnum.XButton2:
                    asKey = MyKeys.ExtraButton2;
                    break;
                default:
                    // any other mouse buttons you care about → extend here
                    return true;
            }

            return !activeConfig.DisabledKeys.Contains(asKey);
        }

        private bool AnyKeyDown(IEnumerable<MyKeys> keys, bool force = false)
        {
            foreach (var key in keys)
                if (IsKeyDown(key, force))
                    return true;
            return false;
        }

        private bool AnyKey(IEnumerable<MyKeys> keys, bool force = false)
        {
            foreach (var key in keys)
                if (IsKey(key, force))
                    return true;
            return false;
        }

        #region Rotate

        private static readonly MyStringId[] _RotationControls = {
            MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE,
            MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE,
            MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE,
            MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE,
            MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE,
            MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE
        };

        private Vector3D Rotate(ref float angularVelocityDamping, int validMode) {
            var directions = ResolveDirections();

            var torque = Vector3D.Zero;
            for (var i = 0; i < _RotationControls.Length; i++) {
                if (GetInput(_RotationControls[i])) {
                    torque += directions[i] * MAX_TORQUE;
                }
            }

            // (could use sprint controls to make it more configurable, but you never know what player set for sprint)
            if (IsKeyDown(MyKeys.Shift) && validMode > 0 && !MyAPIGateway.Input.IsKeyPress(MyKeys.W))
            {
                alignToReference = !alignToReference;
                Debug.Log($"ClientGridHandler.Rotate: Toggled alignToReference to {alignToReference}", informUser: true);
                if (Config.ShowModes)
                    Utils.Notify(alignToReference 
                        ? "Alignment: ON" 
                        : "Alignment: OFF");
            }

            if (alignToReference)
                torque += ComputeFaceAlignTorqueLocal(faceTowardsIndex, faceUpIndex, ref angularVelocityDamping) * MAX_TORQUE;

            if (IsKeyDown(MyKeys.Alt) && validMode > 0)
            {
                faceUpIndex = faceTowardsIndex;
                faceTowardsIndex = (faceTowardsIndex + 1) % 6;
            }

            if ((IsKeyDown(MyKeys.Control) && validMode > 0) || faceUpIndex == faceTowardsIndex || faceUpIndex == OppositeFace[faceTowardsIndex])
            {
                var ring = FaceUpRing[faceTowardsIndex];
                int pos  = Array.IndexOf(ring, faceUpIndex);
                if (pos < 0)
                    faceUpIndex = ring[0];
                else
                    faceUpIndex = ring[(pos + 1) % ring.Length];
            }

            if (validMode > 0 && (IsKeyDown(MyKeys.Alt) || IsKeyDown(MyKeys.Control)))
            {
                Debug.Log($"ClientGridHandler.Rotate: Now facing {faceTowardsIndex} and up {faceUpIndex}", informUser: true);
            }

            DrawRelativeCameraLine();

            heldGrid.Physics.AngularVelocity = heldGrid.Physics.AngularVelocity * 0.3f;
            angularVelocityDamping *= 0.9f;
            return torque;
        }

        private void DrawRelativeCameraLine()
        {
            if (relativeGrid == null || heldGrid == null || heldGrid.Physics == null)
                return;

            MatrixD gridMatrix = heldGrid.WorldMatrix;
            Vector3D axisWorld = GetFaceDirWorld(gridMatrix, faceTowardsIndex);

            if (axisWorld.LengthSquared() < 1e-6f)
                return;

            Vector3D center = heldGrid.Physics.CenterOfMassWorld;

            Vector3D dims = ((Vector3D)(heldGrid.Max - heldGrid.Min) + Vector3D.One) * heldGrid.GridSize;
            double maxDim = Math.Max(dims.X, Math.Max(dims.Y, dims.Z));

            double lineLength = maxDim * 1.5f;
            Vector3D end = center + axisWorld * lineLength;

            Visualization.DrawLineDirect(center, end, 0, 0, 255);
        }

        const float DEADZONE_DEG = 3f;

        private Vector3D ComputeFaceAlignTorqueLocal(int faceTowardsIndex, int faceUpIndex, ref float angularVelocityDamping)
        {
            if (heldGrid?.Physics == null)
                return Vector3D.Zero;

            // Reference frame: camera or relative grid
            MatrixD refMatrix;
            bool haveRelative = relativeGrid != null && !relativeGrid.Closed && !relativeGrid.MarkedForClose;

            if (haveRelative)
                refMatrix = relativeGrid.WorldMatrix;
            else
                refMatrix = MyAPIGateway.Session.Player.Character.GetHeadMatrix(true);

            var gridMatrix = heldGrid.WorldMatrix;
            var gridRot    = (Matrix)gridMatrix;
            var invGridRot = Matrix.Transpose(gridRot); // world -> grid-local

            // ---- current face directions in WORLD (same as before) ----
            Vector3D curT_world = GetFaceDirWorld(gridMatrix, faceTowardsIndex);
            Vector3D curU_world = GetFaceDirWorld(gridMatrix, faceUpIndex);

            if (curT_world.LengthSquared() < 1e-6f || curU_world.LengthSquared() < 1e-6f)
                return Vector3D.Zero;

            curT_world = Vector3D.Normalize(curT_world);
            curU_world = Vector3D.Normalize(curU_world);

            // make U orthogonal to T
            curU_world = Vector3D.Normalize(curU_world - curT_world * Vector3D.Dot(curT_world, curU_world));
            Vector3D curR_world = Vector3D.Normalize(Vector3D.Cross(curT_world, curU_world));

            // ---- desired directions in WORLD (same as before) ----
            Vector3D desT_world = -Vector3D.Normalize(refMatrix.Forward);
            Vector3D desU_world =  Vector3D.Normalize(refMatrix.Up);

            desU_world = Vector3D.Normalize(desU_world - desT_world * Vector3D.Dot(desT_world, desU_world));
            Vector3D desR_world = Vector3D.Normalize(Vector3D.Cross(desT_world, desU_world));

            // ---- transform everything to GRID-LOCAL ----
            Vector3D curT_local = Vector3D.TransformNormal(curT_world, invGridRot);
            Vector3D curU_local = Vector3D.TransformNormal(curU_world, invGridRot);
            Vector3D curR_local = Vector3D.TransformNormal(curR_world, invGridRot);

            Vector3D desT_local = Vector3D.TransformNormal(desT_world, invGridRot);
            Vector3D desU_local = Vector3D.TransformNormal(desU_world, invGridRot);
            Vector3D desR_local = Vector3D.TransformNormal(desR_world, invGridRot);

            // ---- build torque directly in LOCAL space ----
            Vector3D torqueLocal = Vector3D.Zero;

            var x = AddAxisTorqueLocal(curR_local, desR_local);
            var y = AddAxisTorqueLocal(curU_local, desU_local);
            var z = AddAxisTorqueLocal(curT_local, desT_local);

            Visualization.DrawLine(heldGrid.Physics.CenterOfMassWorld, curR_world * 1.2, 255, 0, 0);
            Visualization.DrawLine(heldGrid.Physics.CenterOfMassWorld + curR_world * 1.2, curR_world * x.Length(), 0, 0, 0);
            Visualization.DrawLine(heldGrid.Physics.CenterOfMassWorld + desR_world * 1.2, desR_world * 0.5, 255, 0, 0);
            Visualization.DrawLine(heldGrid.Physics.CenterOfMassWorld, curU_world * 1.2, 0, 255, 0);
            Visualization.DrawLine(heldGrid.Physics.CenterOfMassWorld + curU_world * 1.2, curU_world * y.Length(), 0, 0, 0);
            Visualization.DrawLine(heldGrid.Physics.CenterOfMassWorld + desU_world * 1.2, desU_world * 0.5, 0, 255, 0);
            Visualization.DrawLine(heldGrid.Physics.CenterOfMassWorld, curT_world * 1.2, 0, 0, 255);
            Visualization.DrawLine(heldGrid.Physics.CenterOfMassWorld + curT_world * 1.2, curT_world * z.Length(), 0, 0, 0);
            Visualization.DrawLine(heldGrid.Physics.CenterOfMassWorld + desT_world * 1.2, desT_world * 0.5, 0, 0, 255);

            torqueLocal = x + y + z;
            torqueLocal /= 3;

            if (torqueLocal.LengthSquared() < 1e-6f) // keep your heuristic
            {
                heldGrid.Physics.AngularVelocity *= 0.3f;
                angularVelocityDamping          *= 0.3f;
                return Vector3D.Zero;
            }

            // --- MASS-BASED STRENGTH SCALING, still local ---
            float mass = (float)heldGrid.Physics.Mass;

            const float MASS_REF         = 1000f; // "reference mass" where scaling ~0.5
            const float MIN_MASS_FACTOR  = 0.5f;
            const float MAX_MASS_FACTOR  = 1.0f;

            float massFactor = 1f / (1f + mass / MASS_REF);
            massFactor = MathHelper.Clamp(massFactor, MIN_MASS_FACTOR, MAX_MASS_FACTOR);

            torqueLocal *= massFactor;

            return torqueLocal;
        }

        // operates entirely in GRID-LOCAL space
        private Vector3D AddAxisTorqueLocal(Vector3D curLocal, Vector3D desLocal)
        {
            if (curLocal.LengthSquared() < 1e-6f || desLocal.LengthSquared() < 1e-6f)
                return Vector3D.Zero;

            curLocal = Vector3D.Normalize(curLocal);
            desLocal = Vector3D.Normalize(desLocal);


            double dot = MathHelper.Clamp(Vector3D.Dot(curLocal, desLocal), -1f, 1f);
            float angleDeg = MathHelper.ToDegrees((float)Math.Acos(dot));

            Utils.Message($"X:{curLocal.X:0.00},  Y:{curLocal.Y:0.00}, Z:{curLocal.Z:0.00} dot X:{desLocal.X:0.00},  Y:{desLocal.Y:0.00}, Z:{desLocal.Z:0.00} = {dot:0.00}, angle = {angleDeg:0.00} deg");

            if (angleDeg <= DEADZONE_DEG)
                return Vector3D.Zero;

            // rotation axis IN LOCAL SPACE
            Vector3D axisLocal = Vector3D.Cross(curLocal, desLocal);
            if (axisLocal.LengthSquared() < 1e-6f)
                return Vector3D.Zero;

            // axisLocal = Vector3D.Normalize(axisLocal);

            // you can scale by angleDeg if you want "stronger when farther away",
            // but for now keep magnitude ~1 like your original code:
            return axisLocal;
        }


        private static Vector3D GetFaceDirWorld(MatrixD gridMatrix, int faceIndex)
        {
            switch (faceIndex)
            {
                case 0: return Vector3D.Normalize(gridMatrix.Forward);        // +F
                case 1: return Vector3D.Normalize(gridMatrix.Right);          // +R
                case 2: return -Vector3D.Normalize(gridMatrix.Forward);       // -F
                case 3: return Vector3D.Normalize(gridMatrix.Up);             // +U
                case 4: return -Vector3D.Normalize(gridMatrix.Right);         // -R
                case 5: return -Vector3D.Normalize(gridMatrix.Up);            // -U
                default: return Vector3D.Zero;
            }
        }

        private static readonly int[][] FaceUpRing =
        {
            // towards 0: +F → U+, R+, U-, R-
            new[] { 3, 1, 5, 4 }, // 0
            // towards 1: +R → U+, F-, U-, F+
            new[] { 3, 2, 5, 0 }, // 1
            // towards 2: -F → U+, R-, U-, R+
            new[] { 3, 4, 5, 1 }, // 2
            // towards 3: +U → F+, R-, F-, R+
            new[] { 0, 4, 2, 1 }, // 3
            // towards 4: -R → U+, F+, U-, F-
            new[] { 3, 0, 5, 2 }, // 4
            // towards 5: -U → F+, R+, F-, R-
            new[] { 0, 1, 2, 4 }, // 5
        };

        private Vector3D[] ResolveDirections()
        {
            var cameraMatrix = MyAPIGateway.Session.Player.Character.GetHeadMatrix(true);
            var gridMatrix   = heldGrid.WorldMatrix;

            var gridAxes = new List<AxisInfo> {
                new AxisInfo(Vector3D.Forward, gridMatrix.Forward),
                new AxisInfo(Vector3D.Up,      gridMatrix.Up),
                new AxisInfo(Vector3D.Right,   gridMatrix.Right)
            };

            var result = new Vector3D[6];
            result[0] = GetBestAxis(gridAxes, cameraMatrix.Right);
            result[1] = result[0] * -1;
            result[2] = GetBestAxis(gridAxes, cameraMatrix.Down);
            result[3] = result[2] * -1;
            result[4] = GetBestAxis(gridAxes, cameraMatrix.Forward);
            result[5] = result[4] * -1;
            return result;
        }

        private static Vector3D GetBestAxis(List<AxisInfo> gridAxes, Vector3D fitVector)
        {
            var minValue  = double.MaxValue;
            var bestAxis  = Vector3D.Zero;
            var direction = 0;

            foreach (var axis in gridAxes)
            {
                double dot   = Vector3D.Dot(fitVector, axis.Direction);
                double value = 1.0 - Math.Abs(dot);
                if (value < minValue)
                {
                    minValue = value;
                    bestAxis = axis.Axis;
                    direction = Math.Sign(dot);
                }
            }

            gridAxes.RemoveAll(a => a.Axis == bestAxis);
            return bestAxis * direction;
        }

        private struct AxisInfo
        {
            public AxisInfo(Vector3D axis, Vector3D direction)
            {
                Axis = axis;
                Direction = direction;
            }

            public Vector3D Axis      { get; }
            public Vector3D Direction { get; }
        }

        #endregion
    }
}