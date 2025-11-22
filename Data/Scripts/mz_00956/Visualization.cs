using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace mz_00956.ImprovisedEngineering
{
    static class Visualization
    {
        public static void DrawLine(Vector3D position, Vector3D direction, int red, int green, int blue, double thickness = 0.01f)
        {
            DrawLine(position, direction, red / 255f, green / 255f, blue / 255f, thickness);
        }

        public static void DrawLine(Vector3D position, Vector3D direction, double red, double green, double blue, double thickness = 0.01f)
        {
            Vector4 color = new Vector4D(red, green, blue, 1);
            MySimpleObjectDraw.DrawLine(position, position + direction, MyStringId.GetOrCompute("Square"), ref color, (float)thickness);
        }

        public static void DrawLineDirect(Vector3D posistionStart, Vector3D positionEnd, int red, int green, int blue, double thickness = 0.01f)
        {
            DrawLineDirect(posistionStart, positionEnd, red / 255f, green / 255f, blue / 255f, thickness);
        }

        public static void DrawLineDirect(Vector3D posistionStart, Vector3D positionEnd, double red, double green, double blue, double thickness = 0.01f)
        {
            Vector4 color = new Vector4D(red, green, blue, 1);
            MySimpleObjectDraw.DrawLine(posistionStart, positionEnd, MyStringId.GetOrCompute("Square"), ref color, (float)thickness);
        }
    }
}