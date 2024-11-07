using System;
using System.Linq;
using System.Collections.Generic;
using SoulsFormats;
using System.Globalization;
using Org.BouncyCastle.Ocsp;
using static SoulsFormats.MSB.Shape;

namespace SoulsIds
{
    public class ESDEdits
    {
        public enum ComparisonType
        {
            Equal = 0,
            NotEqual = 1,
            Greater = 2,
            Less = 3,
            GreaterOrEqual = 4,
            LessOrEqual = 5,
        }
        public enum EldenStat
        {
            Vigor = 0,
            Mind = 1,
            End = 2,
            Str = 3,
            Dex = 4,
            Int = 5,
            Faith = 6,
            Arcane = 7,
            Runes = 8,
            RuneLevel = 33,
        }
        public static readonly List<EldenStat> LevelStats = new List<EldenStat>
        {
            // Vigor is broken and points to Vitality instead
            EldenStat.Mind, EldenStat.End, EldenStat.Str, EldenStat.Dex, EldenStat.Int, EldenStat.Faith, EldenStat.Arcane,
        };
        public enum ChangeType
        {
            Add = 0,
            Subtract = 1,
            Multiplayer = 2,
            Divide = 3,
            Modulus = 4,
            Set = 5,
        }

        // Used to chain the state into the next one
        public static void ShowDialog(ESD.State state, long nextId, int msg)
        {
            // c1_17 OpenGenericDialog
            state.EntryCommands.Add(AST.MakeCommand(1, 17, 7, msg, 1, 0, 1));
            // f58 CheckSpecificPersonGenericDialogIsOpen
            AST.Expr noDialogExpr = new AST.BinaryExpr { Op = "==", Lhs = AST.MakeFunction("f58", 0), Rhs = AST.MakeVal(0) };
            state.Conditions.Add(new ESD.Condition(nextId, AST.AssembleExpression(noDialogExpr)));
        }

        // Check the result with f22 GetGenericDialogButtonResult
        public static void ShowConfirmationDialog(ESD.State state, long nextId, int msg)
        {
            // c1_17 OpenGenericDialog
            state.EntryCommands.Add(AST.MakeCommand(1, 17, 8, msg, 3, 4, 2));
            // f58 CheckSpecificPersonGenericDialogIsOpen
            AST.Expr noDialogExpr = new AST.BinaryExpr { Op = "==", Lhs = AST.MakeFunction("f58", 0), Rhs = AST.MakeVal(0) };
            state.Conditions.Add(new ESD.Condition(nextId, AST.AssembleExpression(noDialogExpr)));
        }

        // Returns trailing states corresponding to each message, then another one for cancelling out of the menu.
        public static List<ESD.State> OpenSubmenu(Dictionary<long, ESD.State> machine, ESD.State state, List<(int, AST.Expr)> msgs, ref long optId, int startId = 0)
        {
            // There's also ClearTalkActionState, can probably be ignored? Also try without c1_110 for now, it's already been used.
            // c1_20 ClearTalkListData()
            state.EntryCommands.Add(AST.MakeCommand(1, 20));
            // c1_110 MainMenuFlag()
            // nextCheckState.EntryCommands.Add(AST.MakeCommand(1, 110));

            // Every item has several talk list entries with (expr, branch id), and each branch has a set of items.
            // AddTalkListDataIf could also be used, but the conditions make it simpler to nest it exclusively.
            List<AST.Expr> talkListConds = new List<AST.Expr>();
            // List<List<(SlotKey, int)>> branchSlots = new List<List<(SlotKey, int)>>();
            foreach ((int msgId, AST.Expr cond) in msgs)
            {
                int talkListId = talkListConds.Count + 1 + startId;
                talkListConds.Add(new AST.BinaryExpr { Op = "==", Lhs = AST.MakeFunction("f23"), Rhs = AST.MakeVal(talkListId) });
                if (cond == null)
                {
                    state.EntryCommands.Add(AST.MakeCommand(1, 19, talkListId, msgId, -1));
                }
                else
                {
                    state.EntryCommands.Add(AST.MakeCommand(5, 19, cond, talkListId, msgId, -1));
                }
            }
            talkListConds.Add(AST.Pass);

            // Return to talk list entries and connect everything together
            // c1_10 ShowShopMessage(1)
            state.EntryCommands.Add(AST.MakeCommand(1, 10, 1));
            // assert not (CheckSpecificPersonMenuIsOpen(1, 0) == 1 and not CheckSpecificPersonGenericDialogIsOpen(0)) ->
            // f59 f58 assert CheckSpecificPersonMenuIsOpen(1, 0) == 0 or CheckSpecificPersonGenericDialogIsOpen(0)
            AST.Expr waitExpr = new AST.BinaryExpr
            {
                Op = "||",
                Lhs = AST.NegateCond(AST.MakeFunction("f59", 1, 0)),
                Rhs = AST.MakeFunction("f58", 0),
            };
            (long selectStateId, ESD.State selectState) = AST.AllocateState(machine, ref optId);
            state.Conditions.Add(new ESD.Condition(selectStateId, AST.AssembleExpression(waitExpr)));
            return AST.AllocateBranch(machine, selectState, talkListConds, ref optId);
        }

