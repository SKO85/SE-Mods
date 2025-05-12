using ProtoBuf;
using System.Collections.Generic;
using VRage.Game;
using VRageMath;

namespace SKONanobotBuildAndRepairSystem.Voxels
{

    [ProtoContract]
    public struct OreDeposit
    {
        [ProtoMember(1)]
        public string Ore;
        
        [ProtoMember(2)]
        public Vector3D Location;
    }

    public class ScanResult
    {
        public MyVoxelMaterialDefinition Material { get; set; }
        public Vector3D Location { get; set; }
        public double Distance { get; set; }

        public ScanResult(MyVoxelMaterialDefinition material)
        {
            Material = material;
            Distance = double.MaxValue;
        }
    }

    public static class OreScannerVisuals
    {
        public static readonly List<ScannedZone> ScannedZones = new List<ScannedZone>();
    }

    public class ScannedZone
    {
        public MatrixD Matrix;
        public BoundingBoxD Box;

        public ScannedZone(MatrixD matrix, BoundingBoxD box)
        {
            Matrix = matrix;
            Box = box;
        }
    }    
}