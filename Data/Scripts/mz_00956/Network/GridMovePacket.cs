using ProtoBuf;
using Sandbox.ModAPI;
using System;
using VRage.Game.Components;
using VRage.Utils;
using VRageMath;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game;

namespace mz_00956.ImprovisedEngineering
{
    // An example packet extending another packet.
    // Note that it must be ProtoIncluded in PacketBase for it to work.
    [ProtoContract]
    public class GridMovePacket : PacketBase
    {
        // tag numbers in this class won't collide with tag numbers from the base class
        [ProtoMember(1)]
        public long GridID;

        [ProtoMember(2)]
        public Vector3D HitPos = new Vector3D();

        [ProtoMember(3)]
        public double DesiredDistance;

        [ProtoMember(4)]
        public bool GrabCenter;

        [ProtoMember(5)]
        public Vector3D CurrentRotationInput = new Vector3D();

        [ProtoMember(6)]
        public bool Rotating;

        [ProtoMember(7)]
        public bool Holding;

        [ProtoMember(8)]
        public long PlayerIdentityId;

        [ProtoMember(9)]
        public long GridIDGround;
        [ProtoMember(10)]
        public Vector3D HandOffset;
        [ProtoMember(11)]
        public float AngularVelocityDamping;
        [ProtoMember(12)]
        public bool DisableInteractions;
        [ProtoMember(13)]
        public bool HoldingTool;
        [ProtoMember(14)]
        public bool LookingAtGrid;
        [ProtoMember(15)]
        public bool AlignedMode;
        [ProtoMember(16)]
        public long RelativeGridID;
        [ProtoMember(17)]
        public int FaceTowardsIndex;
        [ProtoMember(18)]
        public int FaceUpIndex;

        public ServerGridHandler serverGridHandler;

        public GridMovePacket() { } // Empty constructor required for deserialization

        public GridMovePacket(
            long gridID, 
            Vector3D hitPos, 
            double desiredDistance, 
            bool grabCenter, 
            Vector3D currentRotationInput, 
            bool rotating, 
            bool holding, 
            long playerIdentityId, 
            long gridIDGround,
            Vector3D handOffset,
            float angularVelocityDamping,
            bool disableInteractions,
            bool holdingTool,
            bool lookingAtGrid,
            bool alignedMode,
            long relativeGridID,
            int faceTowardsIndex,
            int faceUpIndex)
        {
            GridID = gridID;
            HitPos = hitPos;
            DesiredDistance = desiredDistance;
            GrabCenter = grabCenter;
            CurrentRotationInput = currentRotationInput;
            Rotating = rotating;
            Holding = holding;
            PlayerIdentityId = playerIdentityId;
            GridIDGround = gridIDGround;
            HandOffset = handOffset;
            AngularVelocityDamping = angularVelocityDamping;
            DisableInteractions = disableInteractions;
            HoldingTool = holdingTool;
            LookingAtGrid = lookingAtGrid;
            AlignedMode = alignedMode;
            RelativeGridID = relativeGridID;
            FaceTowardsIndex = faceTowardsIndex;
            FaceUpIndex = faceUpIndex;
        }

        public override bool Received()
        {
            if (MyAPIGateway.Utilities.IsDedicated || MyAPIGateway.Session.IsServer)
            {
                Debug.Log($"GridMovePacket.Received: Server received packed from a player {SenderId}", informUser: true);

                UpdateGrid(GridID, HitPos, DesiredDistance, GrabCenter, CurrentRotationInput, Rotating, Holding, 
                PlayerIdentityId, GridIDGround, HandOffset, AngularVelocityDamping, DisableInteractions, HoldingTool, 
                LookingAtGrid, AlignedMode, RelativeGridID, FaceTowardsIndex, FaceUpIndex, SenderId);
            }

            return false; // relay packet to other clients (only works if server receives it)
        }

