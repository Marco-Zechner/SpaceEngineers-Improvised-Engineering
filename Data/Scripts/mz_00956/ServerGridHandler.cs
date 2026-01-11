using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using System.Collections.Generic;
using System.Linq;
using System;
using VRage.Input;
using Digi;

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
        public bool AlignedMode;
        public long RelativeGridID;
        public int FaceTowardsIndex;
        public int FaceUpIndex;
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

    public class GridPhysicsCache
    {
        public MatrixD inertiaBody;     // 3x3 inertia tensor (local)
        public MatrixD inertiaBodyInv;  // inverse
        public double inertiaScalar;    // (Ixx+Iyy+Izz)/3
        public Vector3D comLocal;       // local CoM at time of calculation
        public double furthestRadius;   // radius containing all blocks
        public Vector3D furthestCornerLocal;
        
        public bool valid;
    } 

    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ServerGridHandler : MySessionComponentBase
    {
        public NetworkProtobuf Networking = new NetworkProtobuf(56162);

        public static Dictionary<long, GridStatus> grids = new Dictionary<long, GridStatus>();
        private readonly Dictionary<long, GridPhysicsCache> inertiaCache = new Dictionary<long, GridPhysicsCache>();
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
                    var torque = player.Torque;

                    GridPhysicsCache cache;
                    if (!inertiaCache.TryGetValue(heldGrid.EntityId, out cache) || cache == null || !cache.valid)
                    {
                        cache = ComputeGridInertia(heldGrid);
                        inertiaCache[heldGrid.EntityId] = cache;

                        Log.Info($"[INERTIA] Recompute (missing/invalid) for grid={heldGrid.EntityId}");
                    }
                    else
                    {
                        // Check if CoM changed significantly
                        var currentComLocal = heldGrid.Physics.CenterOfMassLocal;
                        double comDiff = (cache.comLocal - currentComLocal).LengthSquared();
                        if (comDiff > 1e-6)
                        {
                            Log.Info($"[INERTIA] Recompute (CoM changed) for grid={gridID} oldCom={cache.comLocal} newCom={currentComLocal} diffSq={comDiff:E3}");
                            cache = ComputeGridInertia(heldGrid);
                            inertiaCache[gridID] = cache;
                        }
                    }

                    if (cache == null || !cache.valid)
                    {
                        Log.Info($"[INERTIA] FAILED to compute valid inertia for grid={gridID}, skipping align torque.");
                    }
                    else
                    {
                        Log.Info($"[INERTIA] grid={gridID} Iavg={cache.inertiaScalar:F2} CoMlocal={cache.comLocal} furthestR={cache.furthestRadius:F2}");
                    }

                    if (player.AlignedMode)
                    {
                        var relativeGrid = MyAPIGateway.Entities.GetEntityById(player.RelativeGridID) as IMyCubeGrid;

                        var alignTorque = ComputeFaceAlignTorqueLocal(
                            heldGrid,
                            relativeGrid,
                            player.FaceTowardsIndex,
                            player.FaceUpIndex
                        );

                        Log.Info($"[ALIGN] grid={gridID} player={player.PlayerID} baseTorque={torque} alignTorque={alignTorque}");

                        torque += alignTorque;
                    }
                    Log.Info($"[TORQUE_APPLY] grid={gridID} player={player.PlayerID} finalTorque={torque} |ω|={heldGrid.Physics.AngularVelocity.Length():F3}");

                    Debug.Log($"ServerGridHandler.CalculateForces: Applying torque {torque.Length():F2} to grid {gridID} for player {player.PlayerID}", informUser: true);

                    heldGrid.Physics.AddForce(
                        MyPhysicsForceType.ADD_BODY_FORCE_AND_BODY_TORQUE,
                        Vector3D.Zero,
                        heldGrid.Physics.CenterOfMassWorld,
                        torque
                    );

                    if (torque == Vector3D.Zero && grids[gridID].playerStatuses[0].RotationInput)
                    {
                        heldGrid.Physics.AngularVelocity = heldGrid.Physics.AngularVelocity * 0.3f;
                        Log.Info($"[TORQUE_APPLY] grid={gridID} player={player.PlayerID} zeroTorque + RotationInput => damping angular velocity by 0.3");
                    }

                    if (grids[gridID].playerStatuses[0].GridIDGround == -2)
                    {
                        Log.Info($"[TORQUE_APPLY] grid={gridID} throw mode finished, removing grid from tracking.");
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

        private Vector3D ComputeFaceAlignTorqueLocal(
            IMyCubeGrid heldGrid,
            IMyCubeGrid relativeGrid,
            int faceTowardsIndex,
            int faceUpIndex
        )
        {
            if (heldGrid?.Physics == null)
                return Vector3D.Zero;

            long gridId = heldGrid.EntityId;

            // --- inertia cache ---
            GridPhysicsCache cache;
            if (!inertiaCache.TryGetValue(gridId, out cache) || !cache.valid)
            {
                cache = ComputeGridInertia(heldGrid);
                inertiaCache[gridId] = cache;
                Log.Info($"[ALIGN] Recomputed inertia for grid={gridId} Iavg={cache.inertiaScalar:F3}");
            }

            MatrixD Ibody    = cache.inertiaBody;
            double  Iavg     = Math.Max(cache.inertiaScalar, 1e-6);
            double  furthestR = Math.Max(cache.furthestRadius, 0.1);

            // --- reference rotation (what we want to align to) ---
            MatrixD refMat;
            if (relativeGrid != null && !relativeGrid.Closed && !relativeGrid.MarkedForClose)
                refMat = relativeGrid.WorldMatrix;
            else
                refMat = MyAPIGateway.Session.Player.Character.GetHeadMatrix(true);

            // world target directions for "towards" and "up"
            Vector3D desTowardsWorld = -refMat.Forward; // look where ref looks
            Vector3D desUpWorld      =  refMat.Up;

            desTowardsWorld.Normalize();
            // project Up onto plane orthogonal to Towards, re-normalize
            desUpWorld = Vector3D.Normalize(desUpWorld - desTowardsWorld * Vector3D.Dot(desTowardsWorld, desUpWorld));
            Vector3D desRightWorld = Vector3D.Normalize(Vector3D.Cross(desTowardsWorld, desUpWorld));

            // --- which LOCAL axes on the grid should align to these world dirs? ---
            Vector3D localTowards = GetLocalAxisForFace(faceTowardsIndex);
            Vector3D localUp      = GetLocalAxisForFace(faceUpIndex);

            // Build a "desired orientation" matrix that says:
            //  localTowards -> desTowardsWorld
            //  localUp      -> desUpWorld
            //
            // We do this by constructing world basis vectors for the grid's Forward/Right/Up.
            // Then we remap them according to which face is "towards" and which is "up".

            // Start with some dummy basis, we'll overwrite
            Vector3D newF = Vector3D.Zero;
            Vector3D newU = Vector3D.Zero;
            Vector3D newR = Vector3D.Zero;

            AssignAxis(localTowards, desTowardsWorld, ref newF, ref newU, ref newR);
            AssignAxis(localUp,      desUpWorld,      ref newF, ref newU, ref newR);

            // If Up/Right not fully determined yet, complete an orthonormal frame
            // Ensure Forward is valid
            if (newF.LengthSquared() < 1e-8)
                newF = desTowardsWorld;
            newF.Normalize();

            // Prefer to use given Up; if not, use ref Up
            if (newU.LengthSquared() < 1e-8)
                newU = desUpWorld;

            // Make Up orthogonal to Forward
            newU = Vector3D.Normalize(newU - newF * Vector3D.Dot(newF, newU));
            // Right from cross
            newR = Vector3D.Normalize(Vector3D.Cross(newF, newU));
            // Re-orthogonalize Up
            newU = Vector3D.Normalize(Vector3D.Cross(newR, newF));

            // Desired rotation matrix in WORLD
            MatrixD desRot = MatrixD.Identity;
            desRot.Forward = newF;
            desRot.Up      = newU;
            desRot.Right   = newR;

            // Current grid rotation in WORLD
            MatrixD curRot = heldGrid.WorldMatrix.GetOrientation();

            Quaternion qCur = Quaternion.CreateFromRotationMatrix((Matrix)curRot);
            Quaternion qTgt = Quaternion.CreateFromRotationMatrix((Matrix)desRot);

            // Error: rotation that takes current → target
            Quaternion qErr = Quaternion.Multiply(qTgt, Quaternion.Conjugate(qCur));
            qErr.Normalize();
            if (qErr.W < 0f)
                qErr = new Quaternion(-qErr.X, -qErr.Y, -qErr.Z, -qErr.W);

            // axis-angle
            Vector3 axisWorld;
            float   angleRad;
            QuaternionToAxisAngle(qErr, out axisWorld, out angleRad);

            if (axisWorld.LengthSquared() < 1e-8 || Math.Abs(angleRad) < MathHelper.ToRadians(0.5))
            {
                Log.Info($"[ALIGN] grid={gridId} faceT={faceTowardsIndex} faceU={faceUpIndex} angleRad~0 => no torque.");
                return Vector3D.Zero;
            }

            axisWorld.Normalize();

            Vector3D omegaWorld = heldGrid.Physics.AngularVelocity;
            double   omegaMag   = omegaWorld.Length();

            // --- controller limits & gains (radians + SI) ---
            // Config values assumed to be in *deg/s* for angular velocity, convert here:
            double maxOmegaCfg = MathHelper.ToRadians(Config.AlignMaxAngularVelocity);
            double maxLinV     = Config.AlignMaxLinearVelocity;
            double maxTorqueCfg = Config.AlignMaxTorque;

            // limit by linear speed at furthest point
            double maxOmegaLin = maxLinV / furthestR;
            double maxOmegaEff = Math.Min(maxOmegaCfg, maxOmegaLin);

            double maxAlpha = maxTorqueCfg / Iavg; // rad/s^2

            // near-critical PD design around ~45°
            double designAngle = MathHelper.ToRadians(45.0);
            double kPos = maxAlpha / designAngle;
            double kVel = 2.0 * Math.Sqrt(kPos);

            // scalar components along error axis
            double omegaAlong = Vector3D.Dot(omegaWorld, axisWorld);
            double alphaP = kPos * angleRad;
            double alphaD = -kVel * omegaAlong;

            double alphaAlong = alphaP + alphaD;

            // clamp accel along axis
            if (Math.Abs(alphaAlong) > maxAlpha)
                alphaAlong = Math.Sign(alphaAlong) * maxAlpha;

            // build desired accel vector in WORLD
            Vector3D alphaWorld = (Vector3D)axisWorld * alphaAlong;

            // extra damping if spinning too fast overall
            if (omegaMag > maxOmegaEff)
            {
                double extraDamp = kVel * 0.5;
                alphaWorld -= omegaWorld * extraDamp;
            }

            // convert alphaWorld -> local, apply full inertia matrix, then back to world
            MatrixD R  = curRot;
            MatrixD Rt = MatrixD.Transpose(R);

            Vector3D alphaLocal = Vector3D.TransformNormal(alphaWorld, Rt);
            Vector3D torqueLocal = new Vector3D(
                Ibody.M11 * alphaLocal.X + Ibody.M12 * alphaLocal.Y + Ibody.M13 * alphaLocal.Z,
                Ibody.M21 * alphaLocal.X + Ibody.M22 * alphaLocal.Y + Ibody.M23 * alphaLocal.Z,
                Ibody.M31 * alphaLocal.X + Ibody.M32 * alphaLocal.Y + Ibody.M33 * alphaLocal.Z
            );
            Vector3D torqueWorld = Vector3D.TransformNormal(torqueLocal, R);

            double tMag = torqueWorld.Length();
            if (tMag > maxTorqueCfg)
            {
                double factor = maxTorqueCfg / tMag;
                torqueWorld *= factor;
            }

            Log.Info(
                $"[ALIGN] grid={gridId} faceT={faceTowardsIndex} faceU={faceUpIndex} " +
                $"angleDeg={MathHelper.ToDegrees(angleRad):F2} |ω|={omegaMag:F2} rad/s " +
                $"maxOmegaEff={maxOmegaEff:F2} rad/s " +
                $"alphaAlong={alphaAlong:F2} rad/s^2 " +
                $"torqueWorld=({torqueWorld.X:F1},{torqueWorld.Y:F1},{torqueWorld.Z:F1}) |τ|={torqueWorld.Length():F1}"
            );

            return torqueWorld;
        }

        // Assign target directions depending on which local axis is chosen
        // This is basically: if +Forward face is "towards", then newF = desTowardsWorld, etc.
        private void AssignAxis(Vector3D localAxis, Vector3D targetWorld, ref Vector3D f, ref Vector3D u, ref Vector3D r)
        {
            // map +/-Forward/Right/Up
            if (localAxis == Vector3D.Forward)      f = targetWorld;
            else if (localAxis == Vector3D.Backward) f = -targetWorld;
            else if (localAxis == Vector3D.Up)      u = targetWorld;
            else if (localAxis == Vector3D.Down)    u = -targetWorld;
            else if (localAxis == Vector3D.Right)   r = targetWorld;
            else if (localAxis == Vector3D.Left)    r = -targetWorld;
        }

        private static void QuaternionToAxisAngle(Quaternion q, out Vector3 axis, out float angle)
        {
            if (q.W > 1f || q.W < -1f)
                q.Normalize();

            angle = (float)(2.0 * Math.Acos(q.W)); // radians
            float s = (float)Math.Sqrt(1.0 - q.W * q.W);

            if (s < 1e-6f)
            {
                // axis not important when angle is tiny
                axis = new Vector3(1f, 0f, 0f);
                angle = 0f;
            }
            else
            {
                axis = new Vector3(q.X / s, q.Y / s, q.Z / s);
            }
        }

        private static Vector3D GetLocalAxisForFace(int faceIndex)
        {
            switch (faceIndex)
            {
                case 0: return Vector3D.Forward;   // +F
                case 2: return Vector3D.Backward;  // -F
                case 1: return Vector3D.Right;     // +R
                case 4: return Vector3D.Left;      // -R
                case 3: return Vector3D.Up;        // +U
                case 5: return Vector3D.Down;      // -U
                default: return Vector3D.Forward;
            }
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

        struct ShiftedBlock
        {
            public Vector3D pos;
            public Vector3D size;
            public double mass;
        }

        private GridPhysicsCache ComputeGridInertia(IMyCubeGrid grid)
        {
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            if (blocks.Count == 0)
            {
                Log.Info($"[INERTIA] grid={grid.EntityId} has 0 blocks, cache invalid.");
                return null;
            }

            var cache = new GridPhysicsCache();

            // 1) Use SE's CoM in local coordinates (meters)
            Vector3D com = grid.Physics.CenterOfMassLocal;
            cache.comLocal = com;

            // 2) Compute inertia tensor around CoM and furthest radius
            double Ixx = 0.0, Iyy = 0.0, Izz = 0.0;
            double Ixy = 0.0, Ixz = 0.0, Iyz = 0.0;

            double maxRadius = 0.0;
            Vector3D maxCorner = Vector3D.Zero;

            double gridSize = grid.GridSize;

            Log.Info($"[INERTIA] START grid={grid.EntityId} blocks={blocks.Count} CoMlocal={com} gridSize={gridSize}");

            foreach (var b in blocks)
            {
                double m = b.Mass;
                if (m <= 0.0)
                    continue;

                // Block center in local meters
                Vector3I cell = b.Position;
                Vector3D center = (Vector3D)cell * gridSize;

                // Shift so that CoM is at origin (purely for math)
                Vector3D r = center - com;
                double x = r.X, y = r.Y, z = r.Z;
                double r2 = x * x + y * y + z * z;

                // Block size (assuming full cube)
                Vector3D s = new Vector3D(gridSize, gridSize, gridSize);

                // Box inertia about its own center (aligned with grid axes)
                double Ibx = (1.0 / 12.0) * m * (s.Y * s.Y + s.Z * s.Z);
                double Iby = (1.0 / 12.0) * m * (s.X * s.X + s.Z * s.Z);
                double Ibz = (1.0 / 12.0) * m * (s.X * s.X + s.Y * s.Y);

                // Parallel-axis terms relative to CoM
                double Px  = m * (r2 - x * x);
                double Py  = m * (r2 - y * y);
                double Pz  = m * (r2 - z * z);

                double Pxy = -m * x * y;
                double Pxz = -m * x * z;
                double Pyz = -m * y * z;

                // Diagonals
                Ixx += Ibx + Px;
                Iyy += Iby + Py;
                Izz += Ibz + Pz;

                // Off-diagonals
                Ixy += Pxy;
                Ixz += Pxz;
                Iyz += Pyz;

                // Furthest corner (for linear speed → angular speed limit)
                Vector3D half = s * 0.5;
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3D corner = new Vector3D(
                        r.X + sx * half.X,
                        r.Y + sy * half.Y,
                        r.Z + sz * half.Z
                    );

                    double d = corner.Length();
                    if (d > maxRadius)
                    {
                        maxRadius = d;
                        maxCorner = corner;
                    }
                }
            }

            cache.furthestRadius      = maxRadius;
            cache.furthestCornerLocal = maxCorner;

            // 3) Build inertiaBody matrix (3x3 in a 4x4 MatrixD)
            MatrixD Ibody = MatrixD.Zero;
            Ibody.M11 = Ixx;
            Ibody.M22 = Iyy;
            Ibody.M33 = Izz;

            Ibody.M12 = Ixy; Ibody.M21 = Ixy;
            Ibody.M13 = Ixz; Ibody.M31 = Ixz;
            Ibody.M23 = Iyz; Ibody.M32 = Iyz;

            Ibody.M44 = 1.0; // make it a valid 4x4

            cache.inertiaBody = Ibody;

            // Correct way to invert MatrixD in SE:
            MatrixD Iinv;
            MatrixD.Invert(ref Ibody, out Iinv);
            cache.inertiaBodyInv = Iinv;

            cache.inertiaScalar = (Ixx + Iyy + Izz) / 3.0;
            cache.valid = true;

            Log.Info($"[INERTIA] DONE grid={grid.EntityId} Ixx={Ixx:F3} Iyy={Iyy:F3} Izz={Izz:F3} " +
                     $"Ixy={Ixy:F3} Ixz={Ixz:F3} Iyz={Iyz:F3} Iavg={cache.inertiaScalar:F3} " +
                     $"furthestR={maxRadius:F3} furthestCornerLocal={maxCorner}");

            return cache;
        }
    }
}