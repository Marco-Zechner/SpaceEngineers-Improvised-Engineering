using ProtoBuf;
using Sandbox.ModAPI;
using VRageMath;

namespace mz_00956.ImprovisedEngineering
{
    // An example packet extending another packet.
    // Note that it must be ProtoIncluded in PacketBase for it to work.
    [ProtoContract]
    public class GridInformationPacket : PacketBase
    {
        // tag numbers in this class won't collide with tag numbers from the base class
        [ProtoMember(1)]
        public long GridID;

        [ProtoMember(2)]
        public double Mass;
        
        [ProtoMember(3)]
        public double MaxForce;

        [ProtoMember(4)]
        public Vector3D[] HoldingPoints;

        [ProtoMember(5)]
        public Vector3D[] Directions;

        [ProtoMember(6)]
        public double[] Tensions;

        public GridInformationPacket() { } // Empty constructor required for deserialization

        public GridInformationPacket(long gridID, double mass, double maxForce, Vector3D[] holdingPoints, Vector3D[] directions, double[] tensions)
        {
            GridID = gridID;
            Mass = mass;
            MaxForce = maxForce;
            HoldingPoints = holdingPoints;
            Directions = directions;
            Tensions = tensions;
        }

        public override bool Received()
        {
            if (MyAPIGateway.Session.Player != null)
            {
                Debug.Log($"GridInformationPacket.Received: Server send grid information packet to player", informUser: true);
                
                for (int i = 0; i < HoldingPoints.Length; i++)
                {
                    Vector3D holdingPoint = HoldingPoints[i];
                    Vector3D direction = Directions[i];
                    double tension = Tensions[i];
                    Visualization.DrawLine(holdingPoint, direction, tension, 1 - tension, 0);
                }
            }

            return true; // relay packet to other clients (only works if server receives it)
        }
    }
}