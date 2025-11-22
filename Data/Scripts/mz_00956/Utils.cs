using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using VRage.ModAPI;

namespace mz_00956.ImprovisedEngineering
{
    static class Utils
    {
        public static IMyEntity OnSurface(IMyPlayer player)
        {
            if (player == null || player.Character == null || player.Character.EnabledThrusts)
                return null;

            const double RandomRadius = 0.25;
            Vector3D horizontalRandom = player.Character.WorldMatrix.Right * MyUtils.GetRandomDouble(-RandomRadius, RandomRadius)
                                      + player.Character.WorldMatrix.Forward * MyUtils.GetRandomDouble(-RandomRadius, RandomRadius);

            Vector3D from = player.Character.GetPosition() + player.Character.WorldMatrix.Up + horizontalRandom;
            Vector3D to = player.Character.GetPosition() + player.Character.WorldMatrix.Down + horizontalRandom;

            IHitInfo hit = null;
            MyAPIGateway.Physics.CastRay(from, to, out hit, 28);

            if (hit != null)
                return hit.HitEntity;

            return null;
        }
        
        public static bool TouchingPlayer(IMyPlayer player, IMyEntity heldGrid)
        {
            Vector3D center = player.Character.GetPosition() + player.Character.WorldMatrix.Up;

            const double RandomRadius = 0.40;
            Vector3D randomRight = player.Character.WorldMatrix.Right * MyUtils.GetRandomDouble(-RandomRadius, RandomRadius);
            Vector3D randomForward = player.Character.WorldMatrix.Forward * MyUtils.GetRandomDouble(-RandomRadius, RandomRadius);
            Vector3D randomUp = player.Character.WorldMatrix.Up * MyUtils.GetRandomDouble(-RandomRadius, RandomRadius) * 2;

            Vector3D from = center;
            Vector3D[] toS =
                {
                    center + player.Character.WorldMatrix.Down * 1.2 + randomRight + randomForward,
                    center + player.Character.WorldMatrix.Up * 1.2 + randomRight + randomForward,
                    center + player.Character.WorldMatrix.Right * 0.6 + randomUp + randomForward,
                    center + player.Character.WorldMatrix.Left * 0.6 + randomUp + randomForward,
                    center + player.Character.WorldMatrix.Forward * 0.6 + randomUp + randomRight,
                    center + player.Character.WorldMatrix.Backward * 0.6 + randomUp + randomRight
                };

            IHitInfo hit;
            foreach (var to in toS)
            {
                MyAPIGateway.Physics.CastRay(from, to, out hit, 11);
                //DrawLineDirect(from, to, 255, 0, 0);

                if (hit != null && hit.HitEntity == heldGrid)
                    return true;
            }

            return false;
        }

        public static void Message(object message)
        {
            MyAPIGateway.Utilities.ShowMessage("IME", message.ToString());
        }
        
        public static void Message(string sender, object message)
        {
            if (sender == null)
                sender = "IME";

            MyAPIGateway.Utilities.ShowMessage(sender, message.ToString());
        }

        public static void Notify(object message, int ms = 1000, string color = "Blue")
        {
            MyAPIGateway.Utilities.ShowNotification(message.ToString(), ms, color);
        }
    }
}
