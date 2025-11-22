using System;

namespace mz_00956.ImprovisedEngineering
{
    static class CommandHelper
    {
        private static readonly string[] supporters = new string[]
        {
            "Logiwonk"
        };

        public const int newsVersion = 3;

        public static readonly string MESSAGE =
            "Improvised Engineering updated!\n" +
            "- New, more vanilla-like rotation\n" +
            "- LMB rotation is now a toggle\n" +
            "- New Config Settings -> \"/IME\"\n" +
            "- SHIFT: toggle camera/grid alignment\n" +
            "- ALT: cycle 'towards' face\n" +
            "- CTRL: cycle 'up' face\n" +
            "- MMB: set relative grid\n" +
            "- Grids remember their toggle states\n" +
            "- Close grids get a small force boost and move aside\n" +
            "- Grids with <5 blocks get a small force boost\n" +
            "- When 2 grids merge, the held grid is dropped" +
            (Config.DebugMode
                ? "\n(Debug mode is ON) -> /IME debug to toggle"
                : "") +
            "\n\"/IME news\" to show this message again.";

        public static void HandleCommands(ulong sender, string messageText, ref bool sendToOthers)
        {
            var trimmed = messageText.Trim();

            // Old shortcut
            if (trimmed == "/mz Mods")
            {
                Utils.Message("mz", "Improvised Experimentation -> /IME [command]");
                sendToOthers = false;
                return;
            }

            // Only handle IME commands below
            if (!trimmed.StartsWith("/IME", StringComparison.OrdinalIgnoreCase))
                return;

            sendToOthers = false;

            // Extract args after "/IME"
            string args = trimmed.Length > 4
                ? trimmed.Substring(4).Trim()
                : string.Empty;

            // No args -> short help
            if (string.IsNullOrEmpty(args))
            {
                ShowImeShortHelp();
                return;
            }

            var parts = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0];
            var cmdLower = cmd.ToLowerInvariant();
            var param = parts.Length > 1 ? parts[1] : null;

            // Global help
            if (cmdLower == "help" || cmdLower == "--help" || cmdLower == "-h")
            {
                ShowImeGlobalHelp();
                return;
            }

