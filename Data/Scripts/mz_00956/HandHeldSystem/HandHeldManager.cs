using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;
using System;
using System.Collections.Generic;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game;
using Sandbox.ModAPI.Interfaces;
using VRage.Input;

namespace mz_00956.ImprovisedEngineering
{
    // What we read from [IME] for a single handheld grid
    public class HandHeldConfig
    {
        public long GridId;
        public IMyTerminalBlock SettingsBlock;   // "HandHeldSettings"
        public Vector3D CoMOffsetLocal;          // offset in grid-local coords
        public int UpFace = 0;                   //
        public int FwdFace = 2;                  //

        public readonly HashSet<MyKeys> DisabledKeys = new HashSet<MyKeys>();

        // Parsed actions (condition + target block + target action)
        public readonly List<HandHeldAction> Actions = new List<HandHeldAction>();
    }

    public enum HandHeldInputKind
    {
        Down,
        Up,
        Held
    }

    public enum CondNodeType
    {
        Leaf,
        And,
        Or
    }

    public struct InputPredicate
    {
        public HandHeldInputKind Kind;
        public MyKeys Key;
    }

    public class CondNode
    {
        public CondNodeType Type;
        public CondNode Left;
        public CondNode Right;
        public InputPredicate Predicate; // valid if Type == Leaf
    }

    public class HandHeldAction
    {
        public CondNode Condition;

        public string TargetBlockName;
        public string TargetActionName;

        public IMyTerminalBlock TargetBlock;    // resolved at load, re-checked at runtime
        public IMyBlockGroup TargetBlockGroup;    // resolved at load, re-checked at runtime
        public ITerminalAction TargetAction;    // resolved at load, refreshed if null

        public bool WarnedMissingBlock;
        public bool WarnedMissingAction;
        public bool WarnedAmbiguousBlock;
    }

    public static class HandHeldManager
    {
        // One profile per handheld grid
        private static readonly Dictionary<long, HandHeldConfig> _configByGrid
            = new Dictionary<long, HandHeldConfig>();

        public static HandHeldConfig TryGet(IMyCubeGrid grid, bool reload = false)
        {
            if (grid == null)
                return null;

            HandHeldConfig cfg;
            if (!reload && _configByGrid.TryGetValue(grid.EntityId, out cfg))
                return cfg;

            cfg = TryLoadConfig(grid);
            if (cfg != null)
                _configByGrid[grid.EntityId] = cfg;

            return cfg;
        }

