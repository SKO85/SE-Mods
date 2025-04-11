using System;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace SKONanobotBuildAndRepairSystem
{
    public class Logging
    {
        private static Logging _instance;
        public static Logging Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new Logging("NanobotBuildAndRepairSystem", 0, "NanobotBuildAndRepairSystem.log", typeof(NanobotBuildAndRepairSystemMod));
                return _instance;
            }
        }

        private readonly string _modName;
        private readonly int _workshopId;
        private readonly string _logFileName;
        private readonly Type _typeOfMod;

        private System.IO.TextWriter _writer = null;
        private IMyHudNotification _notify = null;
        private int _indent = 0;
        private readonly StringBuilder _cache = new StringBuilder();

        public enum BlockNameOptions
        {
            None = 0x0000,
            IncludeTypename = 0x0001
        }

        [Flags]
        public enum Level
        {
            Error = 0x0001,
            Event = 0x0002,
            Info = 0x0004,
            Verbose = 0x0008,
            Special1 = 0x100000,
            Communication = 0x1000,
            All = 0xFFFF
        }

        public Level LogLevel { get; set; } = Level.Error;
        public bool EnableHudNotification { get; set; } = false;

        private Logging(string modName, int workshopId, string logFileName, Type typeOfMod)
        {
            _modName = modName;
            _workshopId = workshopId;
            _logFileName = logFileName;
            _typeOfMod = typeOfMod;

            MyLog.Default.WriteLineAndConsole(_modName + " Logger initialized. Utilities available: " + (MyAPIGateway.Utilities != null));
        }

        public static string BlockName(object block, BlockNameOptions options = BlockNameOptions.IncludeTypename, bool includeId = false)
        {
            if (block is IMyInventory) block = ((IMyInventory)block).Owner;

            var slim = block as IMySlimBlock;
            if (slim != null)
            {
                if (slim.FatBlock != null) block = slim.FatBlock;
                else return string.Format("{0}.{1}", slim.CubeGrid != null ? slim.CubeGrid.DisplayName : "Unknown Grid", slim.BlockDefinition.DisplayNameText);
            }

            var terminal = block as IMyTerminalBlock;
            if (terminal != null)
            {
                if ((options & BlockNameOptions.IncludeTypename) != 0)
                    return string.Format("{0}.{1} [{2}]", terminal.CubeGrid != null ? terminal.CubeGrid.DisplayName : "Unknown Grid", terminal.CustomName, terminal.BlockDefinition.TypeIdString);
                return string.Format("{0}.{1}", terminal.CubeGrid != null ? terminal.CubeGrid.DisplayName : "Unknown Grid", terminal.CustomName);
            }

            var cube = block as IMyCubeBlock;
            if (cube != null)
            {
                return string.Format("{0} [{1}/{2}]", cube.CubeGrid != null ? cube.CubeGrid.DisplayName : "Unknown Grid", cube.BlockDefinition.TypeIdString, cube.BlockDefinition.SubtypeName);
            }

            var entity = block as IMyEntity;
            if (entity != null)
            {
                if ((options & BlockNameOptions.IncludeTypename) != 0)
                    return string.Format("{0} ({1}) [{2}]", string.IsNullOrEmpty(entity.DisplayName) ? entity.GetFriendlyName() : entity.DisplayName, entity.EntityId, entity.GetType().Name);

                if(includeId)
                    return string.Format("{0} ({1})", entity.DisplayName, entity.EntityId);
                else
                    return string.Format("{0}", entity.DisplayName);
            }

            return block != null ? block.ToString() : "NULL";
        }

        public bool ShouldLog(Level level)
        {
            return (LogLevel & level) != 0;
        }

        public void IncreaseIndent(Level level)
        {
            if (ShouldLog(level)) _indent++;
        }

        public void DecreaseIndent(Level level)
        {
            if (ShouldLog(level) && _indent > 0) _indent--;
        }

        public void ResetIndent(Level level)
        {
            if (ShouldLog(level)) _indent = 0;
        }

        public void Error(Exception e)
        {
            Error(e.ToString());
        }

        public void Error(string msg, params object[] args)
        {
            Error(string.Format(msg, args));
        }

        private void Error(string msg)
        {
            if (!ShouldLog(Level.Error)) return;

            Write("ERROR: " + msg);

            try
            {
                MyLog.Default.WriteLineAndConsole(_modName + " error: " + msg);

                if (EnableHudNotification)
                {
                    ShowOnHud(_modName + " error - see log for details");
                }
            }
            catch (Exception e)
            {
                Write("ERROR: Could not send HUD notification: " + e);
            }
        }

        public void Write(Level level, string msg, params object[] args)
        {
            if (!ShouldLog(level)) return;
            Write(string.Format(msg, args));
        }

        public void Write(string msg, params object[] args)
        {
            Write(string.Format(msg, args));
        }

        private void Write(string msg)
        {
            try
            {
                lock (_cache)
                {
                    _cache.Append(DateTime.Now.ToString("u")).Append(":");
                    for (int i = 0; i < _indent; i++) _cache.Append("   ");
                    _cache.Append(msg).AppendLine();

                    if (_writer == null && MyAPIGateway.Utilities != null)
                    {
                        _writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(_logFileName, _typeOfMod);
                    }

                    if (_writer != null)
                    {
                        _writer.Write(_cache);
                        _writer.Flush();
                        _cache.Clear();
                    }
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole(_modName + " Logging failure: " + e);
            }
        }

        public void ShowOnHud(string text, int displayMs = 10000)
        {
            if (_notify == null)
            {
                _notify = MyAPIGateway.Utilities.CreateNotification("", displayMs, "Red");
            }

            _notify.Text = text;
            _notify.Show();
        }

        public void Close()
        {
            try
            {
                if (_writer != null)
                {
                    _writer.Flush();
                    _writer.Close();
                }
            }
            catch { }
        }
    }
}