            // Per-command help: /IME <command> --help
            if (string.Equals(param, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(param, "-h", StringComparison.OrdinalIgnoreCase))
            {
                ShowImeCommandHelp(cmdLower);
                return;
            }

            switch (cmdLower)
            {
                //
                // 1) In-game settings
                //
                case "modes":
                case "modemsg":
                {
                    bool? requested = ParseOnOff(param);
                    if (requested.HasValue)
                        Config.ShowModes = requested.Value;
                    else if (param == null)
                        Config.ShowModes = !Config.ShowModes;
                    else
                    {
                        Utils.Message("IME", "Usage: /IME modes [on|off|--help]");
                        return;
                    }

                    Utils.Message("IME", "Show mode change messages: " + (Config.ShowModes ? "ON" : "OFF"));
                    return;
                }

                case "grabtool":
                case "grab":
                {
                    bool? requested = ParseOnOff(param);
                    if (requested.HasValue)
                        Config.GrabTool = requested.Value;
                    else if (param == null)
                        Config.GrabTool = !Config.GrabToolSetting;
                    else
                    {
                        Utils.Message("IME", "Usage: /IME grabTool [on|off|--help]");
                        return;
                    }

                    Utils.Message("IME",
                        "Hold grid with tool equipped: " +
                        (Config.GrabToolSetting ? "ON" : "OFF") +
                        (Config.GrabTool.HasValue ? "" : " (UNSET, using default)"));
                    return;
                }

                case "holdui":
                case "hold":
                {
                    bool? requested = ParseOnOff(param);
                    if (requested.HasValue)
                        Config.HoldUi = requested.Value;
                    else if (param == null)
                        Config.HoldUi = !Config.HoldUi;
                    else
                    {
                        Utils.Message("IME", "Usage: /IME holdUi [on|off|--help]");
                        return;
                    }

                    Utils.Message("IME", "Hold grid with menu or chat open: " + (Config.HoldUi ? "ON" : "OFF"));
                    return;
                }

                case "offset":
                case "offhand":
                {
                    bool? requested = ParseOnOff(param);
                    if (requested.HasValue)
                        Config.OffsetHand = requested.Value;
                    else if (param == null)
                        Config.OffsetHand = !Config.OffsetHand;
                    else
                    {
                        Utils.Message("IME", "Usage: /IME offset [on|off|--help]");
                        return;
                    }

                    Utils.Message("IME", "Offset close grid to hand: " + (Config.OffsetHand ? "ON" : "OFF"));
                    return;
                }

                case "lockuse":
                case "lock":
                {
                    bool? requested = ParseOnOff(param);
                    if (requested.HasValue)
                        Config.LockUse = requested.Value;
                    else if (param == null)
                        Config.LockUse = !Config.LockUse;
                    else
                    {
                        Utils.Message("IME", "Usage: /IME lockUse [on|off|--help]");
                        return;
                    }

                    Utils.Message("IME",
                        "Disable interactions while holding grid: " +
                        (Config.LockUse ? "ON" : "OFF"));
                    return;
                }
                case "rot":
                case "rotation":
                {
                    bool? requested = ParseOnOff(param);
                    if (requested.HasValue)
                        Config.RotToggle = requested.Value;
                    else if (param == null)
                        Config.RotToggle = !Config.RotToggle;
                    else
                    {
                        Utils.Message("IME", "Usage: /IME rot [on|off|--help]");
                        return;
                    }

                    Utils.Message("IME", "Rotation toggle (LMB acts as toggle): " + (Config.RotToggle ? "ON" : "OFF"));
                    return;
                }

                case "kb2":
                case "keyboard2":
                {
                    bool? requested = ParseOnOff(param);
                    if (requested.HasValue)
                        Config.Keyboard2 = requested.Value;
                    else if (param == null)
                        Config.Keyboard2 = !Config.Keyboard2;
                    else
                    {
                        Utils.Message("IME", "Usage: /IME kb2 [on|off|--help]");
                        return;
                    }

                    Utils.Message("IME", "Legacy Keyboard2 input (CTRL+WASDQE): " + (Config.Keyboard2 ? "ON" : "OFF"));
                    return;
                }

                //
                // 2) Other stuff
                //
                case "news":
                    Utils.Message("IME", MESSAGE);
                    return;

                case "supporters":
                case "thanks":
                    ShowSupporters();
                    return;

                //
                // 3) Dev stuff
                //
                case "debug":
                case "dbg":
                {
                    bool? requested = ParseOnOff(param);
                    if (requested.HasValue)
                        Config.DebugMode = requested.Value;
                    else if (param == null)
                        Config.DebugMode = !Config.DebugMode;
                    else
                    {
                        Utils.Message("IME", "Usage: /IME debug [on|off|--help]");
                        return;
                    }

                    Utils.Message("IME", "Debug mode: " + (Config.DebugMode ? "ON" : "OFF"));
                    return;
                }

                case "state":
                case "cfg":
                    ShowState(parts.Length > 1 ? parts[1].ToLowerInvariant() : null);
                    return;

                case "reset":
                    Utils.Message("IME",
                        "Resetting all held grid states.\n" +
                        "(All per-grid settings will be recalculated on next use.)");
                    ClientGridHandler.GridSettings.Clear();
                    return;

                default:
                    Utils.Message("IME", $"Unknown command '{cmd}'. Type /IME help for a list of commands.");
                    return;
            }
        }

        // ---------------- helpers ----------------

        private static bool? ParseOnOff(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            value = value.ToLowerInvariant();
            if (value == "on" || value == "true") return true;
            if (value == "off" || value == "false") return false;
            return null;
        }

