using System;
using System.Text;
using Sandbox.ModAPI;
using VRage.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace SpaceEquipmentLtd.Utils
{
   public class Logging
   {
      private string _ModName;
      private int _WorkshopId;
      private string _LogFilename;
      private Type _TypeOfMod;

      private System.IO.TextWriter _Writer = null;
      private IMyHudNotification _Notify = null;
      private int _Indent = 0;
      private StringBuilder _Cache = new StringBuilder();

      [Flags]
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

      public static string BlockName(object block)
      {
         return BlockName(block, BlockNameOptions.IncludeTypename);
      }
      public static string BlockName(object block, BlockNameOptions options)
      {
         var inventory = block as IMyInventory;
         if (inventory != null)
         {
            block = inventory.Owner;
         }

         var slimBlock = block as IMySlimBlock;
         if (slimBlock != null)
         {
            if (slimBlock.FatBlock != null) block = slimBlock.FatBlock;
            else
            {
               return string.Format("{0}.{1}", slimBlock.CubeGrid != null ? slimBlock.CubeGrid.DisplayName : "Unknown Grid", slimBlock.BlockDefinition.DisplayNameText);
            }
         }

         var terminalBlock = block as IMyTerminalBlock;
         if (terminalBlock != null)
         {
            if ((options & BlockNameOptions.IncludeTypename) != 0)  return string.Format("{0}.{1} [{2}]", terminalBlock.CubeGrid != null ? terminalBlock.CubeGrid.DisplayName : "Unknown Grid", terminalBlock.CustomName, terminalBlock.BlockDefinition.TypeIdString);
            return string.Format("{0}.{1}", terminalBlock.CubeGrid != null ? terminalBlock.CubeGrid.DisplayName : "Unknown Grid", terminalBlock.CustomName);
         }

         var cubeBlock = block as IMyCubeBlock;
         if (cubeBlock != null)
         {
            return string.Format("{0} [{1}/{2}]", cubeBlock.CubeGrid != null ? cubeBlock.CubeGrid.DisplayName : "Unknown Grid", cubeBlock.BlockDefinition.TypeIdString, cubeBlock.BlockDefinition.SubtypeName);
         }

         var entity = block as IMyEntity;
         if (entity != null)
         {
            if ((options & BlockNameOptions.IncludeTypename) != 0) return string.Format("{0} ({1}) [{2}]", string.IsNullOrEmpty(entity.DisplayName) ? entity.GetFriendlyName() : entity.DisplayName, entity.EntityId, entity.GetType().Name);
            return string.Format("{0} ({1})", entity.DisplayName, entity.EntityId);
         }

         return block != null ? block.ToString() : "NULL";
      }

      public Level LogLevel { get; set; }

      public bool EnableHudNotification { get; set; }

      /// <summary>
      /// 
      /// </summary>
      public Logging(string modName, int workshopId, string logFileName, Type typeOfMod)
      {
         MyLog.Default.WriteLineAndConsole(_ModName + " Create Log instance Utils=" + (MyAPIGateway.Utilities != null).ToString());
         _ModName = modName;
         _WorkshopId = workshopId;
         _LogFilename = logFileName;
         _TypeOfMod = typeOfMod;
      }

      /// <summary>
      /// Precheckl to avoid retriveing large amout of data,
      /// that might be not needed afterwards
      /// </summary>
      /// <param name="level"></param>
      /// <returns></returns>
      public bool ShouldLog(Level level)
      {
         return (LogLevel & level) != 0;
      }

      /// <summary>
      /// 
      /// </summary>
      public void IncreaseIndent(Level level)
      {
         if ((LogLevel & level) != 0) _Indent++;
      }

      /// <summary>
      /// 
      /// </summary>
      public void DecreaseIndent(Level level)
      {
         if ((LogLevel & level) != 0)
            if (_Indent > 0) _Indent--;
      }

      /// <summary>
      /// 
      /// </summary>
      public void ResetIndent(Level level)
      {
         if ((LogLevel & level) != 0) _Indent = 0;
      }

      /// <summary>
      ///
      /// </summary>
      public void Error(Exception e)
      {
         Error(e.ToString());
      }

      /// <summary>
      ///
      /// </summary>
      public void Error(string msg, params object[] args)
      {
         Error(string.Format(msg, args));
      }

      /// <summary>
      /// 
      /// </summary>
      private void Error(string msg)
      {
         if ((LogLevel & Level.Error) == 0) return;

         Write("ERROR: " + msg);

         try
         {
            MyLog.Default.WriteLineAndConsole(_ModName + " error: " + msg);

            string text = _ModName + " error - open %AppData%/SpaceEngineers/Storage/" + _LogFilename + " for details";

            if (EnableHudNotification)
            {
               ShowOnHud(text);
            }
         }
         catch (Exception e)
         {
            Write(string.Format("ERROR: Could not send notification to local client: " + e.ToString()));
         }
      }

      /// <summary>
      /// 
      /// </summary>
      public void Write(Level level, string msg, params Object[] args)
      {
         if ((LogLevel & level) == 0) return;
         Write(string.Format(msg, args));
      }

      /// <summary>
      /// 
      /// </summary>
      public void Write(string msg, params Object[] args)
      {
         Write(string.Format(msg, args));
      }

      /// <summary>
      /// 
      /// </summary>
      private void Write(string msg)
      {
         try
         {
            lock (_Cache)
            {
               _Cache.Append(DateTime.Now.ToString("u") + ":");

               for (int i = 0; i < _Indent; i++)
               {
                  _Cache.Append("   ");
               }

               _Cache.Append(msg).AppendLine();

               if (_Writer == null && MyAPIGateway.Utilities != null)
               {
                  _Writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(_LogFilename, _TypeOfMod);
               }

               if (_Writer != null)
               {
                  _Writer.Write(_Cache);
                  _Writer.Flush();
                  _Cache.Clear();
               }
            }
         }
         catch (Exception e)
         {
            MyLog.Default.WriteLineAndConsole(_ModName + " Error while logging message='" + msg + "'\nLogger error: " + e.Message + "\n" + e.StackTrace);
         }
      }

      /// <summary>
      /// 
      /// </summary>
      /// <param name="text"></param>
      /// <param name="displayms"></param>
      public void ShowOnHud(string text, int displayms = 10000)
      {
         if (_Notify == null)
         {
            _Notify = MyAPIGateway.Utilities.CreateNotification(text, displayms, MyFontEnum.Red);
         }
         else
         {
            _Notify.Text = text;
            _Notify.ResetAliveTime();
         }

         _Notify.Show();
      }

      /// <summary>
      /// 
      /// </summary>
      public void Close()
      {
         lock (_Cache)
         {
            if (_Writer != null)
            {
               _Writer.Flush();
               _Writer.Close();
               _Writer.Dispose();
               _Writer = null;
            }

            _Indent = 0;
            _Cache.Clear();
         }
      }
   }
}