        // Returns trailing states corresponding to each message, then another one for cancelling out of the menu.
        public static List<ESD.State> OpenOptionMenu(Dictionary<long, ESD.State> machine, ESD.State state, List<int> msgs, ref long optId)
        {
            // c1_20 ClearTalkListData()
            state.EntryCommands.Add(AST.MakeCommand(1, 20));
            // c1_110 MainMenuFlag()
            state.EntryCommands.Add(AST.MakeCommand(1, 110));
            List<AST.Expr> condExprs = new List<AST.Expr>();
            for (int i = 0; i < msgs.Count; i++)
            {
                // c1_19 AddTalkListData(id, msg, -1)
                state.EntryCommands.Add(AST.MakeCommand(1, 19, i + 1, msgs[i], -1));
                // f23 GetTalkListEntryResult()
                condExprs.Add(new AST.BinaryExpr { Op = "==", Lhs = AST.MakeFunction("f23"), Rhs = AST.MakeVal(i + 1) });
            }
            condExprs.Add(AST.Pass);
            // c1_76 OpenConversationChoicesMenu(0)
            state.EntryCommands.Add(AST.MakeCommand(1, 76, 0));
            // f59 f58 assert CheckSpecificPersonMenuIsOpen(12, 0) == 0 or CheckSpecificPersonGenericDialogIsOpen(0)
            AST.Expr waitExpr = MenuCloseExpr(12);
            (long selectStateId, ESD.State selectState) = AST.AllocateState(machine, ref optId);
            state.Conditions.Add(new ESD.Condition(selectStateId, AST.AssembleExpression(waitExpr)));
            return AST.AllocateBranch(machine, selectState, condExprs, ref optId);
        }

        public static AST.Expr MenuCloseExpr(int type)
        {
            // f59 f58 assert CheckSpecificPersonMenuIsOpen(type, 0) == 0 or CheckSpecificPersonGenericDialogIsOpen(0)
            return new AST.BinaryExpr
            {
                Op = "||",
                Lhs = AST.NegateCond(AST.MakeFunction("f59", type, 0)),
                Rhs = AST.MakeFunction("f58", 0),
            };
        }

        public static AST.Expr CheckDialogResult(int result)
        {
            // f22 GetGenericDialogButtonResult
            return new AST.BinaryExpr
            {
                Op = "==",
                Lhs = AST.MakeFunction("f22"),
                Rhs = AST.MakeVal(result),
            };
        }

        // Much more specific utilities than AST
        public static List<long> FindMachinesWithCondition(ESD esd, Func<AST.Expr, bool> predicate)
        {
            List<long> ret = new List<long>();
            bool checkCond(ESD.Condition cond)
            {
                AST.Expr expr = AST.DisassembleExpression(cond.Evaluator);
                if (predicate(expr)) return true;
                return cond.Subconditions.Any(checkCond);
            }
            foreach (KeyValuePair<long, Dictionary<long, ESD.State>> machineEntry in esd.StateGroups)
            {
                foreach (KeyValuePair<long, ESD.State> stateEntry in machineEntry.Value)
                {
                    ESD.State state = stateEntry.Value;
                    if (state.Conditions.Any(checkCond))
                    {
                        ret.Add(machineEntry.Key);
                        break;
                    }
                }
            }
            return ret;
        }