        private static void ShowSupporters()
        {
            var text =
                "Supporters of this mod:\n" +
                "- " + string.Join("\n- ", supporters) +
                "\n\nThank you very much for your support!";
            Utils.Message("IME", text);
        }

        private static void ShowImeShortHelp()
        {
            Utils.Message("IME", "Commands:\n" +
                "    modes\n" +
                "    grabTool\n" +
                "    holdUi\n" +
                "    offset\n" +
                "    lockUse\n" +
                "    rotation\n" +
                "    keyboard2\n" +
                "    supporters\n" +
                "    debug\n" +
                "    state\n" +
                "    reset\n" +
                "Use '/IME <command> --help' for details.\n" +
                "Use '/IME --help' for full details.");
        }

        private static void ShowImeGlobalHelp()
        {
            Utils.Message("IME", "Commands:\n" +
                "  modes | modeMsg    - Show/hide mode change messages\n" +
                "  grabTool | grab    - Grab grid while tool is equipped\n" +
                "  holdUi | hold    - Hold grid while menu/chat is open\n" +
                "  offset | offHand    - Offset close grid to hand\n" +
                "  lockUse | lock    - Disable LMB/RMB/F while holding\n" +
                "  rot | rotation    - LMB rotation acts as toggle\n" +
                "  kb2 | keyboard2    - Track 2. input row in controls\n" +
                "  news    - Show latest changelog message\n" +
                "  supporters    - List supporters of this mod\n" +
                "  debug | dbg    - Toggle or set debug mode\n" +
                "  state | cfg    - Print current config state\n" +
                "  reset    - Reset all held grid states\n" +
                "Usage: /IME <command> [on|off|--help]");
        }

        private static void ShowImeCommandHelp(string cmdLower)
        {
            string text;
            switch (cmdLower)
            {
                // In-game
                case "modes":
                case "modemsg":
                    text =
                        "modes: show/hide mode change chat messages.\n" +
                        "Aliases: modeMsg\n" +
                        "Usage: /IME modes [on|off]";
                    break;

                case "grabtool":
                case "grab":
                    text =
                        "grabTool: allow grabbing grids while a tool is equipped.\n" +
                        "Aliases: grab\n" +
                        "Usage: /IME grabTool [on|off]\n" +
                        "Note: when unset, a default may be used.";
                    break;

                case "holdui":
                case "hold":
                    text =
                        "holdUi: keep holding grids while menu or chat is open.\n" +
                        "Aliases: hold\n" +
                        "Usage: /IME holdUi [on|off]";
                    break;

                case "offset":
                case "offhand":
                    text =
                        "offset: move close grids slightly to the hand to avoid blocking view.\n" +
                        "Aliases: offHand\n" +
                        "Usage: /IME offset [on|off]";
                    break;

                case "lockuse":
                case "lock":
                    text =
                        "lockUse: disable interactions (LMB, RMB, F) while holding a grid.\n" +
                        "Aliases: lock\n" +
                        "Usage: /IME lockUse [on|off]";
                    break;

                case "rot":
                case "rotation":
                    text =
                        "rot: toggle LMB rotation-once vs hold-to-rotate.\n" +
                        "Aliases: rotation\n" +
                        "Usage: /IME rot [on|off]";
                    break;

                case "kb2":
                case "keyboard2":
                    text =
                        "kb2: enable or disable the tracking of the 2. input row. (where keen placed the CTRL+WASDQE rotation).\n" +
                        "Aliases: keyboard2\n" +
                        "Usage: /IME kb2 [on|off]";
                    break;

                // Other
                case "news":
                    text =
                        "news: show the current version's changelog message again.\n" +
                        "Aliases: (none)\n" +
                        "Usage: /IME news";
                    break;

                case "supporters":
                case "thanks":
                    text =
                        "supporters: list players who support this mod.\n" +
                        "Aliases: thanks\n" +
                        "Usage: /IME supporters";
                    break;

                // Dev
                case "debug":
                case "dbg":
                    text =
                        "debug: toggle or set debug mode.\n" +
                        "Aliases: dbg\n" +
                        "Usage: /IME debug [on|off]";
                    break;

                case "state":
                case "cfg":
                    text =
                        "state: print current config values.\n" +
                        "Aliases: cfg\n" +
                        "Usage:\n" +
                        "  /IME state        -> show all\n" +
                        "  /IME state debug  -> show only 'debug' state\n" +
                        "  /IME state grab   -> show only 'grabTool' state";
                    break;

                case "reset":
                    text =
                        "reset: clear all saved held-grid states.\n" +
                        "Aliases: (none)\n" +
                        "Usage: /IME reset";
                    break;

                default:
                    text = $"Unknown command '{cmdLower}'. Type /IME help for all commands.";
                    break;
            }

            Utils.Message("IME", text);
        }