        private void UpdateGrid(
            long gridID, 
            Vector3D hitPos, 
            double desiredDistance, 
            bool grabCenter, 
            Vector3D currentRotationInput, 
            bool rotating, 
            bool holding, 
            long playerIdentityId, 
            long gridIDGround, 
            Vector3D handOffset, 
            float angularVelocityDamping,
            bool disableInteractions,
            bool holdingTool,
            bool lookingAtGrid,
            bool alignedMode,
            long relativeGridID,
            int faceTowardsIndex,
            int faceUpIndex,
            ulong senderID)
        {
            bool blockInteraction = !holding || gridIDGround == -2 || !disableInteractions || holdingTool || !lookingAtGrid;
            MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(MyControlsSpace.USE.String, playerIdentityId, blockInteraction);
            MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(MyControlsSpace.PRIMARY_TOOL_ACTION.String, playerIdentityId, blockInteraction);
            MyVisualScriptLogicProvider.SetPlayerInputBlacklistState(MyControlsSpace.SECONDARY_TOOL_ACTION.String, playerIdentityId, blockInteraction);

            if (ServerGridHandler.grids.ContainsKey(gridID))
            {
                Debug.Log($"GridMovePacket.Received: Server updating grid {gridID} for player {senderID}");

                GridStatus grid = ServerGridHandler.grids[gridID];

                if (grid.playerStatuses.Any(x => x.PlayerID == senderID))
                {

                    if (holding)
                    {
                        Debug.Log($"GridMovePacket.Received: Player {senderID} is still holding grid {gridID}");
                        PlayerStatus player = grid.playerStatuses.Find(x => x.PlayerID == senderID);

                        player.HitPos = hitPos;
                        player.DesiredDistance = desiredDistance;
                        player.GrabCenter = grabCenter;
                        player.Torque = currentRotationInput;
                        player.RotationInput = rotating;
                        player.GridIDGround = gridIDGround;
                        player.HandOffset = handOffset;
                        player.AngularVelocityDamping = angularVelocityDamping;
                        player.AlignedMode = alignedMode;
                        player.RelativeGridID = relativeGridID;
                        player.FaceTowardsIndex = faceTowardsIndex;
                        player.FaceUpIndex = faceUpIndex;
                    }
                    else
                    {
                        Debug.Log($"GridMovePacket.Received: Player {senderID} released grid {gridID}");
                        grid.playerStatuses.Remove(grid.playerStatuses.Find(x => x.PlayerID == senderID));
                    }

                }
                else if (holding)
                {   
                    Debug.Log($"GridMovePacket.Received: Player {senderID} is picking up grid {gridID}");
                    grid.playerStatuses.Add(new PlayerStatus
                    {
                        HitPos = hitPos,
                        DesiredDistance = desiredDistance,
                        GrabCenter = grabCenter,
                        Torque = currentRotationInput,
                        RotationInput = rotating,
                        PlayerID = senderID,
                        GridIDGround = gridIDGround,
                        HandOffset = handOffset,
                        AngularVelocityDamping = angularVelocityDamping,
                        AlignedMode = alignedMode,
                        RelativeGridID = relativeGridID,
                        FaceTowardsIndex = faceTowardsIndex,
                        FaceUpIndex = faceUpIndex,
                    });
                }
                ServerGridHandler.grids[gridID] = grid;
            }
            else if (holding)
            {
                Debug.Log($"GridMovePacket.Received: Server picking up grid {gridID} for player {senderID}");

                ServerGridHandler.grids.Add(gridID, new GridStatus
                {
                    gridPosition = MyAPIGateway.Entities.GetEntityById(gridID).WorldMatrix.Translation,
                    playerStatuses = new List<PlayerStatus>
                    {
                        new PlayerStatus
                        {
                            HitPos = hitPos,      
                            DesiredDistance = desiredDistance,
                            GrabCenter = grabCenter,
                            Torque = currentRotationInput,
                            RotationInput = rotating,
                            PlayerID = senderID,
                            GridIDGround = gridIDGround,
                            HandOffset = handOffset,
                            AngularVelocityDamping = angularVelocityDamping,
                            AlignedMode = alignedMode,
                            RelativeGridID = relativeGridID,
                            FaceTowardsIndex = faceTowardsIndex,
                            FaceUpIndex = faceUpIndex,
                        }
                    },
                });
            }
        }
    }
}