        public static void ForEachCondition(ESD esd, Action<ESD.Condition> action)
        {
            void checkCond(ESD.Condition cond)
            {
                action(cond);
                cond.Subconditions.ForEach(checkCond);
            }
            foreach (KeyValuePair<long, Dictionary<long, ESD.State>> machineEntry in esd.StateGroups)
            {
                foreach (KeyValuePair<long, ESD.State> stateEntry in machineEntry.Value)
                {
                    ESD.State state = stateEntry.Value;
                    state.Conditions.ForEach(checkCond);
                }
            }
        }

        public static List<long> FindMachinesWithPassCommand(ESD esd, Func<ESD.CommandCall, bool> predicate)
        {
            List<long> ret = new List<long>();
            bool checkCond(ESD.Condition cond)
            {
                if (cond.PassCommands.Any(predicate)) return true;
                return cond.Subconditions.Any(checkCond);
            }
            foreach (KeyValuePair<long, Dictionary<long, ESD.State>> machineEntry in esd.StateGroups)
            {
                foreach (KeyValuePair<long, ESD.State> stateEntry in machineEntry.Value)
                {
                    ESD.State state = stateEntry.Value;
                    if (state.Conditions.Any(checkCond))
                    {
                        ret.Add(machineEntry.Key);
                        break;
                    }
                }
            }
            return ret;
        }

        public static List<long> FindMachinesWithCommand(ESD esd, Func<ESD.CommandCall, bool> predicate)
        {
            List<long> ret = new List<long>();
            foreach (KeyValuePair<long, Dictionary<long, ESD.State>> machineEntry in esd.StateGroups)
            {
                foreach (KeyValuePair<long, ESD.State> stateEntry in machineEntry.Value)
                {
                    ESD.State state = stateEntry.Value;
                    if (state.EntryCommands.Any(predicate) || state.WhileCommands.Any(predicate) || state.ExitCommands.Any(predicate))
                    {
                        ret.Add(machineEntry.Key);
                        break;
                    }
                }
            }
            return ret;
        }

        public static List<long> FindMachinesWithTalkData(ESD esd, int msgId)
        {
            bool hasTalkData(ESD.CommandCall c)
            {
                // AddTalkListData c1_19, AddTalkListDataIf c5_19
                if (c.CommandID != 19) return false;
                AST.Expr arg;
                if (c.CommandBank == 1 && c.Arguments.Count > 1)
                {
                    arg = AST.DisassembleExpression(c.Arguments[1]);
                }
                else if (c.CommandBank == 5 && c.Arguments.Count > 2)
                {
                    arg = AST.DisassembleExpression(c.Arguments[2]);
                }
                else return false;
                return arg.IsInt(msgId);
            }
            return FindMachinesWithCommand(esd, hasTalkData);
        }

        public class CustomTalkData
        {
            public int Msg { get; set; }
            public int ConsistentID { get; set; }
            // Optional
            public AST.Expr Condition { get; set; }
            public AST.Expr HighlightCondition { get; set; }
            // Game-wide metadata
            public int LeaveMsg { get; set; }
        }