        private static HandHeldConfig TryLoadConfig(IMyCubeGrid grid)
        {
            // 1) Only for grids whose name starts with "HandHeld"
            var name = grid.CustomName ?? grid.DisplayName;
            if (string.IsNullOrEmpty(name) ||
                !name.StartsWith("HandHeld", StringComparison.OrdinalIgnoreCase))
                return null;

            // 2) Find "HandHeldSettings" terminal on same grid
            var terminals = new List<IMyTerminalBlock>();
            var system = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (system == null)
                return null;

            system.GetBlocksOfType<IMyTerminalBlock>(terminals, b =>
                b.CubeGrid == grid &&
                b.CustomName.Equals("HandHeldSettings", StringComparison.OrdinalIgnoreCase));

            if (terminals.Count == 0)
            {
                Debug.Error($"HandHeldManager.TryLoadConfig: No 'HandHeldSettings' found on grid {grid.DisplayName}",  informUser: true);
                return null;
            }

            var settingsBlock = terminals[0];

            // 2.5) Normalize action key names in raw CustomData:
            //      any line in [IME] with "=>" becomes "ActionX = <expr>" (X = 0..N-1)
            NormalizeImeActions(settingsBlock);

            // 3) Parse [IME] section via MyIni
            var ini = new MyIni();
            MyIniParseResult result;
            if (!ini.TryParse(settingsBlock.CustomData, out result))
            {
                Debug.Error($"HandHeldManager.TryLoadConfig: INI parse failed in {grid.DisplayName}: {result.Error}",  informUser: true);
                return null;
            }

            if (!ini.ContainsSection("IME"))
                return null;

            var cfg = new HandHeldConfig
            {
                GridId = grid.EntityId,
                SettingsBlock = settingsBlock,
                CoMOffsetLocal = ReadVector(ini, "IME", "CoMoffset", Vector3D.Zero),
                UpFace = ini.Get("IME", "up").ToInt32(3),
                FwdFace = ini.Get("IME", "forward").ToInt32(0),
            };

            var disableRaw = ini.Get("IME", "disable").ToString();
            if (!string.IsNullOrWhiteSpace(disableRaw))
            {
                ParseDisableKeys(disableRaw, cfg.DisabledKeys);
            }

            // 4) Parse any other keys in [IME] as expressions
            var keys = new List<MyIniKey>();
            ini.GetKeys("IME", keys);

            foreach (var key in keys)
            {
                var keyName = key.Name;

                // Skip known config entries
                if (keyName.Equals("CoMoffset", StringComparison.OrdinalIgnoreCase) ||
                    keyName.Equals("up", StringComparison.OrdinalIgnoreCase) ||
                    keyName.Equals("forward", StringComparison.OrdinalIgnoreCase) ||
                    keyName.Equals("disable", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rawValue = ini.Get(key).ToString();
                if (string.IsNullOrWhiteSpace(rawValue))
                    continue;

                // strip comments //...
                int commentIdx = rawValue.IndexOf("//", StringComparison.Ordinal);
                if (commentIdx >= 0)
                    rawValue = rawValue.Substring(0, commentIdx);

                rawValue = rawValue.Trim();
                if (string.IsNullOrWhiteSpace(rawValue))
                    continue;

                var expr = ParseFullExpression(rawValue);
                if (expr == null)
                {
                    Debug.Error($"HandHeldManager.TryLoadConfig: Failed to parse expression '{rawValue}' for key '{keyName}'", informUser: true);
                    continue;
                }

                // Resolve target block and action now
                var hAction = new HandHeldAction
                {
                    Condition = expr.Condition,
                    TargetBlockName = expr.TargetBlockName,
                    TargetActionName = expr.TargetActionName
                };

                ResolveTarget(grid, ref hAction);

                if ((hAction.TargetBlock == null && hAction.TargetBlockGroup == null) || hAction.TargetAction == null)
                {
                    Debug.Error($"HandHeldManager.TryLoadConfig: Could not resolve block/action for '{rawValue}'", informUser:  true);
                    // still add it, condition may be fine and block might appear later
                }

                cfg.Actions.Add(hAction);
            }

            Debug.Log($"HandHeldManager.TryLoadConfig: Loaded handheld config for {grid.DisplayName} with {cfg.Actions.Count} actions",  informUser: true);
            return cfg;
        }

        private static void NormalizeImeActions(IMyTerminalBlock settingsBlock)
        {
            var raw = settingsBlock.CustomData ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var lines = raw.Replace("\r", "").Split('\n');
            var newLines = new List<string>(lines.Length);
            bool inIme = false;
            int actionIndex = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                if (trimmed.StartsWith("[IME]", StringComparison.OrdinalIgnoreCase))
                {
                    inIme = true;
                    newLines.Add(line);
                    continue;
                }

                if (inIme && trimmed.StartsWith("[") && !trimmed.StartsWith("[IME]", StringComparison.OrdinalIgnoreCase))
                {
                    // leaving IME section
                    inIme = false;
                    newLines.Add(line);
                    continue;
                }

                if (!inIme)
                {
                    newLines.Add(line);
                    continue;
                }

                // Inside [IME]
                int eqIdx = line.IndexOf('=');
                if (eqIdx < 0)
                {
                    newLines.Add(line);
                    continue;
                }

                string keyPart = line.Substring(0, eqIdx).Trim();
                string valPart = line.Substring(eqIdx + 1).Trim();

                // If this line contains "=>" it is treated as an action expression,
                // and we normalize its key to "ActionX"
                if (valPart.Contains("=>"))
                {
                    string newKey = "Action" + actionIndex++;
                    newLines.Add(newKey + " = " + valPart);
                }
                else
                {
                    newLines.Add(line);
                }
            }

            var rebuilt = string.Join("\n", newLines);
            settingsBlock.CustomData = rebuilt;
        }

        private static void ParseDisableKeys(string disableRaw, HashSet<MyKeys> target)
        {
            target.Clear();
            var parts = disableRaw.Trim(new char[] { '"', ' ' }).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var token = p.Trim();
                if (token.Length == 0)
                    continue;

                MyKeys key;
                if (!Enum.TryParse(token, true, out key))
                {
                    Debug.Log($"IME handheld: Unknown key '{token}' in disable list", informUser: true);
                    continue;
                }

                target.Add(key);
            }
        }

