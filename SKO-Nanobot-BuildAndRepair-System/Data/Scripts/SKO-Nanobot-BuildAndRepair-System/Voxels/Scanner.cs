using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Voxels
{
    /// <summary>
    /// TEST SETUP OF SCANNER, ONLY FOR DEV/ADMIN FOR NOW UNTIL INTEGRATED IN BLOCK.
    /// </summary>
    internal static class Scanner
    {
        public static bool ScanActive = false;
        public static ConcurrentQueue<MyVoxelBase> Asteroids = new ConcurrentQueue<MyVoxelBase>();
        private static double SecondsBetweenAsteroids = 1;
        private static TimeSpan LastProcessed = MyAPIGateway.Session.ElapsedPlayTime;
        private static MyVoxelBase CurrentAsteroid = null;

        public static void CheckQueue()
        {
            if (MyAPIGateway.Session.ElapsedPlayTime.Subtract(LastProcessed).TotalSeconds < SecondsBetweenAsteroids)
                return;

            LastProcessed = MyAPIGateway.Session.ElapsedPlayTime;

            if(Asteroids.Count == 0 && ScanActive)
            {
                ScanActive = false;
                MyAPIGateway.Utilities.ShowMessage("Nanobot", $"Scanning complete.");
                return;
            }


            if (Asteroids.Count == 0)
            {
                ScanActive = false;
                return;
            }

            if (CurrentAsteroid != null)
            {
                return;
            }

            MyVoxelBase asteroid = null;
            if(Asteroids.TryDequeue(out asteroid))
            {
                if (asteroid != null)
                {
                    CurrentAsteroid = asteroid;
                    MyAPIGateway.Parallel.StartBackground(() =>
                    {
                        try
                        {
                            
                            var deposits = ScanVoxelMapForDeposits(asteroid, MyAPIGateway.Session.Player.Character.GetPosition());
                            if (deposits != null)
                            {
                                foreach (var deposit in deposits)
                                {
                                    // Find the center position and log in the Description.
                                    var depositCenter = FindDepositCenter(asteroid, deposit.Location, deposit.Material);
                                    if (depositCenter != null)
                                    {
                                        deposit.Location = depositCenter.Value;
                                    }

                                    // Set the description:
                                    var description = $"scan_{asteroid.StorageName}_{deposit.Material.MinedOre}";
                                    description += $"\nJSON:{{ \"x\": {deposit.Location.X}, \"y\": {deposit.Location.Y}, \"z\": {deposit.Location.Z} }}";

                                    EnsureGpsByDescription(deposit.Material.MinedOre, description, deposit.Location, Color.Yellow, MyAPIGateway.Session.Player.Identity.IdentityId);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            CurrentAsteroid = null;
                        }
                    },
                    () =>
                    {
                        CurrentAsteroid = null;
                    });
                }
            }
        }

        public static void ScanView()
        {
            try
            {
                // Testing with admin only.
                // Test scanner options before adding to BnR Terminal
                ulong sId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0UL;
                if (sId != Constants.sId)
                    return;

                if (ScanActive)
                {
                    MyAPIGateway.Utilities.ShowMessage("Nanobot", "Already scanning...");
                    return;
                }

                ScanActive = true;
                OreScannerVisuals.ScannedZones.Clear();

                var asteroid = GetLookingAsteroid();
                if(asteroid != null)
                {
                    if (!Asteroids.Contains(asteroid))
                    {
                        Asteroids.Enqueue(asteroid);
                    }
                }
            }
            catch 
            {
                ScanActive = false;
            }
        }

        public static void ScanAround()
        {
            try
            {
                // Testing with admin only.
                // Test scanner options before adding to BnR Terminal
                ulong sId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0UL;
                if (sId != Constants.sId)
                    return;

                if (ScanActive)
                {
                    MyAPIGateway.Utilities.ShowMessage("Nanobot", "Already scanning...");
                    return;
                }

                ScanActive = true;
                OreScannerVisuals.ScannedZones.Clear();

                var asteroids = GetNearbyAsteroids();

                if (asteroids.Count > 0)
                    MyAPIGateway.Utilities.ShowMessage("Nanobot", $"Scanning {asteroids.Count} asteroids...");

                foreach (var asteroid in asteroids)
                {
                    if (!Asteroids.Contains(asteroid))
                    {
                        Asteroids.Enqueue(asteroid);
                    }
                }
            }
            catch (Exception)
            {
                ScanActive = false;
            }
        }
    
        public static void VisualizeAsteroidBoundingBox(MyVoxelBase voxel)
        {
            Vector3D min = voxel.PositionLeftBottomCorner;
            Vector3D max = min + voxel.Size;
            BoundingBoxD worldAABB = new BoundingBoxD(min, max);
            MatrixD matrix = MatrixD.CreateTranslation(worldAABB.Center);
            BoundingBoxD localBox = new BoundingBoxD(worldAABB.Min - worldAABB.Center, worldAABB.Max - worldAABB.Center);

            OreScannerVisuals.ScannedZones.Add(new ScannedZone(matrix, localBox));
        }

        public static List<ScanResult> ScanVoxelMapForDeposits(MyVoxelBase voxel, Vector3D scannerPosition)
        {
            int scanBoxSize = 20;
            int maxOreTypes = 2;
            double coreSkip = 0.25;

            var results = new List<ScanResult>();
            var visited = new HashSet<Vector3I>();
            var foundDefs = new HashSet<MyVoxelMaterialDefinition>();

            Vector3I storageCenter = voxel.Storage.Size / 2;
            int radius = 384; // or 64 for tighter scan

            Vector3I min = Vector3I.Max(Vector3I.Zero, storageCenter - radius);
            Vector3I max = Vector3I.Min(voxel.Storage.Size - 1, storageCenter + radius);

            min = Vector3I.Floor((Vector3D)min / scanBoxSize) * scanBoxSize;
            max = Vector3I.Ceiling((Vector3D)max / scanBoxSize) * scanBoxSize;
            BoundingBoxI bounds = new BoundingBoxI(min, max);

            Vector3D center = (Vector3D)voxel.Size / 2.0;
            Vector3I centerChunk = Vector3I.Floor(center / scanBoxSize) * scanBoxSize;

            Vector3D asteroidCenter = voxel.PositionLeftBottomCorner + (Vector3D)center;
            double asteroidRadius = voxel.Size.Length() / 2.0;
            double coreRadius = asteroidRadius * coreSkip;

            ProcessScanCell(voxel, centerChunk, bounds, scanBoxSize, visited, asteroidCenter, coreRadius, scannerPosition, foundDefs, results, maxOreTypes);

            return results;
        }

        private static void ProcessScanCell(MyVoxelBase voxel, Vector3I cell, BoundingBoxI bounds, int scanBoxSize, HashSet<Vector3I> visited, Vector3D coreCenter, double coreRadius, Vector3D scannerPosition, HashSet<MyVoxelMaterialDefinition> foundDefs, List<ScanResult> results, int maxOreTypes)
        {
            if (foundDefs.Count >= maxOreTypes)
                return;

            if (cell.X < bounds.Min.X || cell.Y < bounds.Min.Y || cell.Z < bounds.Min.Z ||
                cell.X > bounds.Max.X || cell.Y > bounds.Max.Y || cell.Z > bounds.Max.Z)
                return;

            if (!visited.Add(cell))
                return;

            // Expand first
            int step = scanBoxSize * 2; // or (int)(scanBoxSize * 1.5)
            Vector3I[] directions = new Vector3I[]
            {
                new Vector3I(step, 0, 0),
                new Vector3I(-step, 0, 0),
                new Vector3I(0, step, 0),
                new Vector3I(0, -step, 0),
                new Vector3I(0, 0, step),
                new Vector3I(0, 0, -step)
            };

            for (int i = 0; i < directions.Length; i++)
            {
                Vector3I next = cell + directions[i];
                ProcessScanCell(voxel, next, bounds, scanBoxSize, visited, coreCenter, coreRadius, scannerPosition, foundDefs, results, maxOreTypes);

                if (foundDefs.Count >= maxOreTypes)
                    return;
            }

            // Check if cell is outside the core
            Vector3D cellCenter = voxel.PositionLeftBottomCorner + (Vector3D)cell + new Vector3D(scanBoxSize / 2.0);
            double distToCenter = Vector3D.Distance(cellCenter, coreCenter);
            double cellRadius = scanBoxSize * 0.866;
            if (distToCenter - cellRadius < coreRadius)
                return;

            // Check if this cell touches voxel content
            if (!CellTouchesVoxels(voxel, cell, scanBoxSize))
                return;

            // Visualize the valid cell
            double half = scanBoxSize / 2.0;
            BoundingBoxD box = new BoundingBoxD(new Vector3D(-half), new Vector3D(half));
            MatrixD matrix = MatrixD.CreateTranslation(cellCenter);
            OreScannerVisuals.ScannedZones.Add(new ScannedZone(matrix, box));

            // Ore detection (optional here, if needed again)
            var oreDef = TryDetectOreInCell(voxel, cell, scanBoxSize);
            if (oreDef != null && oreDef.IsRare && foundDefs.Add(oreDef))
            {
                double distance = Vector3D.Distance(cellCenter, scannerPosition);
                results.Add(new ScanResult(oreDef)
                {
                    Location = cellCenter,
                    Distance = distance
                });

                return; // ✅ stop expanding this branch — ore found
            }
        }

        private static MyVoxelMaterialDefinition TryDetectOreInCell(MyVoxelBase voxel, Vector3I cell, int scanBoxSize)
        {
            var data = new MyStorageData();
            Vector3I cellMax = cell + scanBoxSize - 1;
            data.Resize(cell, cellMax);
            voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.Material | MyStorageDataTypeFlags.Content, 0, cell, cellMax);

            Vector3I size = data.Size3D;
            Vector3I p = new Vector3I();

            int step = 2; // scan every 2 voxels

            for (p.X = 0; p.X < size.X; p.X += step)
                for (p.Y = 0; p.Y < size.Y; p.Y += step)
                    for (p.Z = 0; p.Z < size.Z; p.Z += step)
                    {
                        int idx = data.ComputeLinear(ref p);
                        byte content = data.Content(idx);
                        if (content < 127)
                            continue;

                        byte mat = data.Material(idx);
                        if (mat == byte.MaxValue)
                            continue;

                        MyVoxelMaterialDefinition def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(mat);
                        if (def != null && def.IsRare)
                            return def;
                    }

            return null;
        }

        private static bool CellTouchesVoxels(MyVoxelBase voxel, Vector3I cell, int scanBoxSize)
        {
            try
            {
                var data = new MyStorageData();
                Vector3I cellMax = cell + scanBoxSize - 1;
                data.Resize(cell, cellMax);
                voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.Content, 0, cell, cellMax);

                Vector3I size = data.Size3D;
                Vector3I p = new Vector3I();

                for (p.X = 0; p.X < size.X; p.X++)
                    for (p.Y = 0; p.Y < size.Y; p.Y++)
                        for (p.Z = 0; p.Z < size.Z; p.Z++)
                        {
                            int idx = data.ComputeLinear(ref p);
                            if (data.Content(idx) >= 127)
                                return true; // solid voxel found
                        }
            }
            catch (Exception)
            {
            }            

            return false;
        }

        private static MyVoxelBase GetLookingAsteroid()
        {
            const float depth = 30000f;
            const float width = 50f;
            const float height = 50f;

            var character = MyAPIGateway.Session?.Player?.Character;
            if (character == null)
                return null;

            var headMatrix = character.GetHeadMatrix(true);
            var startPos = headMatrix.Translation;
            var forward = headMatrix.Forward;
            var ray = new RayD(startPos, forward);

            var center = startPos + forward * (depth / 2.0);
            var halfExtents = new Vector3D(width / 2.0, height / 2.0, depth / 2.0);
            var orientation = Quaternion.CreateFromRotationMatrix(headMatrix.GetOrientation());
            var obb = new MyOrientedBoundingBoxD(center, halfExtents, orientation);
            var aabb = obb.GetAABB();

            var voxels = new List<MyVoxelBase>();
            MyGamePruningStructure.GetAllVoxelMapsInBox(ref aabb, voxels);

            MyVoxelBase closest = null;
            double closestDist = double.MaxValue;

            foreach (var voxel in voxels)
            {
                if (!(voxel is MyVoxelMap) || voxel is MyPlanet || voxel.Storage == null || !voxel.StorageName.StartsWith("Asteroid"))
                    continue;

                var voxelAABB = new BoundingBoxD(voxel.PositionLeftBottomCorner, voxel.PositionLeftBottomCorner + voxel.Size);
                var intersect = ray.Intersects(voxelAABB);
                if (intersect.HasValue && intersect.Value < closestDist)
                {
                    closest = voxel;
                    closestDist = intersect.Value;
                }
            }

            return closest;
        }
          
        public static void EnsureGpsByDescription(string name, string description, Vector3D position, Color color, long playerId)
        {
            if(string.IsNullOrEmpty(name)) return;
            if(string.IsNullOrEmpty(description)) return;
            if (position == null) return;
            if (playerId == 0) return;

            var gpsList = new List<IMyGps>();
            MyAPIGateway.Session.GPS.GetGpsList(playerId, gpsList);

            foreach (var gpsItem in gpsList)
            {
                if (gpsItem.Description == description)
                    return; // GPS with this description already exists
            }

            var gps = MyAPIGateway.Session.GPS.Create(name, description, position, true);
            gps.GPSColor = color;
            MyAPIGateway.Session.GPS.AddGps(playerId, gps);
        }

        public static void ClearGPS(int maxPerOreType)
        {
            var player = MyAPIGateway.Session.Player;
            if(player == null || player.Character == null || player.Character.IsDead) return;

            var gpsList = MyAPIGateway.Session.GPS.GetGpsList(player.IdentityId);
            if (gpsList == null || gpsList.Count == 0) return;

            var position = player.GetPosition();
            var groupedByOre = new Dictionary<string, List<IMyGps>>();

            foreach (var gps in gpsList)
            {
                if (string.IsNullOrEmpty(gps.Description) || !gps.Description.StartsWith("scan_Asteroid_"))
                    continue;

                var oreType = gps.Name.Trim();
                if (!groupedByOre.ContainsKey(oreType))
                    groupedByOre[oreType] = new List<IMyGps>();

                groupedByOre[oreType].Add(gps);
            }

            foreach (var kv in groupedByOre)
            {
                var oreType = kv.Key;
                var entries = kv.Value;

                var closest = entries
                    .OrderBy(g => Vector3D.DistanceSquared(position, g.Coords))
                    .Take(maxPerOreType)
                    .ToHashSet();

                foreach (var gps in entries)
                {
                    if (!closest.Contains(gps))
                    {
                        try
                        {
                            MyAPIGateway.Session.GPS.RemoveGps(player.IdentityId, gps);
                        }
                        catch { }
                    }
                }
            }
        }

        private static Vector3D? FindDepositCenter(MyVoxelBase voxel, Vector3D startPosition, MyVoxelMaterialDefinition targetMaterial, int radius = 20)
        {
            if (voxel?.Storage == null || targetMaterial == null)
                return null;

            var voxelStart = Vector3I.Floor(startPosition - voxel.PositionLeftBottomCorner);
            Vector3I min = voxelStart - radius;
            Vector3I max = voxelStart + radius;

            // Clamp to storage size
            Vector3I storageSize = voxel.Storage.Size - 1;
            min = Vector3I.Clamp(min, Vector3I.Zero, storageSize);
            max = Vector3I.Clamp(max, Vector3I.Zero, storageSize);

            var data = new MyStorageData();
            data.Resize(min, max);
            voxel.Storage.ReadRange(data, MyStorageDataTypeFlags.Content | MyStorageDataTypeFlags.Material, 0, min, max);

            Vector3I size = data.Size3D;
            Vector3D sum = Vector3D.Zero;
            int count = 0;
            Vector3I p = new Vector3I();

            for (p.X = 0; p.X < size.X; p.X++)
                for (p.Y = 0; p.Y < size.Y; p.Y++)
                    for (p.Z = 0; p.Z < size.Z; p.Z++)
                    {
                        int idx = data.ComputeLinear(ref p);
                        byte content = data.Content(idx);
                        if (content < 127)
                            continue;

                        byte mat = data.Material(idx);
                        if (mat == byte.MaxValue)
                            continue;

                        MyVoxelMaterialDefinition def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(mat);
                        if (def == null || def.Id.SubtypeName != targetMaterial.Id.SubtypeName)
                            continue;

                        Vector3I voxelPos = min + p;
                        Vector3D worldPos = voxel.PositionLeftBottomCorner + voxelPos;
                        sum += worldPos;
                        count++;
                    }

            if (count == 0)
                return null;

            return sum / count;
        }

        public static List<MyVoxelBase> GetNearbyAsteroids(double range = 20000)
        {
            var player = MyAPIGateway.Session.Player;
            if (player == null || player.Character == null || player.Character.IsDead) return new List<MyVoxelBase>();
            var playerPosition = player.Character.GetPosition();

            var sphere = new BoundingSphereD(playerPosition, range);
            var results = new List<MyVoxelBase>();
            MyGamePruningStructure.GetAllVoxelMapsInSphere(ref sphere, results);

            // Filter to real asteroids (exclude planets, procedural fog, null storage, etc.)
            return results
                .Where(v =>
                    v is MyVoxelMap &&
                    !(v is MyPlanet) &&
                    v.Storage != null &&
                    v.StorageName != null &&
                    v.StorageName.StartsWith("Asteroid"))
                .ToList();
        }
    }
}
