using Sandbox.Definitions;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Sandbox.Game.Entities;
using VRageMath;
using VRage.Voxels;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRage;

namespace SpaceEquipmentLtd.Utils
{
   public static class UtilsVoxels
   {
      public enum Shapes { Box, Sphere };
      public delegate bool VoxelIncludeMaterialHandler(byte material, byte content, ISet<byte> ignoreMaterial);
      public delegate void VoxelCubeHandler(MyVoxelBase voxelMap, uint id, ref Vector3I voxelCoordMin, ref Vector3I voxelCoordMax, ref Vector3D worldPosition, Dictionary<byte, float> material);
      public delegate bool VoxelRemoveMaterialHandler(MyVoxelBase voxelMap, ref Vector3I voxelCoordMin, ref Vector3I voxelCoordMax, ref Vector3D worldPosition, byte material, float volume, ISet<byte> ignoreMaterial, ref bool ignoreItem);
      public delegate bool VoxelAddMaterialHandler   (MyVoxelBase voxelMap, ref Vector3I voxelCoordMin, ref Vector3I voxelCoordMax, ref Vector3D worldPosition, ref byte material, ref float volumne);

      public static void VoxelVolumeToMinedOre(MyVoxelMaterialDefinition materialDef, float voxelVolume, float harvestMultiplier, out float oreAmount, out float oreVolume)
      {
         MyObjectBuilder_Ore mindedOreMaterial = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(materialDef.MinedOre);
         MyPhysicalItemDefinition definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(mindedOreMaterial);
         oreVolume = voxelVolume * harvestMultiplier * Sandbox.Game.MyDrillConstants.VOXEL_HARVEST_RATIO * materialDef.MinedOreRatio;
         oreAmount = oreVolume / definition.Volume;
      }

      public static float VoxelVolumeToMinedOreAmount(MyVoxelMaterialDefinition materialDef, float voxelVolume, float harvestMultiplier)
      {
         float oreAmount, oreVolume;
         VoxelVolumeToMinedOre(materialDef, voxelVolume, harvestMultiplier, out oreAmount, out oreVolume);
         return oreAmount;
      }

      public static float VoxelVolumeToMinedOreVolume(MyVoxelMaterialDefinition materialDef, float voxelVolume, float harvestMultiplier)
      {
         float oreAmount, oreVolume;
         VoxelVolumeToMinedOre(materialDef, voxelVolume, harvestMultiplier, out oreAmount, out oreVolume);
         return oreVolume;
      }

      public static float MinedOreAmountToVoxelVolume(MyVoxelMaterialDefinition materialDef, float oreAmount, float harvestMultiplier)
      {
         MyObjectBuilder_Ore mindedOreMaterial = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(materialDef.MinedOre);
         MyPhysicalItemDefinition definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(mindedOreMaterial);
         return (oreAmount * definition.Volume) / (harvestMultiplier * Sandbox.Game.MyDrillConstants.VOXEL_HARVEST_RATIO * materialDef.MinedOreRatio);
      }

      /// <summary>
      /// 
      /// </summary>
      private static void ComputeShapeBounds(MyVoxelBase voxelMap, ref BoundingBoxD shapeAabb, Vector3D voxelMapMinCorner, Vector3I storageSize, out Vector3I voxelMin, out Vector3I voxelMax)
      {
         MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxelMapMinCorner, ref shapeAabb.Min, out voxelMin);
         MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxelMapMinCorner, ref shapeAabb.Max, out voxelMax);