        private static Vector3D ReadVector(MyIni ini, string section, string name, Vector3D def)
        {
            var vStr = ini.Get(section, name).ToString();
            if (string.IsNullOrWhiteSpace(vStr))
                return def;

            vStr = vStr.Trim();
            vStr = vStr.Trim('(', ')');
            var parts = vStr.Split(',');
            if (parts.Length != 3)
                return def;

            double x, y, z;
            if (!double.TryParse(parts[0], out x)) return def;
            if (!double.TryParse(parts[1], out y)) return def;
            if (!double.TryParse(parts[2], out z)) return def;
            return new Vector3D(x, y, z);
        }

        // ========= public tick entry =========

        // ========= public tick entry =========

        // Call this from ClientGridHandler.MoveGrid / UpdateAfterSimulation
        public static void HandleHandHeldInputs(HandHeldConfig cfg, int validMode)
        {
            if (cfg == null || cfg.SettingsBlock == null || validMode < 0)
                return;

            var grid = cfg.SettingsBlock.CubeGrid as IMyCubeGrid;
            if (grid == null)
                return;

            foreach (var action in cfg.Actions)
            {
                try
                {
                    if (!EvaluateCondition(action.Condition))
                        continue;

                    var act = action;

                    // Re-resolve if target is missing / closed / moved
                    bool needResolve =
                        act.TargetAction == null ||
                        (act.TargetBlockGroup == null &&
                         (act.TargetBlock == null || act.TargetBlock.Closed || act.TargetBlock.CubeGrid != grid));

                    if (needResolve)
                    {
                        ResolveTarget(grid, ref act);
                    }

                    if (act.TargetAction == null)
                        continue;

                    // If we have a group, apply to all valid blocks in the group
                    if (act.TargetBlockGroup != null)
                    {
                        var groupBlocks = new List<IMyTerminalBlock>();
                        act.TargetBlockGroup.GetBlocks(groupBlocks);

                        foreach (var b in groupBlocks)
                        {
                            if (b == null || b.Closed || b.CubeGrid != grid)
                                continue;

                            if (!act.TargetAction.IsEnabled(b))
                                continue;

                            act.TargetAction.Apply(b);
                        }
                    }
                    else
                    {
                        if (act.TargetBlock == null || act.TargetBlock.Closed || act.TargetBlock.CubeGrid != grid)
                            continue;

                        if (!act.TargetAction.IsEnabled(act.TargetBlock))
                            continue;

                        act.TargetAction.Apply(act.TargetBlock);
                    }
                }
                catch (Exception ex)
                {
                    Debug.Error($"HandHeldManager.HandleHandHeldInputs: Exception running handheld action on grid {cfg.GridId}: {ex}");
                }
            }
        }


        // ========= expression + mapping types =========

        private class HandHeldActionExpr
        {
            public CondNode Condition;
            public string TargetBlockName;
            public string TargetActionName; // may be null => list actions
        }