        // Way too complex, but basically handles adding a talk list entry
        public static bool ModifyCustomTalkEntry(
            Dictionary<long, ESD.State> machine,
            CustomTalkData data,
            bool install,
            bool uninstall,
            out long resultStateId)
        {
            resultStateId = -1;
            int talkListId = data.ConsistentID;
            int talkState = data.ConsistentID;
            bool isInstalled = false;

            // Required
            long loopId = -1;
            long entryId = -1;
            long checkId = -1;
            foreach (KeyValuePair<long, ESD.State> stateEntry in machine)
            {
                ESD.State state = stateEntry.Value;
                // ClearTalkListData c1_20
                if (state.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == 20)) loopId = stateEntry.Key;
                // AddTalkListData c1_19
                // There may be multiple states like this, so finding the last one is fine
                // Leave should usually always be present, so no need to search for c5_19 probably
                if (state.EntryCommands.Any(c => c.CommandBank == 1 && c.CommandID == 19)) entryId = stateEntry.Key;
                // GetTalkListEntryResult f23 == 3 condition
                foreach (ESD.Condition cond in state.Conditions)
                {
                    bool found = false;
                    AST.AstVisitor talkListEntryVisitor = AST.AstVisitor.PostAct(expr =>
                    {
                        found |= expr is AST.FunctionCall call && call.Name == "f23";
                    });
                    AST.DisassembleExpression(cond.Evaluator).Visit(talkListEntryVisitor);
                    if (found)
                    {
                        checkId = stateEntry.Key;
                        break;
                    }
                }
            }
            if (loopId == -1 || entryId == -1 || checkId == -1)
            {
                if (install)
                {
                    throw new InvalidOperationException($"Can't install mod {data.Msg}: ESD missing states {loopId} {entryId} {checkId}");
                }
                // If it can't be installed, don't count it as such
                return false;
            }
            bool isTalkEntry(ESD.CommandCall c, int findMsg)
            {
                if (findMsg == -1) return (c.CommandBank == 1 || c.CommandBank == 5) && c.CommandID == 19;
                return c.CommandBank == 1 && c.CommandID == 19 && c.Arguments.Count == 3
                    && AST.DisassembleExpression(c.Arguments[1]) is AST.ConstExpr con
                    && con.AsInt() == findMsg;
            }

            // Search for existing talk list entry
            ESD.CommandCall existingEntry = machine[entryId].EntryCommands.Find(c => isTalkEntry(c, data.Msg));
            isInstalled = existingEntry != null;

            if (!install && !uninstall) return isInstalled;

            // If it exists, try to uninstall it
            List<int> usedTalkIds = new List<int>();
            if (isInstalled)
            {
                machine[entryId].EntryCommands.Remove(existingEntry);
                int findCheck = -1;
                if (AST.DisassembleExpression(existingEntry.Arguments[0]) is AST.ConstExpr talkCon)
                {
                    findCheck = talkCon.AsInt();
                }
                // Find condition
                ESD.Condition existingCond = null;
                foreach (ESD.Condition cond in machine[checkId].Conditions)
                {
                    int talkCheck = -1;
                    AST.AstVisitor talkListEntryVisitor = AST.AstVisitor.PostAct(expr =>
                    {
                        // For the moment, check for things of the form GetTalkListEntryResult() == 7
                        if (expr is AST.BinaryExpr bin
                            && bin.Lhs is AST.FunctionCall call && call.Name == "f23"
                            && bin.Rhs is AST.ConstExpr con)
                        {
                            talkCheck = con.AsInt();
                        }
                    });
                    AST.DisassembleExpression(cond.Evaluator).Visit(talkListEntryVisitor);
                    if (talkCheck != -1)
                    {
                        usedTalkIds.Add(talkCheck);
                        if (existingCond == null && talkCheck == findCheck)
                        {
                            existingCond = cond;
                        }
                    }
                }
                if (existingCond != null)
                {
                    machine[checkId].Conditions.Remove(existingCond);
                    if (existingCond.TargetState is long destState)
                    {
                        machine.Remove(destState);
                    }
                }
            }
            if (!install) return isInstalled;

            // If we're installing, find an unused state and talk list id
            while (machine.ContainsKey(talkState)) talkState++;
            while (usedTalkIds.Contains(talkListId)) talkListId++;

            // Add new talk list entry
            ESD.State entryState = machine[entryId];
            int leaveEntry = entryState.EntryCommands.FindLastIndex(c => isTalkEntry(c, data.LeaveMsg));
            if (leaveEntry == -1)
            {
                // Prefer to put it before the "Leave" command
                // Otherwise, avoid interfering with non-talk commands, if possible
                leaveEntry = entryState.EntryCommands.FindLastIndex(c => isTalkEntry(c, -1));
                leaveEntry = leaveEntry == -1 ? entryState.EntryCommands.Count : leaveEntry + 1;
            }
            // c1_149(talkListId, data.Msg, -1, 0, data.HighlightCondition)
            ESD.CommandCall newEntry;
            if (data.HighlightCondition == null)
            {
                if (data.Condition == null)
                {
                    newEntry = AST.MakeCommand(1, 19, talkListId, data.Msg, -1);
                }
                else
                {
                    newEntry = AST.MakeCommand(5, 19, data.Condition, talkListId, data.Msg, -1);
                }
            }
            else
            {
                if (data.Condition == null)
                {
                    newEntry = AST.MakeCommand(1, 149, talkListId, data.Msg, -1, 0, data.HighlightCondition);
                }
                else
                {
                    newEntry = AST.MakeCommand(5, 149, data.Condition, talkListId, data.Msg, -1, 0, data.HighlightCondition);
                }
            }
            entryState.EntryCommands.Insert(leaveEntry, newEntry);

