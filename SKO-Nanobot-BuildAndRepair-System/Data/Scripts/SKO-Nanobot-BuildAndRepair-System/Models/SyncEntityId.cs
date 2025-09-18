using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Models
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class SyncEntityId
    {
        [ProtoMember(1)]
        public long EntityId { get; set; }

        [ProtoMember(2)]
        public long GridId { get; set; }

        [ProtoMember(3)]
        public Vector3I? Position { get; set; }

        [ProtoMember(4)]
        public BoundingBoxD? Box { get; set; }

        public override string ToString()
        {
            return string.Format("EntityId={0}, GridId={1}, Position={2}, Box={3}", EntityId, GridId, Position, Box);
        }

        public static SyncEntityId GetSyncId(object item)
        {
            if (item == null) return null;
            var slimBlock = item as IMySlimBlock;
            if (slimBlock != null)
            {
                if (slimBlock.FatBlock != null)
                {
                    return new SyncEntityId() { EntityId = slimBlock.FatBlock.EntityId, GridId = slimBlock.CubeGrid != null ? slimBlock.CubeGrid.EntityId : 0, Position = slimBlock.Position };
                }
                else if (slimBlock.CubeGrid != null)
                {
                    return new SyncEntityId() { EntityId = 0, GridId = slimBlock.CubeGrid.EntityId, Position = slimBlock.Position };
                }
            }

            var voxelBase = item as IMyVoxelBase;
            if (voxelBase != null)
            {
                return new SyncEntityId() { Box = voxelBase.WorldAABB };
            }

            var entity = item as IMyEntity;
            if (entity != null)
            {
                return new SyncEntityId() { EntityId = entity.EntityId };
            }

            var position = item as Vector3D?;
            if (position != null)
            {
                return new SyncEntityId() { Position = new Vector3I((int)position.Value.X, (int)position.Value.Y, (int)position.Value.Z) };
            }

            return null;
        }

        public static object GetItem(SyncEntityId id)
        {
            if (id == null) return null;

            if (id.EntityId != 0)
            {
                IMyEntity entity;
                if (MyAPIGateway.Entities.TryGetEntityById(id.EntityId, out entity))
                {
                    return entity;
                }
            }
            if (id.GridId != 0 && id.Position != null)
            {
                IMyEntity entity;
                if (MyAPIGateway.Entities.TryGetEntityById(id.GridId, out entity))
                {
                    var grid = entity as IMyCubeGrid;
                    return grid != null ? grid.GetCubeBlock(id.Position.Value) : null;
                }
            }
            if (id.Position != null)
            {
                return id.Position;
            }
            if (id.Box != null)
            {
                IMyEntity entity;
                var box = id.Box.Value;
                if ((entity = MyAPIGateway.Session.VoxelMaps.GetVoxelMapWhoseBoundingBoxIntersectsBox(ref box, null)) != null)
                {
                    return entity;
                }
            }
            return null;
        }

        public static IMySlimBlock GetItemAsSlimBlock(SyncEntityId id)
        {
            var item = GetItem(id);
            var slimBlock = item as IMySlimBlock;
            if (slimBlock != null) return slimBlock;

            var block = item as IMyCubeBlock;
            if (block != null) return block.SlimBlock;

            return null;
        }

        public static T GetItemAs<T>(SyncEntityId id) where T : class
        {
            return GetItem(id) as T;
        }

        public override bool Equals(object obj)
        {
            var syncObj = obj as SyncEntityId;
            if (obj == null || syncObj == null)
            {
                return false;
            }
            return EntityId == syncObj.EntityId && GridId == syncObj.GridId && Position.Equals(syncObj.Position) && Box.Equals(syncObj.Box);
        }

        public override int GetHashCode()
        {
            return EntityId.GetHashCode() + (GridId.GetHashCode() << 8) + ((Position.HasValue ? Position.Value.GetHashCode() : 0) << 16) + ((Box.HasValue ? Box.Value.GetHashCode() : 0) << 24);
        }
    }
}