        private static HandHeldActionExpr ParseFullExpression(string line)
        {
            int arrowIdx = line.IndexOf("=>", StringComparison.Ordinal);
            if (arrowIdx < 0)
                return null;

            string condText = line.Substring(0, arrowIdx).Trim();
            string actionText = line.Substring(arrowIdx + 2).Trim();

            var parser = new ExprParser(condText);
            var cond = parser.ParseExpression();
            if (cond == null)
                return null;

            string blockName = null;
            string actionName = null;

            // Accept:
            //   => "Block"
            //   => "Block"."Action"
            // (and ignore any trailing junk after that for now)
            int q1 = actionText.IndexOf('"');
            if (q1 >= 0)
            {
                int q2 = actionText.IndexOf('"', q1 + 1);
                if (q2 > q1)
                {
                    blockName = actionText.Substring(q1 + 1, q2 - q1 - 1);

                    // try to find a second quoted string for action
                    int q3 = actionText.IndexOf('"', q2 + 1);
                    if (q3 >= 0)
                    {
                        int q4 = actionText.IndexOf('"', q3 + 1);
                        if (q4 > q3)
                        {
                            actionName = actionText.Substring(q3 + 1, q4 - q3 - 1);
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(blockName))
                return null;

            return new HandHeldActionExpr
            {
                Condition = cond,
                TargetBlockName = blockName,
                TargetActionName = actionName // may be null
            };
        }


        // ========= resolve block + action once =========

        private static void ResolveTarget(IMyCubeGrid grid, ref HandHeldAction action)
        {
            action.TargetBlock       = null;
            action.TargetBlockGroup  = null;
            action.TargetAction      = null;

            if (grid == null || string.IsNullOrEmpty(action.TargetBlockName))
                return;

            var system = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            if (system == null)
                return;

            // 1) Try block group first
            IMyBlockGroup group = system.GetBlockGroupWithName(action.TargetBlockName);

            if (group != null)
            {
                var groupBlocks = new List<IMyTerminalBlock>();
                group.GetBlocks(groupBlocks);

                if (groupBlocks.Count == 0)
                {
                    if (!action.WarnedMissingBlock)
                    {
                        Utils.Message($"[IME] Handheld: Block group '{action.TargetBlockName}' has no blocks on grid '{grid.DisplayName}'.");
                        action.WarnedMissingBlock = true;
                    }
                    return;
                }

                // Use first block as action-template
                var templateBlock = groupBlocks[0];
                var acts = new List<ITerminalAction>();
                templateBlock.GetActions(acts);

                // No action name => list actions once, do not auto-pick
                if (string.IsNullOrEmpty(action.TargetActionName))
                {
                    if (!action.WarnedMissingAction)
                    {
                        Utils.Message($"[IME] Handheld: Block group '{action.TargetBlockName}' actions (from first block '{templateBlock.CustomName}'):");
                        foreach (var a in acts)
                            Utils.Message($" - {a.Name}");

                        action.WarnedMissingAction = true;
                    }
                    return;
                }

                ITerminalAction chosen = null;
                foreach (var a in acts)
                {
                    if (a.Name.ToString().Equals(action.TargetActionName, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = a;
                        break;
                    }
                }

                if (chosen == null)
                {
                    if (!action.WarnedMissingAction)
                    {
                        Utils.Message($"[IME] Handheld: Block group '{action.TargetBlockName}' has no action '{action.TargetActionName}'. Available actions (first block '{templateBlock.CustomName}'):");
                        foreach (var a in acts)
                            Utils.Message($" - {a.Name}");

                        action.WarnedMissingAction = true;
                    }
                    return;
                }

                action.TargetBlockGroup = group;
                action.TargetAction     = chosen;
                return;
            }

            // 2) Fallback: specific block by name
            var blocks = new List<IMyTerminalBlock>();
            var actionLocal = action;
            system.GetBlocksOfType<IMyTerminalBlock>(blocks, b =>
                b.CubeGrid == grid &&
                b.CustomName.Equals(actionLocal.TargetBlockName, StringComparison.OrdinalIgnoreCase));

            if (blocks.Count == 0)
            {
                if (!action.WarnedMissingBlock)
                {
                    Utils.Message($"[IME] Handheld: Block '{action.TargetBlockName}' not found on grid '{grid.DisplayName}'.");
                    action.WarnedMissingBlock = true;
                }
                return;
            }

            if (blocks.Count > 1 && !action.WarnedAmbiguousBlock)
            {
                Utils.Message($"[IME] Handheld: Found {blocks.Count} blocks named '{action.TargetBlockName}' on grid '{grid.DisplayName}'. Using the first one.");
                action.WarnedAmbiguousBlock = true;
            }

            var block = blocks[0];
            var acts2 = new List<ITerminalAction>();
            block.GetActions(acts2);

            // No action name => list actions once, do not auto-pick
            if (string.IsNullOrEmpty(action.TargetActionName))
            {
                if (!action.WarnedMissingAction)
                {
                    Utils.Message($"[IME] Handheld: Block '{action.TargetBlockName}' actions:");
                    foreach (var a in acts2)
                        Utils.Message($" - {a.Name}");

                    action.WarnedMissingAction = true;
                }
                return;
            }

            ITerminalAction chosen2 = null;
            foreach (var a in acts2)
            {
                if (a.Name.ToString().Equals(action.TargetActionName, StringComparison.OrdinalIgnoreCase))
                {
                    chosen2 = a;
                    break;
                }
            }

            if (chosen2 == null)
            {
                if (!action.WarnedMissingAction)
                {
                    Utils.Message($"[IME] Handheld: Block '{action.TargetBlockName}' has no action '{action.TargetActionName}'. Available actions:");
                    foreach (var a in acts2)
                        Utils.Message($" - {a.Name}");

                    action.WarnedMissingAction = true;
                }
                return;
            }

            action.TargetBlock  = block;
            action.TargetAction = chosen2;
        }


        // ========= condition evaluation =========

        private static bool EvaluateCondition(CondNode node)
        {
            if (node == null)
                return false;

            switch (node.Type)
            {
                case CondNodeType.Leaf:
                    return CheckInput(node.Predicate);

                case CondNodeType.And:
                    return EvaluateCondition(node.Left) && EvaluateCondition(node.Right);

                case CondNodeType.Or:
                    return EvaluateCondition(node.Left) || EvaluateCondition(node.Right);

                default:
                    return false;
            }
        }

        private static bool CheckInput(InputPredicate p)
        {
            if (p.Key == MyKeys.None)
                return false;

            var input = MyAPIGateway.Input;

            bool isMouse =
                p.Key == MyKeys.LeftButton ||
                p.Key == MyKeys.RightButton ||
                p.Key == MyKeys.MiddleButton ||
                p.Key == MyKeys.ExtraButton1 ||
                p.Key == MyKeys.ExtraButton2;

            if (isMouse)
            {
                MyMouseButtonsEnum mb;
                switch (p.Key)
                {
                    case MyKeys.LeftButton:
                        mb = MyMouseButtonsEnum.Left;
                        break;
                    case MyKeys.RightButton:
                        mb = MyMouseButtonsEnum.Right;
                        break;
                    case MyKeys.MiddleButton:
                        mb = MyMouseButtonsEnum.Middle;
                        break;
                    case MyKeys.ExtraButton1:
                        mb = MyMouseButtonsEnum.XButton1;
                        break;
                    case MyKeys.ExtraButton2:
                        mb = MyMouseButtonsEnum.XButton2;
                        break;
                    default:
                        return false; // should not happen
                }

                switch (p.Kind)
                {
                    case HandHeldInputKind.Down:
                        return input.IsNewMousePressed(mb);
                    case HandHeldInputKind.Up:
                        return input.IsMouseReleased(mb);
                    case HandHeldInputKind.Held:
                        return input.IsMousePressed(mb);
                    default:
                        return false;
                }
            }

            switch (p.Kind)
            {
                case HandHeldInputKind.Down:
                    return input.IsNewKeyPressed(p.Key);
                case HandHeldInputKind.Up:
                    return input.IsNewKeyReleased(p.Key);
                case HandHeldInputKind.Held:
                    return input.IsKeyPress(p.Key);
                default:
                    return false;
            }
        }

        // ========= tiny lexer + parser for the condition side =========

        private enum TokenType
        {
            LParen,
            RParen,
            And,
            Or,
            Func,
            Ident,
            End
        }

        private struct Token
        {
            public TokenType Type;
            public string Text;
        }

        private class Lexer
        {
            private readonly string _s;
            private int _i;

            public Lexer(string s)
            {
                _s = s ?? string.Empty;
                _i = 0;
            }

            public Token Next()
            {
                SkipWs();
                if (_i >= _s.Length)
                    return new Token { Type = TokenType.End };

                char c = _s[_i];

                if (c == '(') { _i++; return new Token { Type = TokenType.LParen, Text = "(" }; }
                if (c == ')') { _i++; return new Token { Type = TokenType.RParen, Text = ")" }; }

                if (c == '&' && _i + 1 < _s.Length && _s[_i + 1] == '&')
                {
                    _i += 2;
                    return new Token { Type = TokenType.And, Text = "&&" };
                }

                if (c == '|' && _i + 1 < _s.Length && _s[_i + 1] == '|')
                {
                    _i += 2;
                    return new Token { Type = TokenType.Or, Text = "||" };
                }

                if (char.IsLetter(c))
                {
                    int start = _i;
                    while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_'))
                        _i++;

                    string word = _s.Substring(start, _i - start);
                    string wl = word.ToLowerInvariant();
                    if (wl == "input" || wl == "inputdown" || wl == "inputup")
                        return new Token { Type = TokenType.Func, Text = wl };

                    return new Token { Type = TokenType.Ident, Text = word };
                }

                // unrecognized: skip and continue
                _i++;
                return Next();
            }

            private void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                    _i++;
            }
        }

        private class ExprParser
        {
            private readonly Lexer _lexer;
            private Token _cur;

            public ExprParser(string text)
            {
                _lexer = new Lexer(text);
                _cur = _lexer.Next();
            }

            private void Next()
            {
                _cur = _lexer.Next();
            }

            public CondNode ParseExpression()
            {
                return ParseOr();
            }

            private CondNode ParseOr()
            {
                var left = ParseAnd();
                while (_cur.Type == TokenType.Or)
                {
                    Next();
                    var right = ParseAnd();
                    left = new CondNode { Type = CondNodeType.Or, Left = left, Right = right };
                }
                return left;
            }

            private CondNode ParseAnd()
            {
                var left = ParsePrimary();
                while (_cur.Type == TokenType.And)
                {
                    Next();
                    var right = ParsePrimary();
                    left = new CondNode { Type = CondNodeType.And, Left = left, Right = right };
                }
                return left;
            }

            private CondNode ParsePrimary()
            {
                if (_cur.Type == TokenType.LParen)
                {
                    Next();
                    var inner = ParseExpression();
                    if (_cur.Type == TokenType.RParen)
                        Next();
                    return inner;
                }

                if (_cur.Type == TokenType.Func)
                    return ParsePredicate();

                // unknown / error -> false leaf
                var pred = new InputPredicate { Kind = HandHeldInputKind.Held, Key = MyKeys.None };
                return new CondNode { Type = CondNodeType.Leaf, Predicate = pred };
            }

            private CondNode ParsePredicate()
            {
                string func = _cur.Text; // "input", "inputdown", "inputup"
                Next(); // consume func

                if (_cur.Type != TokenType.LParen)
                    return null;
                Next(); // '('

                if (_cur.Type != TokenType.Ident)
                    return null;

                string keyName = _cur.Text;
                Next(); // key

                if (_cur.Type == TokenType.RParen)
                    Next(); // ')'

                var pred = new InputPredicate
                {
                    Kind = FuncToKind(func),
                    Key = ParseKey(keyName)
                };

                return new CondNode { Type = CondNodeType.Leaf, Predicate = pred };
            }

            private HandHeldInputKind FuncToKind(string func)
            {
                func = func.ToLowerInvariant();
                if (func == "inputdown") return HandHeldInputKind.Down;
                if (func == "inputup") return HandHeldInputKind.Up;
                return HandHeldInputKind.Held;
            }

            private MyKeys ParseKey(string keyName)
            {
                MyKeys key;
                if (!Enum.TryParse(keyName, true, out key))
                {
                    Debug.Error($"IME handheld: Unknown key '{keyName}' in expression", informUser: true);
                    return MyKeys.None;
                }
                return key;
            }
        }
    }
}
