using System;
using System.Linq;
using System.Collections.Generic;
using SoulsFormats;

namespace SoulsIds
{
    public class ESDEdits
    {
        // Much more specific utilities than AST
        public static List<long> FindMachinesWithTalkData(ESD esd, int msgId)
        {
            List<long> ret = new List<long>();
            bool hasTalkData(ESD.CommandCall c)
            {
                AST.Expr arg;
                // AddTalkListData c1_19, AddTalkListDataIf c5_19
                if (c.CommandBank == 1 && c.CommandID == 19 && c.Arguments.Count > 1)
                {
                    arg = AST.DisassembleExpression(c.Arguments[1]);
                }
                else if (c.CommandBank == 5 && c.CommandID == 19 && c.Arguments.Count > 2)
                {
                    arg = AST.DisassembleExpression(c.Arguments[2]);
                }
                else return false;
                return arg.TryAsInt(out int argId) && argId == msgId;
            }
            foreach (KeyValuePair<long, Dictionary<long, ESD.State>> machineEntry in esd.StateGroups)
            {
                foreach (KeyValuePair<long, ESD.State> stateEntry in machineEntry.Value)
                {
                    ESD.State state = stateEntry.Value;
                    if (state.EntryCommands.Any(hasTalkData))
                    {
                        ret.Add(machineEntry.Key);
                    }
                }
            }
            return ret;
        }

        public class CustomTalkData
        {
            public int Msg { get; set; }
            public int ConsistentID { get; set; }
            // Optional
            public AST.Expr Condition { get; set; }
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
            ESD.CommandCall newEntry = data.Condition == null
                ? AST.MakeCommand(1, 19, talkListId, data.Msg, -1)
                : AST.MakeCommand(5, 19, data.Condition, talkListId, data.Msg, -1);
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
    }
}
