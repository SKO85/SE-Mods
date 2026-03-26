using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;

namespace SKONanobotBuildAndRepairSystem.Chat.Commands
{
    public static class SystemsCommand
    {
        public static ChatCommandResult Execute(string[] args)
        {
            if (args.Length < 2)
                return ShowHelp();

            switch (args[1])
            {
                case "list":
                    return ExecuteList(args);
                case "count":
                    return ExecuteCount();
                case "enable":
                    return ExecuteSetEnabled(args, true);
                case "disable":
                    return ExecuteSetEnabled(args, false);
                case "help":
                    return ShowHelp();
                default:
                    return ShowHelp();
            }
        }

        public static ChatCommandResult ShowHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Systems Commands (admin-only, server-side):");
            sb.AppendLine();
            sb.AppendLine("  /nanobars systems list");
            sb.AppendLine("    List all BaR blocks on the server.");
            sb.AppendLine();
            sb.AppendLine("  /nanobars systems list --owner <player-name>");
            sb.AppendLine("    List BaR blocks owned by a specific player.");
            sb.AppendLine();
            sb.AppendLine("  /nanobars systems count");
            sb.AppendLine("    Show BaR count per player and per faction.");
            sb.AppendLine();
            sb.AppendLine("  /nanobars systems enable all");
            sb.AppendLine("  /nanobars systems disable all");
            sb.AppendLine("    Enable/disable all BaR blocks on the server.");
            sb.AppendLine();
            sb.AppendLine("  /nanobars systems enable --grid <grid-name>");
            sb.AppendLine("  /nanobars systems disable --grid <grid-name>");
            sb.AppendLine("    Enable/disable all BaR blocks on a specific grid.");
            sb.AppendLine("    Grid name is case-insensitive and supports partial match.");
            sb.AppendLine();
            sb.AppendLine("  /nanobars systems enable --owner <player-name>");
            sb.AppendLine("  /nanobars systems disable --owner <player-name>");
            sb.AppendLine("    Enable/disable all BaR blocks owned by a specific player.");
            sb.AppendLine("    Player name is case-insensitive and supports partial match.");
            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", "Systems Help");
        }