            long baseId = talkState;
            ESD.State resultState;
            (resultStateId, resultState) = AST.AllocateState(machine, ref baseId);
            resultState.Conditions.Add(new ESD.Condition(loopId, null));

            // Add talk condition for state
            AST.Expr buyCond = new AST.BinaryExpr { Op = "==", Lhs = AST.MakeFunction("f23"), Rhs = AST.MakeVal(talkListId) };
            machine[checkId].Conditions.Insert(0, new ESD.Condition(resultStateId, AST.AssembleExpression(buyCond)));

            return isInstalled;
        }

        public class MenuTree<T> where T : MenuTree<T>
        {
            // The display name for the menu
            public string Name { get; set; }
            // A more specific ID, in case a name is used for multiple different menus
            public string ID { get; set; }
            // Reference to the parent's UniqueID
            public string ParentID { get; set; }
            // Message id in EventTextForTalk. If not given, getTextId is invoked.
            public int MsgID { get; set; }
            // This is only given as a menu
            public List<T> Sub { get; set; }

            // ID should be unique
            internal string UniqueID => ID ?? Name;

        }

        public static void MakeMenuTree<T>(
            Dictionary<long, ESD.State> machine, long startStateId, long endStateId, List<T> flatMenus,
            // Text to FMG id for talk list data
            Func<string, int> getTextId,
            // Menu tree, starting state, and required target state id
            Action<T, ESD.State, long> runState) where T : MenuTree<T>
        {
            List<T> menuTrees = flatMenus.ToList();
            Dictionary<string, T> allMenus = menuTrees.ToDictionary(s => s.UniqueID, s => s);
            Dictionary<string, List<T>> subs = new();
            menuTrees.RemoveAll(s =>
            {
                if (s.ParentID != null)
                {
                    if (!subs.TryGetValue(s.ParentID, out List<T> existSubs))
                    {
                        subs[s.ParentID] = existSubs = new();
                    }
                    existSubs.Add(s);
                    return true;
                }
                return false;
            });
            foreach ((string name, List<T> sub) in subs)
            {
                if (!allMenus.TryGetValue(name, out T parent))
                {
                    throw new Exception($"Internal error: missing parent menu {name}");
                }
                parent.Sub = sub;
            }
            long refId = startStateId;
            int talkListId = 0;
            void makeMenu(long shopMenuId, long parentId, List<T> menus)
            {
                ESD.State shopMenuState = machine[shopMenuId];
                List<(int, AST.Expr)> menuItems = new();
                foreach (T menu in menus)
                {
                    int catId = menu.MsgID > 0 ? menu.MsgID : getTextId(menu.Name);
                    menuItems.Add((catId, null));
                }
                List<ESD.State> states = ESDEdits.OpenSubmenu(machine, shopMenuState, menuItems, ref refId, talkListId);
                talkListId += menuItems.Count;
                for (int i = 0; i < states.Count; i++)
                {
                    if (i < menus.Count)
                    {
                        T menu = menus[i];
                        if (menu.Sub != null)
                        {
                            (long subStateId, ESD.State subState) = AST.AllocateState(machine, ref refId);
                            AST.CallState(states[i], subStateId);
                            makeMenu(subStateId, shopMenuId, menu.Sub);
                        }
                        else
                        {
                            runState(menu, states[i], shopMenuId);
                        }
                    }
                    else
                    {
                        AST.CallState(states[i], parentId);
                    }
                }
            }
            makeMenu(startStateId, endStateId, menuTrees);
        }
    }
}
