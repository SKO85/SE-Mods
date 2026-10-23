using NSubstitute;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Caches;
using SKONanobotBuildAndRepairSystem.Handlers;
using SKONanobotBuildAndRepairSystem.Models;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using VRage.Game.ModAPI;
using VRage.Utils;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace System.Runtime.CompilerServices
{
    // net48 has no ModuleInitializerAttribute; the C# compiler only needs the type to exist.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace SKONanobotBuildAndRepairSystem.Tests
{
    /// <summary>
    /// Process-wide setup: resolve Space Engineers assemblies from Bin64 and give the
    /// engine statics the mod touches (MyLog.Default, MyAPIGateway.Session) a harmless value.
    /// </summary>
    internal static class TestEnvironment
    {
        public static string SpaceEngineersBin { get; private set; }

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Init()
        {
            SpaceEngineersBin = Environment.GetEnvironmentVariable("SPACEENGINEERS_BIN");
            if (string.IsNullOrEmpty(SpaceEngineersBin))
            {
                SpaceEngineersBin = typeof(TestEnvironment).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Where(a => a.Key == "SpaceEngineersBin")
                    .Select(a => a.Value)
                    .FirstOrDefault();
            }
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromSpaceEngineersBin;
        }

        private static Assembly ResolveFromSpaceEngineersBin(object sender, ResolveEventArgs args)
        {
            if (string.IsNullOrEmpty(SpaceEngineersBin)) return null;
            var name = new AssemblyName(args.Name).Name;
            var path = Path.Combine(SpaceEngineersBin, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        /// <summary>Repo root (folder holding docs/plan). Baked in at build; falls back to walking up.</summary>
        public static string RepoRoot
        {
            get
            {
                var baked = typeof(TestEnvironment).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Where(a => a.Key == "RepoRoot").Select(a => a.Value).FirstOrDefault();
                if (!string.IsNullOrEmpty(baked) && Directory.Exists(Path.Combine(baked, "docs", "plan")))
                    return Path.GetFullPath(baked);

                // Test hosts may shadow-copy the assembly, so start from the AppDomain base dir.
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "docs", "plan"))) dir = dir.Parent;
                if (dir == null) throw new InvalidOperationException("Repo root (docs/plan) not found.");
                return dir.FullName;
            }
        }
    }

    /// <summary>
    /// Base class for tests that touch mod statics. Resets Mod/handler state before each test and
    /// installs a fake session whose play time and frame counter the test controls.
    /// </summary>
    public abstract class ModTestBase : IDisposable
    {
        protected readonly IMySession Session;
        private TimeSpan _now = TimeSpan.FromSeconds(100);
        private int _frame = 1000;

        protected ModTestBase()
        {
            if (MyLog.Default == null) MyLog.Default = new MyLog(false);

            Session = Substitute.For<IMySession>();
            Session.IsServer.Returns(true);
            Session.ElapsedPlayTime.Returns(_ => _now);
            Session.GameplayFrameCounter.Returns(_ => _frame);
            MyAPIGateway.Session = Session;
            MyAPIGateway.Utilities = null;   // Logging caches instead of writing files
            MyAPIGateway.Multiplayer = null; // network sends early-return
            MyAPIGateway.Parallel = null;

            ResetModStatics();
        }

        protected TimeSpan Now
        {
            get { return _now; }
            set { _now = value; Mod.NowPlayTime = value; }
        }

        protected int Frame
        {
            get { return _frame; }
            set { _frame = value; }
        }

        protected void ResetModStatics()
        {
            Mod.Settings = new SyncModSettings();
            Mod.SettingsValid = false;
            Mod.CustomSettingsLoaded = false;
            Mod.SimSpeedOverride = null;
            Mod.NanobotSystems.Clear();
            Mod.GridSystemCount.Clear();
            Now = TimeSpan.FromSeconds(100);
            SetPrivateStatic(typeof(Mod), "_cachedAutoStagger", -1);
            SetPrivateStatic(typeof(Mod), "_cachedAutoStaggerFrame", int.MinValue);
            BlockSystemAssigningHandler.Clear();
            BlockFailureCooldownHandler.Clear();
            InvokeStatic("SKONanobotBuildAndRepairSystem.Cluster.ScanClusterCoordinator", "Clear");
            InvokeStatic("SKONanobotBuildAndRepairSystem.Cluster.GridScanCache", "Clear");
        }

        public virtual void Dispose()
        {
            ResetModStatics();
            MyAPIGateway.Session = null;
        }

        protected static void SetPrivateStatic(Type type, string field, object value)
        {
            var f = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) throw new MissingFieldException(type.Name, field);
            f.SetValue(null, value);
        }

        /// <summary>Calls a static method on a (possibly internal) mod type by full name.</summary>
        protected static object InvokeStatic(string typeFullName, string method, params object[] args)
        {
            var type = typeof(Mod).Assembly.GetType(typeFullName, true);
            var m = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) throw new MissingMethodException(type.Name, method);
            return m.Invoke(null, args);
        }

        protected static object InvokePrivateStatic(Type type, string method, params object[] args)
        {
            var m = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) throw new MissingMethodException(type.Name, method);
            return m.Invoke(null, args);
        }
    }
}
