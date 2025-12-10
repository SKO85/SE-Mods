using System;
using System.Collections.Generic;
using System.Linq;
using VRage.ModAPI;
using VRage.Utils;

namespace SpaceEquipmentLtd.Utils
{
   public class PrioItem
   {
      public int Key;
      public string Alias;
      public PrioItem(int key, string alias)
      {
         Key = key;
         Alias = alias;
      }
      public override string ToString() 
      {
         return Alias;
      }
   }

   public class PrioItemState<T> where T : PrioItem
   {
      public T PrioItem { get; }
      public bool Enabled { get; set; }
      public bool Visible { get; set; }
      public PrioItemState(T prioItem, bool enabled, bool visible)
      {
         PrioItem = prioItem;
         Enabled = enabled;
         Visible = visible;
      }
   }

   public abstract class PriorityHandling<C, I> : List<PrioItemState<C>> where C: PrioItem //where C : struct
   {
      private bool _HashDirty = true;
      private List<string> _ClassList = new List<string>();
      private Dictionary<int, int> _PrioHash = new Dictionary<int, int>();

      public C Selected { get; private set; } //Visual

      /// <summary>
      /// Set current active item
      /// </summary>
      public void SetSelectedByKey(int key)
      {
         var item = this.Find( i => i.PrioItem.Key == key);
         if (item != null) Selected = item.PrioItem;
         else Selected = null;
      }

      /// <summary>
      /// Clear current active item
      /// </summary>
      public void ClearSelected()
      {
         Selected = null;
      }

      /// <summary>
      /// Retrieve the build/repair priority of the item.
      /// </summary>
      internal int GetPriority(I a)
      {
         var itemKey = GetItemKey(a, false);
         if (_HashDirty) UpdateHash();
         return _PrioHash[itemKey];
      }

      /// <summary>
      /// Retrieve if the build/repair of this item kind is enabled.
      /// </summary>
      internal bool GetEnabled(I a)
      {
         var itemKey = GetItemKey(a, true);
         if (_HashDirty) UpdateHash();
         return _PrioHash[itemKey] < int.MaxValue;
      }

      /// <summary>
      /// Retrieve if the build/repair of this item kind is enabled.
      /// </summary>
      //internal bool GetEnabled(C a)
      //{
      //   if (_HashDirty) UpdateHash();
      //   return _PrioHash[a.Key] < int.MaxValue;
      //}

      /// <summary>
      /// Get the item key value
      /// </summary>
      /// <param name="a"></param>
      /// <returns></returns>
      public abstract int GetItemKey(I a, bool real);

      /// <summary>
      /// Get the item alias
      /// </summary>
      /// <param name="a"></param>
      /// <returns></returns>
      public abstract string GetItemAlias(I a, bool real);

      /// <summary>
      /// 
      /// </summary>
      /// <param name="items"></param>
      internal void FillTerminalList(List<MyTerminalControlListBoxItem> items, List<MyTerminalControlListBoxItem> selected)
      {
         foreach (var entry in this)
         {
            if (entry.Visible) {
               var item = new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(string.Format("({0}) {1}", entry.Enabled ? "X" : "-", entry.PrioItem.ToString())), MyStringId.NullOrEmpty, entry.PrioItem);
               items.Add(item);

               if (entry.PrioItem.Equals(Selected))
               {
                  selected.Add(item);
               }
            }
         }
      }

      internal void MoveSelectedUp()
      {
         if (Selected != null)
         {
            var currentPrio = FindIndex((kv) => kv.PrioItem.Equals(Selected));
            if (currentPrio > 0)
            {
               this.Move(currentPrio, currentPrio - 1);
               _HashDirty = true;
            }
         }
      }

      internal void MoveSelectedDown()
      {
         if (Selected != null)
         {
            var currentPrio = FindIndex((kv) => kv.PrioItem.Equals(Selected));
            if (currentPrio >= 0 && currentPrio < Count - 1)
            {
               this.Move(currentPrio, currentPrio + 1);
               _HashDirty = true;
            }
         }
      }

      internal void ToggleEnabled()
      {
         if (Selected != null)
         {
            var keyValue = this.FirstOrDefault((kv) => kv.PrioItem.Equals(Selected));
            if (keyValue != null)
            {
               keyValue.Enabled = !keyValue.Enabled;
               _HashDirty = true;
            }
         }
      }

      internal int GetPriority(int itemKey)
      {
         return FindIndex((kv) => kv.PrioItem.Key == itemKey);
      }

      internal void SetPriority(int itemKey, int prio)
      {
         if (prio >= 0 && prio < Count)
         {
            var currentPrio = FindIndex((kv) => kv.PrioItem.Key == itemKey);
            if (currentPrio >= 0)
            {
               this.Move(currentPrio, prio);
               _HashDirty = true;
            }
         }
      }

      internal bool GetEnabled(int itemKey)
      {
         var keyValue = this.FirstOrDefault((kv) => kv.PrioItem.Key == itemKey);
         return keyValue != null ? keyValue.Enabled : false;
      }

      internal void SetEnabled(int itemKey, bool enabled)
      {
         var keyValue = this.FirstOrDefault((kv) => kv.PrioItem.Key == itemKey);
         if (keyValue != null)
         {
            if (keyValue.Enabled != enabled)
            {
               keyValue.Enabled = enabled;
               _HashDirty = true;
            }
         }
      }

      public bool AnyEnabled
      {
         get
         {
            return this.Any(i => i.Enabled);
         }
      }

      internal string GetEntries()
      {
         var value = string.Empty;
         foreach (var entry in this)
         {
            value += string.Format("{0};{1}|", entry.PrioItem.Key, entry.Enabled);
         }
         return value.Remove(value.Length - 1);
      }

      internal void SetEntries(string value)
      {
         if (value == null) return;
         var entries = value.Split('|');
         var prio = 0;
         foreach (var val in entries)
         {
            var prioItemKey = 0;
            var enabled = true;
            var values = val.Split(';');
            if (values.Length >= 2 &&
               int.TryParse(values[0], out prioItemKey) &&
               bool.TryParse(values[1], out enabled))
            {
               var keyValue = this.FirstOrDefault((kv) => kv.PrioItem.Key == prioItemKey);
               if (keyValue != null)
               {
                  keyValue.Enabled = enabled;
                  var currentPrio = IndexOf(keyValue);
                  this.Move(currentPrio, prio);
                  prio++;
               }
            }
         }
         _HashDirty = true;
      }

      internal List<string> GetList()
      {
         if (_HashDirty) UpdateHash();
         return _ClassList;
      }

      public void UpdateHash()
      {
         lock (_ClassList)
         {
            if (_HashDirty) //Second check now thread safe
            {
               _ClassList.Clear();
               foreach (var item in this)
               {
                  _ClassList.Add(string.Format("{0};{1}", item.PrioItem.Key, item.Enabled));
               }
               _PrioHash.Clear();
               var prio = 1;
               foreach (var item in this)
               {
                  _PrioHash.Add(item.PrioItem.Key, item.Enabled ? prio : int.MaxValue);
                  prio++;
               }
               _HashDirty = false;
            }
         }
      }
   }
}
