using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using System.Collections.Generic;
using System.Linq;
using System;

namespace mz_00956.ImprovisedEngineering
{
    public struct PlayerStatus
    {
        public Vector3D HitPos;
        public double DesiredDistance;
        public bool GrabCenter;
        public double MaxForce;
        public Vector3D Force;
        public Vector3D Torque;
        public bool RotationInput;
        public ulong PlayerID;
        public long GridIDGround;
        public Vector3D HandOffset;
        public float AngularVelocityDamping;
        public int LastTicks;

        public Vector3D currentPos;
        public Vector3D desiredPos;
    }

    public struct GridStatus
    {
        public Vector3D gridPosition;
        public double gridMass;
        public List<PlayerStatus> playerStatuses;
    }


    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ServerGridHandler : MySessionComponentBase
    {
        public NetworkProtobuf Networking = new NetworkProtobuf(56162);

        public static Dictionary<long, GridStatus> grids = new Dictionary<long, GridStatus>();
        private readonly List<IMyPlayer> tempPlayers = new List<IMyPlayer>();

        public override void BeforeStart()
        {
            Networking.Register();
        }

        protected override void UnloadData()
        {
            Networking?.Unregister();
            Networking = null;
        }

        public override void UpdateAfterSimulation()
        {
            if (MyAPIGateway.Session == null)
                return;

            if (!MyAPIGateway.Utilities.IsDedicated && !MyAPIGateway.Session.IsServer)
                return;

            HandelGrabPhysics();
        }


        public void HandelGrabPhysics()
        {
            CalculateForces();

            InformPlayers();
        }

        private static int lastPlayerCount = 0;
        private static int lastGridCount = 0;

        private void CalculateForces()
        {
            tempPlayers.Clear();
            MyAPIGateway.Multiplayer?.Players?.GetPlayers(tempPlayers);

            for (int i = tempPlayers.Count - 1; i >= 0; i--)
            {
                IMyPlayer player = tempPlayers[i];
                //check if it is a NPC
                if (player.IsBot)
                    tempPlayers.Remove(player);
            }

            if (tempPlayers.Count != lastPlayerCount)
            {
                lastPlayerCount = tempPlayers.Count;
                Debug.Log($"ServerGridHandler.CalculateForces: Found {tempPlayers.Count} Players", informUser: true);
            }
            if (grids.Keys.Count != lastGridCount)
            {
                lastGridCount = grids.Keys.Count;
                Debug.Log($"ServerGridHandler.CalculateForces: Handling {grids.Keys.Count} Grids", informUser: true);
            }

            for (int a = grids.Keys.Count - 1; a >= 0; a--)
            {
                var gridID = grids.Keys.ElementAt(a);

                GridStatus gridStatus = grids[gridID];
                IMyCubeGrid heldGrid = MyAPIGateway.Entities.GetEntityById(gridID) as IMyCubeGrid;

                if (heldGrid == null || heldGrid.Closed || heldGrid.MarkedForClose || heldGrid.Physics == null)
                {
                    Debug.Log($"ServerGridHandler.CalculateForces: Grid {gridID} is no longer valid, removing from handled grids", informUser: true);
                    grids.Remove(gridID);
                    continue;
                }

                gridStatus.gridMass = heldGrid.Physics.Mass;
                gridStatus.gridPosition = heldGrid.WorldMatrix.Translation;
                
                for (int i = gridStatus.playerStatuses.Count - 1; i >= 0; i--)
                {
                    if (gridStatus.playerStatuses[i].LastTicks > Config.GrabTimeout / 15f)
                        gridStatus.playerStatuses.RemoveAt(i);
                    else
                    {
                        PlayerStatus player = gridStatus.playerStatuses[i];
                        player.LastTicks++;
                        gridStatus.playerStatuses[i] = player;
                    }
                }

                if (grids[gridID].playerStatuses.Count == 0)
                {
                    Debug.Log($"ServerGridHandler.CalculateForces: No players holding grid {gridID}, removing from handled grids", informUser: true);
                    grids.Remove(gridID);
                    continue;
                }

                grids[gridID] = gridStatus;

                Debug.Log($"ServerGridHandler.CalculateForces: Grid {gridID} held by {grids[gridID].playerStatuses.Count} players", informUser: grids[gridID].playerStatuses.Count > 0);

                for (int i = 0; i < grids[gridID].playerStatuses.Count; i++)
                {
                    PlayerStatus player = grids[gridID].playerStatuses[i];
                    IMyPlayer myPlayer = tempPlayers.Find(p => p.SteamUserId == player.PlayerID);

                    if (player.GridIDGround == -1)
                    {
                        MyCharacterDefinition characterDefinition = (MyCharacterDefinition)myPlayer.Character.Definition;
                        player.MaxForce = Config.FlyingForce;
                        Debug.Log($"ServerGridHandler.CalculateForces: Player {player.PlayerID} using Jetpack with MaxForce {player.MaxForce}", informUser: true);
                    }
                    else if (player.GridIDGround == -2 && grids[gridID].playerStatuses.Count == 1)
                    {
                        player.MaxForce = Config.ThrowForce;
                        Debug.Log($"ServerGridHandler.CalculateForces: Player {player.PlayerID} using Throw with MaxForce {player.MaxForce}", informUser: true);
                    }
                    else
                    {
                        player.MaxForce = Config.GroundForce;
                        Debug.Log($"ServerGridHandler.CalculateForces: Player {player.PlayerID} using Exoskelet(standing) with MaxForce {player.MaxForce}", informUser: true);
                    }

                    var maxForce = player.MaxForce;

                    MatrixD headMat = myPlayer.Character.GetHeadMatrix(true);
                    MatrixD heldGridMatrix = heldGrid.WorldMatrix;

                    Vector3D transformedOff = Vector3D.Transform(player.HitPos, heldGridMatrix) - heldGridMatrix.Translation;
                    player.desiredPos = headMat.Translation + (headMat.Forward * player.DesiredDistance);
                    player.currentPos = heldGridMatrix.Translation + transformedOff;

                    if (player.GrabCenter)
                        player.currentPos = heldGrid.Physics.CenterOfMassWorld;

                    var velocityPlayer = (Vector3D)myPlayer.Character.Physics.LinearVelocity;

                    // --- lead time based on distance ---
                    const double LEAD_DIST_MAX   = 4.0;   // m
                    const double LEAD_TIME_MAX   = 0.3;  // s

                    double distFactor = 1.0 - MathHelper.Clamp(player.DesiredDistance / LEAD_DIST_MAX, 0.0, 1.0);
                    double leadTime   = LEAD_TIME_MAX * distFactor;

                    // --- scale by grid mass (light grids: more glued, heavy: less) ---
                    const double MASS_REF       = 1000.0;
                    const double MASS_MIN_FACT  = 0.3;
                    const double MASS_MAX_FACT  = 1.0;

                    double mass       = grids[gridID].gridMass;   // or heldGrid.Physics.Mass
                    double massFactor = 1.0 / (1.0 + mass / MASS_REF);
                    massFactor        = MathHelper.Clamp(massFactor, MASS_MIN_FACT, MASS_MAX_FACT);

                    Vector3D leadOffset = (velocityPlayer - heldGrid.Physics.LinearVelocity) * leadTime * massFactor;

                    player.desiredPos += leadOffset;

                    // --- force boost

                    List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                    heldGrid.GetBlocks(blocks);
                    if (blocks.Count < Config.SmallGridBlockCount && player.GridIDGround != -2) // small block count boost
                    {
                        Debug.Log($"ServerGridHandler.CalculateForces: Small grid boost for grid {gridID} with {blocks.Count} blocks < 5", informUser: true);
                        maxForce *= Config.SmallGridBoostMulti;
                    }

                    if (player.HandOffset.Length() > 0 && player.GridIDGround != -2)
                    {
                        // distance where we start increasing stiffness
                        const float STIFF_START_DIST = 2.0f; // m
                        const float STIFF_FULL_DIST  = 1.0f; // m (your min distance clamp)

                        // max multiplier on the maxForce when very close
                        float stiff_max_mult   = Config.CloseToPlayerBoost; // tweak to 1.5–3.0 as needed

                        // t = 0 => far (no extra force), t = 1 => very close (full boost)
                        float t = MathHelper.Clamp(
                            (STIFF_START_DIST - (float)player.DesiredDistance) / (STIFF_START_DIST - STIFF_FULL_DIST),
                            0f, 1f
                        );

                        if (t > 0f)
                        {
                            float mult = MathHelper.Lerp(1f, stiff_max_mult, t);
                            Debug.Log($"ServerGridHandler.CalculateForces: Close range stiffening for player {player.PlayerID} with multiplier {mult:F2}", informUser: true);
                            maxForce *= mult;
                        }
                    }

                    var desiredPos = player.desiredPos + player.HandOffset;
                    Vector3D Pt0 = player.currentPos;
                    Vector3D Vt0 = heldGrid.Physics.LinearVelocity;
                    Vector3D rawForce = ((desiredPos - Pt0) * 10 - Vt0) * grids[gridID].gridMass * 25;
                    player.Force = Vector3D.ClampToSphere(rawForce, maxForce);

                    Debug.Log($"ServerGridHandler.CalculateForces: Applying force {player.Force.Length():F2} / {maxForce:F2} to grid {gridID} for player {player.PlayerID}", informUser: true);
                    heldGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, player.Force, player.currentPos, null);

                    IMyEntity gridGround;
                    if (player.GridIDGround != -1 && MyAPIGateway.Entities.TryGetEntityById(player.GridIDGround, out gridGround))
                    {
                        if (gridGround.Physics != null && gridGround is IMyCubeGrid)
                        {
                            Vector3D playerPos = tempPlayers.Find((p) => p.SteamUserId == player.PlayerID).GetPosition();
                            gridGround.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -player.Force, playerPos, null);
                        }
                    }

                    grids[gridID].playerStatuses[i] = player;
                }

                // Apply torques if only one player is holding
                if (grids[gridID].playerStatuses.Count == 1)
                {
                    var player = grids[gridID].playerStatuses[0];
                    var torque = player.Torque * player.MaxForce / 10.0;
                    Debug.Log($"ServerGridHandler.CalculateForces: Applying torque {torque.Length():F2} to grid {gridID} for player {player.PlayerID}", informUser: true);
                    heldGrid.Physics.AddForce(
                        MyPhysicsForceType.ADD_BODY_FORCE_AND_BODY_TORQUE,
                        Vector3D.Zero,
                        heldGrid.Physics.CenterOfMassWorld,
                        torque
                    );

                    if (torque == Vector3D.Zero && grids[gridID].playerStatuses[0].RotationInput)
                        heldGrid.Physics.AngularVelocity = heldGrid.Physics.AngularVelocity * 0.3f;

                    if (grids[gridID].playerStatuses[0].GridIDGround == -2)
                    {
                        grids.Remove(gridID);
                        continue;
                    }
                }
                if (grids[gridID].playerStatuses.Count >= 1) 
                    heldGrid.Physics.AngularVelocity = heldGrid.Physics.AngularVelocity * grids[gridID].playerStatuses.Average(p => p.AngularVelocityDamping);
            }
        }

        private void InformPlayers()
        {
            foreach (var grid in grids)
            {
                var gridStatus = grid.Value;
                var gridID = grid.Key;

                tempPlayers.Clear();
                MyAPIGateway.Multiplayer?.Players?.GetPlayers(tempPlayers);

                for (int i = tempPlayers.Count - 1; i >= 0; i--)
                {
                    var p = tempPlayers[i];
                    if (p.Character == null || Vector3D.Distance(p.GetPosition(), gridStatus.gridPosition) > Config.LineDist)
                        tempPlayers.RemoveAt(i);
                }

                if (tempPlayers.Count == 0 || gridStatus.playerStatuses.Count == 0)
                    continue;

                Vector3D[] holdingPoints = new Vector3D[gridStatus.playerStatuses.Count];
                Vector3D[] directions = new Vector3D[gridStatus.playerStatuses.Count];
                double[] tensions = new double[gridStatus.playerStatuses.Count];

                for (int i = 0; i < gridStatus.playerStatuses.Count; i++)
                {
                    PlayerStatus player = gridStatus.playerStatuses[i];
                    directions[i] = player.desiredPos + player.HandOffset - player.currentPos;
                    holdingPoints[i] = player.currentPos;
                    tensions[i] = Vector3D.DistanceSquared(player.desiredPos, player.currentPos) / Math.Pow(Config.Reach, 2);
                }

                foreach (var p in tempPlayers)
                {
                    double maxForce = gridStatus.playerStatuses.Find((pl) => pl.PlayerID == p.SteamUserId).MaxForce;
                    Networking.SendToPlayer(new GridInformationPacket(gridID, gridStatus.gridMass, maxForce, holdingPoints, directions, tensions), p.SteamUserId);
                }
            }
        }
    }
}