        private static string BoolToStr(bool? value, bool effective)
        {
            if (!value.HasValue)
                return $"UNSET (effective: {(effective ? "ON" : "OFF")})";
            return value.Value ? "ON" : "OFF";
        }

        private static void ShowState(string singleKey)
        {
            // Helper to render nullable bool


            if (!string.IsNullOrEmpty(singleKey))
            {
                string text;
                switch (singleKey)
                {
                    case "debug":
                    case "dbg":
                        text = $"debug / dbg: {(Config.DebugMode ? "ON" : "OFF")}";
                        break;

                    case "modes":
                    case "modemsg":
                        text = $"modes / modeMsg: {(Config.ShowModes ? "ON" : "OFF")}";
                        break;

                    case "grabtool":
                    case "grab":
                        text = $"grabTool / grab: {BoolToStr(Config.GrabTool, Config.GrabToolSetting)}";
                        break;

                    case "holdui":
                    case "hold":
                        text = $"holdUi / hold: {(Config.HoldUi ? "ON" : "OFF")}";
                        break;

                    case "offset":
                    case "offhand":
                        text = $"offset / offHand: {(Config.OffsetHand ? "ON" : "OFF")}";
                        break;

                    case "lockuse":
                    case "lock":
                        text = $"lockUse / lock: {(Config.LockUse ? "ON" : "OFF")}";
                        break;

                    case "rot":
                    case "rotation":
                        text = $"rot / rotation: {(Config.RotToggle ? "ON" : "OFF")}";
                        break;

                    case "kb2":
                    case "keyboard2":
                        text = $"kb2 / keyboard2: {(Config.Keyboard2 ? "ON" : "OFF")}";
                        break;

                    default:
                        text =
                            "Unknown state key '" + singleKey + "'. Try one of:\n" +
                            "  debug, modes, grabTool, holdUi, offset, lockUse.";
                        break;
                }

                Utils.Message("IME", text);
                return;
            }

            // Full dump, grouped in the same order the player sees commands
            var fullText =
                "Current IME config state:\n" +
                $"  modes / modeMsg      : {(Config.ShowModes ? "ON" : "OFF")}\n" +
                $"  grabTool / grab      : {BoolToStr(Config.GrabTool, Config.GrabToolSetting)}\n" +
                $"  holdUi / hold        : {(Config.HoldUi ? "ON" : "OFF")}\n" +
                $"  offset / offHand     : {(Config.OffsetHand ? "ON" : "OFF")}\n" +
                $"  lockUse / lock       : {(Config.LockUse ? "ON" : "OFF")}\n" +
                $"  rot / rotation     : {(Config.RotToggle ? "ON" : "OFF")}\n" +
                $"  kb2 / keyboard2    : {(Config.Keyboard2 ? "ON" : "OFF")}\n" +
                $"  debug / dbg          : {(Config.DebugMode ? "ON" : "OFF")}";

            Utils.Message("IME", fullText);
        }
    }
}