         voxelMin += voxelMap.StorageMin;
         voxelMax += voxelMap.StorageMin + 1;
         storageSize -= 1;
         Vector3I.Clamp(ref voxelMin, ref Vector3I.Zero, ref storageSize, out voxelMin);
         Vector3I.Clamp(ref voxelMax, ref Vector3I.Zero, ref storageSize, out voxelMax);
      }

      /// <summary>
      /// 
      /// </summary>
      private static void ClampVoxelCoord(this IMyStorage storage, ref Vector3I voxelCoord, int distance = 1)
      {
         if (storage == null) return;
         Vector3I newSize = storage.Size - distance;
         Vector3I.Clamp(ref voxelCoord, ref Vector3I.Zero, ref newSize, out voxelCoord);
      }

      private static int IncAddRest(int start, int inc, int max)
      {
         var end = start + inc;
         if (end > max) end = max;
         return end;
      }

      /// <summary>
      /// 
      /// </summary>
      public static void VoxelIterateInBoxLod(this MyVoxelBase voxelMap, MyStorageData cache, ref BoundingBoxD box, ref MyOrientedBoundingBoxD orientedBox, VoxelRemoveMaterialHandler handler)
      {
         Vector3I minVoxel;
         Vector3I maxVoxel;

         if (voxelMap == null || box == null) return;
         using (voxelMap.Pin())
         {
            if (voxelMap.Closed || voxelMap.MarkedForClose || voxelMap.Storage == null) return;

            MatrixD worldMatrixInvScaled = voxelMap.PositionComp.WorldMatrixInvScaled;
            worldMatrixInvScaled.Translation = worldMatrixInvScaled.Translation + voxelMap.SizeInMetresHalf;
            var localBoundaries = box.TransformFast(worldMatrixInvScaled);

            ComputeShapeBounds(voxelMap, ref localBoundaries, Vector3I.Zero, voxelMap.Storage.Size, out minVoxel, out maxVoxel);

            minVoxel -= 1;
            maxVoxel += 1;
            voxelMap.Storage.ClampVoxelCoord(ref minVoxel, 1);
            voxelMap.Storage.ClampVoxelCoord(ref maxVoxel, 1);

            int lod = 0;
            while(lod < 16 && new Vector3I(maxVoxel.X - minVoxel.X, maxVoxel.Y - minVoxel.Y, maxVoxel.Z - minVoxel.Z).Size > 4000)
            {
               lod++;
               minVoxel >>= 1;
               maxVoxel >>= 1;
            }

            int contentScale = 1 << lod;
            contentScale = contentScale * contentScale * contentScale;

            cache.Resize(minVoxel, maxVoxel);
            var myVoxelRequestFlag = MyVoxelRequestFlags.ConsiderContent;
            voxelMap.Storage.ReadRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, lod, minVoxel, maxVoxel, ref myVoxelRequestFlag);

            Vector3I pos;
            var boundingBox = new BoundingBoxD();
            var ignoreMaterial = new HashSet<byte>();
            bool ignore = false;
            for(pos.X=minVoxel.X; pos.X <= maxVoxel.X; pos.X++)
            {
               for (pos.Y = minVoxel.Y; pos.Y <= maxVoxel.Y; pos.Y++)
               {
                  for (pos.Z = minVoxel.Z; pos.Z <= maxVoxel.Z; pos.Z++)
                  {
                     Vector3I relPos = pos - minVoxel;
                     int linear = cache.ComputeLinear(ref relPos);
                     var content = cache.Content(linear);
                     var material = cache.Material(linear);
                     if (content == MyVoxelConstants.VOXEL_CONTENT_EMPTY) continue;
                     if (ignoreMaterial.Contains(material)) continue;

                     //As worldBoundaries is a axis aligned box it is the absolute outer bounds of the area.
                     //To get the real shape we have to check each real voxel position against the orientedBox
                     Vector3D voxelWorldPositionMin;
                     Vector3I voxelCoordMin = (pos << lod) - voxelMap.StorageMin;
                     Vector3I voxelCoordMax = voxelCoordMin + (lod > 0 ? ((Vector3I.One << lod) - Vector3I.One) : Vector3I.Zero);
                     MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxelMap.PositionLeftBottomCorner, ref voxelCoordMin, out voxelWorldPositionMin);
                     var contains = false;

                     if (lod==0) {
                        //Must exactly fit
                        contains = orientedBox.Contains(ref voxelWorldPositionMin);
                     } else {
                        //Must intersect
                        Vector3D voxelWorldPositionMax;
                        MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxelMap.PositionLeftBottomCorner, ref voxelCoordMax, out voxelWorldPositionMax);
                        boundingBox.Min = voxelWorldPositionMin;
                        boundingBox.Max = voxelWorldPositionMax;
                        contains = orientedBox.Intersects( ref boundingBox);
                     }
                     if (!contains) continue;

                     var volume = (float)(content * MyVoxelConstants.VOXEL_VOLUME_IN_METERS * contentScale) / MyVoxelConstants.VOXEL_CONTENT_FULL;
                     if (!handler(voxelMap, ref voxelCoordMin, ref voxelCoordMax, ref voxelWorldPositionMin, material, volume, ignoreMaterial, ref ignore)) return;
                  }
               }
            }
         }
      }

      /// <summary>
      /// 
      /// </summary>
      public static int VoxelIterateInShape(this MyVoxelBase voxelMap, MyStorageData cache, BoundingBoxD box, ref MatrixD matrix, Shapes shape, bool includeEmpty, VoxelIncludeMaterialHandler includeHandler, VoxelCubeHandler cubeHandler)
      {
         Vector3I minVoxel;
         Vector3I maxVoxel;

         int cnt = 0;
         if (voxelMap == null) return -1;
         using(voxelMap.Pin())
         {
            if (voxelMap.Closed || voxelMap.MarkedForClose || voxelMap.Storage == null) return -2;

            MatrixD matrixD1 = matrix * voxelMap.PositionComp.WorldMatrixNormalizedInv;

            BoundingBoxD localBoundaries;
            MyOrientedBoundingBoxD localOrientedBox;
            //BoundingSphereD localSphere;

            localOrientedBox = MyOrientedBoundingBoxD.Create(box, matrixD1);
            localBoundaries = localOrientedBox.GetAABB();

            switch(shape)
            {
               default:
               case Shapes.Box:
                  localOrientedBox = MyOrientedBoundingBoxD.Create(box, matrixD1);
                  localBoundaries = localOrientedBox.GetAABB();
                  break;
               case Shapes.Sphere:
                  //localSphere = BoundingSphereD.CreateFromBoundingBox();
                  return -1;
            }
       
            localBoundaries.Translate(voxelMap.SizeInMetresHalf);
            ComputeShapeBounds(voxelMap, ref localBoundaries, Vector3I.Zero, voxelMap.Storage.Size, out minVoxel, out maxVoxel);

            minVoxel -=1;
            maxVoxel += 1;
            voxelMap.Storage.ClampVoxelCoord(ref minVoxel, 1);
            voxelMap.Storage.ClampVoxelCoord(ref maxVoxel, 1);

            var cubeSize = cache.Size3D-1;
            int remainder;
            Vector3D cubesCount;

            cubesCount.X = Math.DivRem(maxVoxel.X - minVoxel.X, cubeSize.X, out remainder) + (remainder > 0 ? 1 : 0);
            cubesCount.Y = Math.DivRem(maxVoxel.Y - minVoxel.Y, cubeSize.Y, out remainder) + (remainder > 0 ? 1 : 0);
            cubesCount.Z = Math.DivRem(maxVoxel.Z - minVoxel.Z, cubeSize.Z, out remainder) + (remainder > 0 ? 1 : 0);

            var myVoxelRequestFlag = MyVoxelRequestFlags.ConsiderContent;

            Vector3I pos;
            var ignoreMaterial = new HashSet<byte>();
            var detailedMaterial = new Dictionary<byte, float>();

            var curminVoxel = minVoxel;
            var curmaxVoxel = curminVoxel;
            
            curmaxVoxel.X = IncAddRest(curmaxVoxel.X, cubeSize.X, maxVoxel.X);
            curmaxVoxel.Y = IncAddRest(curmaxVoxel.Y, cubeSize.Y, maxVoxel.Y);
            curmaxVoxel.Z = IncAddRest(curmaxVoxel.Z, cubeSize.Z, maxVoxel.Z);
            var id = 0u;
            Vector3D cube;
            Vector3D curCubeSize;
            for(cube.X = 0; cube.X < cubesCount.X; cube.X++)
            {
               for (cube.Y = 0; cube.Y < cubesCount.Y; cube.Y++)
               {
                  for (cube.Z = 0; cube.Z < cubesCount.Z; cube.Z++)
                  {
                     detailedMaterial.Clear();
                     voxelMap.Storage.ReadRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, 0, curminVoxel, curmaxVoxel, ref myVoxelRequestFlag);
                     Vector3I voxelCoordMin = Vector3I.MaxValue;
                     Vector3I voxelCoordMax = Vector3I.MinValue;
                     id++;
                     curCubeSize.X = curmaxVoxel.X - curminVoxel.X;
                     curCubeSize.Y = curmaxVoxel.Y - curminVoxel.Y;
                     curCubeSize.Z = curmaxVoxel.Z - curminVoxel.Z;

                     for (pos.X = 0; pos.X <= curCubeSize.X; pos.X++)
                     {
                        for (pos.Y = 0; pos.Y <= curCubeSize.Y; pos.Y++)
                        {
                           for (pos.Z = 0; pos.Z <= curCubeSize.Z; pos.Z++)
                           {
                              cnt++;
                              int linear = cache.ComputeLinear(ref pos);
                              var content = cache.Content(linear);
                              if (!includeEmpty && content == MyVoxelConstants.VOXEL_CONTENT_EMPTY) continue;
                              var material = cache.Material(linear);
                              if (ignoreMaterial.Contains(material)) continue;

                              //As worldBoundaries is a axis aligned box it is the absolute outer bounds of the area.
                              //To get the real shape we have to check each real voxel position against the orientedBox
                              //Vector3I voxelCoord = (pos + curminVoxel) - voxelMap.StorageMin;
                              Vector3I voxelCoord = (pos + curminVoxel) - voxelMap.StorageMin;
                              Vector3 voxelLocalPosition = voxelCoord - voxelMap.SizeInMetresHalf;
                              var contains = localOrientedBox.Contains(ref voxelLocalPosition);
                              //BoundingBoxD voxel = new BoundingBoxD(voxelLocalPosition, voxelLocalPosition + MyVoxelConstants.VOXEL_SIZE_IN_METRES);
                              //var contains = (localOrientedBox.Contains(ref voxel) != ContainmentType.Disjoint);
                              if (contains && includeHandler(material, content, ignoreMaterial))
                              {
                                 float volume;
                                 detailedMaterial.TryGetValue(material, out volume);
                                 if (content == MyVoxelConstants.VOXEL_CONTENT_EMPTY && material == MyVoxelConstants.NULL_MATERIAL) content = MyVoxelConstants.VOXEL_CONTENT_FULL;
                                 detailedMaterial[material] = volume + (float)(content * MyVoxelConstants.VOXEL_VOLUME_IN_METERS) / MyVoxelConstants.VOXEL_CONTENT_FULL;
                                 Vector3I.Min(ref voxelCoordMin, ref voxelCoord, out voxelCoordMin);
                                 Vector3I.Max(ref voxelCoordMax, ref voxelCoord, out voxelCoordMax);
                              }
                           }
                        }
                     }
                     if (detailedMaterial.Count > 0)
                     {
                        var voxelCoordCenter = voxelCoordMin + ((voxelCoordMax - voxelCoordMin) / 2);
                        Vector3D voxelLocalPositionCenter = voxelCoordCenter - voxelMap.SizeInMetresHalf;
                        Vector3D voxelWorldPositionCenter = Vector3D.Transform(voxelLocalPositionCenter, voxelMap.WorldMatrix); ;
                        cubeHandler(voxelMap, id, ref voxelCoordMin, ref voxelCoordMax, ref voxelWorldPositionCenter, detailedMaterial);
                     }

                     curminVoxel.Z = IncAddRest(curminVoxel.Z, cubeSize.Z, maxVoxel.Z);
                     curmaxVoxel.Z = IncAddRest(curmaxVoxel.Z, cubeSize.Z, maxVoxel.Z);
                  }
                  curminVoxel.Z = minVoxel.Z;
                  curmaxVoxel.Z = IncAddRest(curminVoxel.Z, cubeSize.Z, maxVoxel.Z);
                  curminVoxel.Y = IncAddRest(curminVoxel.Y, cubeSize.Y, maxVoxel.Y);
                  curmaxVoxel.Y = IncAddRest(curmaxVoxel.Y, cubeSize.Y, maxVoxel.Y);
               }
               curminVoxel.Y = minVoxel.Y;
               curmaxVoxel.Y = IncAddRest(curminVoxel.Y, cubeSize.Y, maxVoxel.Y);
               curminVoxel.X = IncAddRest(curminVoxel.X, cubeSize.X, maxVoxel.X);
               curmaxVoxel.X = IncAddRest(curmaxVoxel.X, cubeSize.X, maxVoxel.X);
            }
         }
         return cnt;
      }

      /// <summary>
      /// 
      /// </summary>
      public static long VoxelRemoveContent(this MyVoxelBase voxelMap, MyStorageData cache, List<BoundingBoxI> cutoutBoxes, Vector3I minVoxel, Vector3I maxVoxel, BoundingBoxD box, ref MatrixD matrix, bool checkOnly, VoxelRemoveMaterialHandler handler)
      {
         if (voxelMap == null) return 0;

         MatrixD matrixD1 = matrix * voxelMap.PositionComp.WorldMatrixNormalizedInv;
         var localOrientedBox = MyOrientedBoundingBoxD.Create(box, matrixD1);

         minVoxel += voxelMap.StorageMin;
         maxVoxel += voxelMap.StorageMin;
         var myVoxelRequestFlag = MyVoxelRequestFlags.ConsiderContent;
         int totalRemoved = 0;
         using(voxelMap.Pin())
         {
            if (voxelMap.Closed || voxelMap.MarkedForClose || voxelMap.Storage == null) return 0;
            voxelMap.Storage.ClampVoxelCoord(ref minVoxel, 1);
            voxelMap.Storage.ClampVoxelCoord(ref maxVoxel, 1);
            voxelMap.Storage.ReadRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, 0, minVoxel, maxVoxel, ref myVoxelRequestFlag);

            Vector3I pos;
            var ignoreMaterial = new HashSet<byte>();
            bool ignore = false;
            for (pos.X = minVoxel.X; pos.X <= maxVoxel.X; pos.X++)
            {
               for (pos.Y = minVoxel.Y; pos.Y <= maxVoxel.Y; pos.Y++)
               {
                  for (pos.Z = minVoxel.Z; pos.Z <= maxVoxel.Z; pos.Z++)
                  {
                     Vector3I relPos = pos - minVoxel;
                     int linear = cache.ComputeLinear(ref relPos);
                     var content = cache.Content(linear);
                     if (content == MyVoxelConstants.VOXEL_CONTENT_EMPTY) continue;
                     var material = cache.Material(linear);
                     if (ignoreMaterial.Contains(material)) continue;

                     Vector3 voxelLocalPosition = pos - (voxelMap.StorageMin + voxelMap.SizeInMetresHalf);
                     if (!localOrientedBox.Contains(ref voxelLocalPosition)) continue;

                     ignore = false;
                     var volume = (float)(content * MyVoxelConstants.VOXEL_VOLUME_IN_METERS) / MyVoxelConstants.VOXEL_CONTENT_FULL;
                     Vector3D voxelWorldPositionMin = Vector3D.Transform(voxelLocalPosition, voxelMap.WorldMatrix); ;
                     var cont = handler(voxelMap, ref relPos, ref relPos, ref voxelWorldPositionMin, material, volume, ignoreMaterial, ref ignore);
                     if (!ignore && !checkOnly)
                     {
                        cache.Content(linear, MyVoxelConstants.VOXEL_CONTENT_EMPTY);
                        cache.Material(linear, MyVoxelConstants.NULL_MATERIAL);
                        AddVoxelToBoxes(cutoutBoxes, pos, cache.Size3D);
                        totalRemoved += content;
                     }
                     if (!cont) goto Leave;
                  }
               }
            }

         Leave:
            if (totalRemoved > 0)
            {
               voxelMap.Storage.WriteRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, minVoxel, maxVoxel, notify: false, skipCache: true);
               MyAPIGateway.Utilities.InvokeOnGameThread(() =>
               {
                  if (voxelMap.Closed || voxelMap.MarkedForClose || voxelMap.Storage == null) return;
                  voxelMap.Storage.NotifyRangeChanged(ref minVoxel, ref maxVoxel, MyStorageDataTypeFlags.ContentAndMaterial);
               }, "VoxelRemoveContent");
            }               
         }

         return totalRemoved;
      }

      /// <summary>
      /// 
      /// </summary>
      public static long VoxelAddContent(this MyVoxelBase voxelMap, MyStorageData cache, List<BoundingBoxI> cutoutBoxes, Vector3I minVoxel, Vector3I maxVoxel, BoundingBoxD box, ref MatrixD matrix, bool checkOnly, VoxelAddMaterialHandler handler)
      {
         if (voxelMap == null) return 0;

         MatrixD matrixD1 = matrix * voxelMap.PositionComp.WorldMatrixNormalizedInv;
         var localOrientedBox = MyOrientedBoundingBoxD.Create(box, matrixD1);

         minVoxel += voxelMap.StorageMin;
         maxVoxel += voxelMap.StorageMin;

         var myVoxelRequestFlag = MyVoxelRequestFlags.ConsiderContent;
         int totalAdded = 0;
         using (voxelMap.Pin())
         {
            if (voxelMap.Closed || voxelMap.MarkedForClose || voxelMap.Storage == null) return 0;

            voxelMap.Storage.ClampVoxelCoord(ref minVoxel, 1);
            voxelMap.Storage.ClampVoxelCoord(ref maxVoxel, 1);
            voxelMap.Storage.ReadRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, 0, minVoxel, maxVoxel, ref myVoxelRequestFlag);
            
            Vector3I pos;
            for (pos.X = minVoxel.X; pos.X <= maxVoxel.X; pos.X++)
            {
               for (pos.Y = minVoxel.Y; pos.Y <= maxVoxel.Y; pos.Y++)
               {
                  bool prevIsEmpty = true;
                  for (pos.Z = minVoxel.Z; pos.Z <= maxVoxel.Z; pos.Z++)
                  {
                     Vector3I relPos = pos - minVoxel;
                     int linear = cache.ComputeLinear(ref relPos);
                     var content = cache.Content(linear);
                     var material = cache.Material(linear);
                     if (content == MyVoxelConstants.VOXEL_CONTENT_FULL) continue;

                     Vector3 voxelLocalPosition = pos - (voxelMap.StorageMin + voxelMap.SizeInMetresHalf);
                     if (!localOrientedBox.Contains(ref voxelLocalPosition)) continue;
                    
                     //Don't fill levitating voxels
                     if (content == MyVoxelConstants.VOXEL_CONTENT_EMPTY && prevIsEmpty && !HasFilledNeighbor(cache, pos, minVoxel, maxVoxel)) continue;

                     var volume = (float)(content * MyVoxelConstants.VOXEL_VOLUME_IN_METERS) / MyVoxelConstants.VOXEL_CONTENT_FULL;
                     Vector3D voxelWorldPositionMin = Vector3D.Transform(voxelLocalPosition, voxelMap.WorldMatrix); ;
                     var cont = handler(voxelMap, ref relPos, ref relPos, ref voxelWorldPositionMin, ref material, ref volume);
                     content = (byte)((volume / MyVoxelConstants.VOXEL_VOLUME_IN_METERS) * MyVoxelConstants.VOXEL_CONTENT_FULL);
                     cache.Content(linear, content);
                     cache.Material(linear, material);
                     AddVoxelToBoxes(cutoutBoxes, pos, cache.Size3D);
                     totalAdded += content;
                     if (!cont) goto Leave;
                     prevIsEmpty = content == MyVoxelConstants.VOXEL_CONTENT_EMPTY;
                  }
               }
            }
         Leave:
            if (totalAdded > 0)
            {
               voxelMap.Storage.WriteRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, minVoxel, maxVoxel, notify: false, skipCache: true);
               MyAPIGateway.Utilities.InvokeOnGameThread(() =>
               {
                  if (voxelMap.Closed || voxelMap.MarkedForClose || voxelMap.Storage == null) return;
                  voxelMap.Storage.NotifyRangeChanged(ref minVoxel, ref maxVoxel, MyStorageDataTypeFlags.ContentAndMaterial);
               }, "VoxelAddContent");
            }
         }
         return totalAdded;
      }

      /// <summary>
      /// 
      /// </summary>
      public static void VoxelChangeContent(this MyVoxelBase voxelMap, MyStorageData cache, List<BoundingBoxI> cutoutBoxes, Vector3I minVoxel, Vector3I maxVoxel, byte newMaterial, long totalContent)
      {
         if (voxelMap == null || totalContent == 0) return;
         minVoxel += voxelMap.StorageMin;
         maxVoxel += voxelMap.StorageMin;

         var myVoxelRequestFlag = MyVoxelRequestFlags.ConsiderContent;
         using (voxelMap.Pin())
         {
            if (voxelMap.Closed || voxelMap.MarkedForClose || voxelMap.Storage == null) return;

            voxelMap.Storage.ClampVoxelCoord(ref minVoxel, 1);
            voxelMap.Storage.ClampVoxelCoord(ref maxVoxel, 1);
            voxelMap.Storage.ReadRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, 0, minVoxel, maxVoxel, ref myVoxelRequestFlag);
            int totalChanged = 0;
            Vector3I pos;

            foreach(var cutoutBox in cutoutBoxes)
            {
               for (pos.X = cutoutBox.Min.X; pos.X <= cutoutBox.Max.X; pos.X++)
               {
                  for (pos.Y = cutoutBox.Min.Y; pos.Y <= cutoutBox.Max.Y; pos.Y++)
                  {
                     for (pos.Z = cutoutBox.Min.Z; pos.Z <= cutoutBox.Max.Z; pos.Z++)
                     {
                        Vector3I relPos = pos - minVoxel;
                        int linear = cache.ComputeLinear(ref relPos);
                        var content = cache.Content(linear);
                        var material = cache.Material(linear);

                        if (totalContent < 0)
                        {
                           //Remove
                           if (content == MyVoxelConstants.VOXEL_CONTENT_EMPTY) continue;
                           if (totalContent < -content)
                           {
                              cache.Content(linear, MyVoxelConstants.VOXEL_CONTENT_EMPTY);
                              cache.Material(linear, newMaterial);
                           } else {
                              cache.Content(linear, (byte)(content + totalContent));
                           }
                           totalContent += content;
                           totalChanged++;
                           if (totalContent >= 0) goto Leave;
                        } else
                        {
                           //Add
                           if (content == MyVoxelConstants.VOXEL_CONTENT_FULL) continue;
                           if (totalContent > MyVoxelConstants.VOXEL_CONTENT_FULL)
                           {
                              cache.Content(linear, MyVoxelConstants.VOXEL_CONTENT_FULL);
                              cache.Material(linear, newMaterial);
                           }
                           else
                           {
                              cache.Content(linear, (byte)totalContent);
                           }
                           totalContent -= MyVoxelConstants.VOXEL_CONTENT_FULL;
                           totalChanged++;
                           if (totalContent < 0) goto Leave;
                        }
                     }
                  }
               }
            }

         Leave:
            if (totalChanged > 0)
            {
               voxelMap.Storage.WriteRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, minVoxel, maxVoxel, notify: false, skipCache: true);
               MyAPIGateway.Utilities.InvokeOnGameThread(() =>
               {
                  if (voxelMap.Closed || voxelMap.MarkedForClose || voxelMap.Storage == null) return;
                  voxelMap.Storage.NotifyRangeChanged(ref minVoxel, ref maxVoxel, MyStorageDataTypeFlags.ContentAndMaterial);
               }, "VoxelChangeContent");
            }
         }
      }

      private static void AddVoxelToBoxes(List<BoundingBoxI> boxes, Vector3I newVoxel, Vector3I maxSize)
      {
         for (var idx = 0; idx < boxes.Count; idx++)
         {
            if ( (boxes[idx].Max.X - boxes[idx].Min.X < maxSize.X) &&
                 (boxes[idx].Max.Y - boxes[idx].Min.Y < maxSize.Y) &&
                 (boxes[idx].Max.Z - boxes[idx].Min.Z < maxSize.Z)) 
            {
               if (boxes[idx].Max.X + 1 == newVoxel.X &&
                  boxes[idx].Min.Y == boxes[idx].Max.Y && boxes[idx].Min.Y == newVoxel.Y &&
                  boxes[idx].Min.Z == boxes[idx].Max.Z && boxes[idx].Min.Z == newVoxel.Z)
               {
                  BoundingBoxI box = boxes[idx];
                  box.Max.X = newVoxel.X;
                  boxes[idx] = box;
                  return;
               }
               else if (boxes[idx].Min.X == boxes[idx].Max.X && boxes[idx].Min.X == newVoxel.X &&
                        boxes[idx].Max.Y + 1 == newVoxel.Y &&
                        boxes[idx].Min.Z == boxes[idx].Max.Z && boxes[idx].Min.Z == newVoxel.Z)
               {
                  BoundingBoxI box = boxes[idx];
                  box.Max.Y = newVoxel.Y;
                  boxes[idx] = box;
                  return;
               }
               else if (boxes[idx].Min.X == boxes[idx].Max.X && boxes[idx].Min.X == newVoxel.X &&
                        boxes[idx].Min.Y == boxes[idx].Max.Y && boxes[idx].Min.Y == newVoxel.Y &&
                        boxes[idx].Max.Z + 1 == newVoxel.Z)
               {
                  BoundingBoxI box = boxes[idx];
                  box.Max.Z = newVoxel.Z;
                  boxes[idx] = box;
                  return;
               }
            }
         }
         boxes.Add(new BoundingBoxI(newVoxel, newVoxel));
      }

      /// <summary>
      /// 
      /// </summary>
      private static bool HasFilledNeighbor(MyStorageData cache, Vector3I centerpos, Vector3I minVoxel, Vector3I maxVoxel)
      {
         Vector3I pos;
         pos.X = centerpos.X - 1;
         pos.Y = centerpos.Y - 1;
         pos.Z = centerpos.Z - 1;
         for (pos.X = centerpos.X - 1; pos.X <= centerpos.X + 1; pos.X++)
         {
            if (pos.X < minVoxel.X || pos.X > maxVoxel.X) continue;
            for (pos.Y = centerpos.Y - 1; pos.Y <= centerpos.Y + 1; pos.Y++)
            {
               if (pos.Y < minVoxel.Y || pos.Y > maxVoxel.Y) continue;
               for (pos.Z = centerpos.Z - 1; pos.Z <= centerpos.Z + 1; pos.Z++)
               {
                  if (pos.Z < minVoxel.Z || pos.Z > maxVoxel.Z) continue;
                  Vector3I relPos = pos - minVoxel;
                  int linear = cache.ComputeLinear(ref relPos);
                  var content = cache.Content(linear);
                  var material = cache.Material(linear);
                  if (content != MyVoxelConstants.VOXEL_CONTENT_EMPTY) return true;
               }
            }
         }
         return false;
      }

      /// <summary>
      /// 
      /// </summary>
      public static bool HarvestOre(IMyInventory dstInventory, float maxInventoryVolume, Vector3D target, Vector3 spawnDirection, float spawnRadius, MyVoxelMaterialDefinition materialDef, float removedVolume, float harvestMultiplier)
      {
         var collected = false;
         MyObjectBuilder_Ore mindedOreMaterial = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(materialDef.MinedOre);
         mindedOreMaterial.MaterialTypeName = new MyStringHash?(materialDef.Id.SubtypeId);
         float minedOreRatio = removedVolume * harvestMultiplier * Sandbox.Game.MyDrillConstants.VOXEL_HARVEST_RATIO * materialDef.MinedOreRatio;

         //MySession.Static.AmountMined[material.MinedOre] += minedOreRatio; //Not available

         MyPhysicalItemDefinition definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(mindedOreMaterial);
         MyFixedPoint amount = (MyFixedPoint)(minedOreRatio / definition.Volume);

         if (amount > 0) //Drilled out and should be collected
         {
            var maxremainAmount = (MyFixedPoint)(maxInventoryVolume / definition.Volume);
            var maxpossibleAmount = maxremainAmount > amount ? amount : maxremainAmount; //Do not use MyFixedPoint.Min !Wrong Implementation could cause overflow!
            if (maxpossibleAmount > 0)
            {
               dstInventory.AddItems(maxpossibleAmount, mindedOreMaterial);
               amount -= maxpossibleAmount;
               collected = true;
            }
         }
         else if (amount < 0) //Drilled out but should not be collected
         {
            amount = -amount;
         }

         if (amount > 0)
         {
            var materialDefDamaged = materialDef.DamagedMaterial != MyStringHash.NullOrEmpty ? MyDefinitionManager.Static.GetVoxelMaterialDefinition(materialDef.DamagedMaterial.ToString()) : materialDef;
            MyFixedPoint amountPerDrop = (MyFixedPoint)((float)(0.15f / definition.Volume));
            
            Vector3 spawnCenter = target - (spawnDirection * spawnRadius * 1.2f);
            BoundingSphere boundingSphere = new BoundingSphere(spawnCenter, spawnRadius);
            while (amount > 0)
            {
               MyFixedPoint amountCurrent = MyFixedPoint.Min(amount, amountPerDrop);
               amount -= amountCurrent;
               var myPhysicalInventoryItem = new VRage.Game.Entity.MyPhysicalInventoryItem(amountCurrent, mindedOreMaterial, 1f);
               MyFloatingObjects.Spawn(myPhysicalInventoryItem, boundingSphere, null, materialDefDamaged, (entiy) =>
               {
                  entiy.Physics.LinearVelocity = MyUtils.GetRandomVector3HemisphereNormalized(spawnCenter) * MyUtils.GetRandomFloat(1.5f, 4f);
                  entiy.Physics.AngularVelocity = MyUtils.GetRandomVector3Normalized() * MyUtils.GetRandomFloat(4f, 8f);
               });
            }
         }

         return collected;
      }
   }
}