        private static ChatCommandResult ExecuteList(string[] args)
        {
            // Parse optional --owner filter: "systems list --owner <name>"
            string ownerFilter = null;
            if (args.Length >= 4 && args[2] == "--owner")
            {
                var nameParts = new string[args.Length - 3];
                Array.Copy(args, 3, nameParts, 0, nameParts.Length);
                ownerFilter = string.Join(" ", nameParts);
                if (string.IsNullOrEmpty(ownerFilter))
                    return ChatCommandResult.Error("Usage: /nanobars systems list --owner <player-name>");
            }

            var sb = new StringBuilder();
            var count = 0;
            var maxItems = 50;

            foreach (var entry in Mod.NanobotSystems)
            {
                var system = entry.Value;
                if (system == null || system.Welder == null) continue;

                var welder = system.Welder;
                var ownerName = GetOwnerName(welder.OwnerId);

                if (ownerFilter != null && ownerName.IndexOf(ownerFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var grid = welder.CubeGrid;
                var gridName = grid != null ? grid.DisplayName : "Unknown";
                var enabled = welder.Enabled;
                var factionTag = GetFactionTag(welder.OwnerId);

                sb.AppendFormat("  {0}{1} | Grid: {2} | Owner: {3} [{4}]{5}",
                    enabled ? "[ON]  " : "[OFF] ",
                    welder.CustomName,
                    gridName,
                    ownerName,
                    factionTag,
                    Environment.NewLine);

                count++;
                if (count >= maxItems)
                {
                    sb.AppendFormat("  ... showing first {0}, more may exist{1}", maxItems, Environment.NewLine);
                    break;
                }
            }

            if (count == 0)
            {
                if (ownerFilter != null)
                    sb.AppendFormat("  No BaR blocks found for owner matching '{0}'.{1}", ownerFilter, Environment.NewLine);
                else
                    sb.AppendLine("  No BaR blocks found on the server.");
            }

            var header = ownerFilter != null
                ? string.Format("BaR Systems owned by '{0}': {1} found", ownerFilter, count)
                : string.Format("BaR Systems: {0} total", Mod.NanobotSystems.Count);
            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", header);
        }

        private static ChatCommandResult ExecuteCount()
        {
            var playerCounts = new Dictionary<string, int>();
            var factionCounts = new Dictionary<string, int>();
            var enabledCount = 0;

            foreach (var entry in Mod.NanobotSystems)
            {
                var system = entry.Value;
                if (system == null || system.Welder == null) continue;

                var welder = system.Welder;
                if (welder.Enabled) enabledCount++;

                var ownerName = GetOwnerName(welder.OwnerId);
                int pc;
                if (playerCounts.TryGetValue(ownerName, out pc))
                    playerCounts[ownerName] = pc + 1;
                else
                    playerCounts[ownerName] = 1;

                var factionTag = GetFactionTag(welder.OwnerId);
                int fc;
                if (factionCounts.TryGetValue(factionTag, out fc))
                    factionCounts[factionTag] = fc + 1;
                else
                    factionCounts[factionTag] = 1;
            }

            var sb = new StringBuilder();
            var total = Mod.NanobotSystems.Count;
            sb.AppendFormat("Total: {0} ({1} enabled, {2} disabled){3}{3}", total, enabledCount, total - enabledCount, Environment.NewLine);

            // Sort by count descending
            var playerList = new List<KeyValuePair<string, int>>(playerCounts);
            playerList.Sort((a, b) => b.Value.CompareTo(a.Value));

            sb.AppendFormat("By Player:{0}", Environment.NewLine);
            foreach (var kvp in playerList)
            {
                sb.AppendFormat("  {0}: {1}{2}", kvp.Key, kvp.Value, Environment.NewLine);
            }

            sb.AppendLine();

            var factionList = new List<KeyValuePair<string, int>>(factionCounts);
            factionList.Sort((a, b) => b.Value.CompareTo(a.Value));

            sb.AppendFormat("By Faction:{0}", Environment.NewLine);
            foreach (var kvp in factionList)
            {
                sb.AppendFormat("  [{0}]: {1}{2}", kvp.Key, kvp.Value, Environment.NewLine);
            }

            return ChatCommandResult.MissionScreen(sb.ToString(), "Nanobot Build and Repair System", "BaR System Count");
        }

        private static ChatCommandResult ExecuteSetEnabled(string[] args, bool enable)
        {
            // args: ["systems", "enable"/"disable", target...]
            // target: "all" | "--grid" <name> | "--owner" <name>
            if (args.Length < 3)
                return ChatCommandResult.Error(string.Format("Usage: /nanobars systems {0} all | --grid <name> | --owner <name>", enable ? "enable" : "disable"));

            var target = args[2];
            var action = enable ? "Enabled" : "Disabled";

            if (target == "all")
            {
                var count = 0;
                foreach (var entry in Mod.NanobotSystems)
                {
                    var system = entry.Value;
                    if (system == null || system.Welder == null) continue;
                    if (system.Welder.Enabled != enable)
                    {
                        system.Welder.Enabled = enable;
                        count++;
                    }
                }
                return ChatCommandResult.Success(string.Format("{0} {1} BaR block(s).", action, count));
            }

            if (target == "--grid")
            {
                if (args.Length < 4)
                    return ChatCommandResult.Error("Usage: /nanobars systems " + (enable ? "enable" : "disable") + " --grid <grid-name>");

                // Join remaining args to support grid names with spaces
                var gridNameParts = new string[args.Length - 3];
                Array.Copy(args, 3, gridNameParts, 0, gridNameParts.Length);
                var gridName = string.Join(" ", gridNameParts);
                if (string.IsNullOrEmpty(gridName))
                    return ChatCommandResult.Error("Usage: /nanobars systems " + (enable ? "enable" : "disable") + " --grid <grid-name>");

                var count = 0;
                var matched = false;
                foreach (var entry in Mod.NanobotSystems)
                {
                    var system = entry.Value;
                    if (system == null || system.Welder == null) continue;
                    var grid = system.Welder.CubeGrid;
                    if (grid == null) continue;

                    if (grid.DisplayName.IndexOf(gridName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matched = true;
                        if (system.Welder.Enabled != enable)
                        {
                            system.Welder.Enabled = enable;
                            count++;
                        }
                    }
                }

                if (!matched)
                    return ChatCommandResult.Error(string.Format("No BaR blocks found on grids matching '{0}'.", gridName));

                return ChatCommandResult.Success(string.Format("{0} {1} BaR block(s) on grids matching '{2}'.", action, count, gridName));
            }

            if (target == "--owner")
            {
                if (args.Length < 4)
                    return ChatCommandResult.Error("Usage: /nanobars systems " + (enable ? "enable" : "disable") + " --owner <player-name>");

                var playerNameParts = new string[args.Length - 3];
                Array.Copy(args, 3, playerNameParts, 0, playerNameParts.Length);
                var playerName = string.Join(" ", playerNameParts);
                if (string.IsNullOrEmpty(playerName))
                    return ChatCommandResult.Error("Usage: /nanobars systems " + (enable ? "enable" : "disable") + " --owner <player-name>");

                var count = 0;
                var matched = false;
                foreach (var entry in Mod.NanobotSystems)
                {
                    var system = entry.Value;
                    if (system == null || system.Welder == null) continue;

                    var ownerName = GetOwnerName(system.Welder.OwnerId);
                    if (ownerName.IndexOf(playerName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matched = true;
                        if (system.Welder.Enabled != enable)
                        {
                            system.Welder.Enabled = enable;
                            count++;
                        }
                    }
                }

                if (!matched)
                    return ChatCommandResult.Error(string.Format("No BaR blocks found owned by players matching '{0}'.", playerName));

                return ChatCommandResult.Success(string.Format("{0} {1} BaR block(s) owned by players matching '{2}'.", action, count, playerName));
            }

            return ChatCommandResult.Error(string.Format("Unknown target '{0}'. Use: all, --grid <name>, or --owner <name>", target));
        }

        private static string GetOwnerName(long ownerId)
        {
            if (ownerId == 0) return "Nobody";
            var player = UtilsPlayer.GetPlayer(ownerId);
            if (player != null) return player.DisplayName;
            // Player may be offline — try identity list
            var identities = new List<IMyIdentity>();
            MyAPIGateway.Players.GetAllIdentites(identities);
            foreach (var identity in identities)
            {
                if (identity.IdentityId == ownerId)
                    return identity.DisplayName;
            }
            return "Unknown (" + ownerId + ")";
        }

        private static string GetFactionTag(long ownerId)
        {
            if (ownerId == 0) return "---";
            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
            return faction != null ? faction.Tag : "---";
        }
    }
}
