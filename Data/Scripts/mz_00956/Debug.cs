using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Utils;

namespace mz_00956.ImprovisedEngineering
{
    static class Debug
    {
        public static void Log(object message = null, string sender = "IME-DEBUG", bool informUser = false)
        {
            if (!Config.DebugMode) return;


            if (informUser)
                Message(sender, message);

            MyLog.Default.WriteLineAndConsole($"{sender}: {message}");
        }

        public static void Error(object message = null, string sender = "IME-ERROR", bool informUser = true)
        {
            MyLog.Default.WriteLineAndConsole($"ERROR - {sender}: {message}");

            if (MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowMessage(sender, $"{message}");

            if (!Config.DebugMode) return;

            Notify($"{sender}: {message}", color: "Red", ping: true);
        }

        public static void Message(object message)
        {
            if (!Config.DebugMode) return;

            if (MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowMessage("Pc", $"{message}");
        }

        public static void Message(string sender, object message)
        {
            if (!Config.DebugMode) return;

            if (MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowMessage(sender, $"{message}");
        }

        public static void Notify(object message, int ms = 2000, string color = "Blue", bool ping = false)
        {
            if (!Config.DebugMode) return;

            if (MyAPIGateway.Session?.Player != null)
            {
                MyAPIGateway.Utilities.ShowNotification(message.ToString(), ms, color);
                if (ping)
                    MyVisualScriptLogicProvider.PlayHudSoundLocal();
            }
        }

    }
}
