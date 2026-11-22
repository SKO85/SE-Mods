using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace DefenseShields
{
    public class ShieldApi
    {
        private bool _apiInit;
        private Func<IMyTerminalBlock, RayD, long, float, bool, bool, Vector3D?> _rayAttackShield; // negative damage values heal
        private Func<IMyTerminalBlock, LineD, long, float, bool, bool, Vector3D?> _lineAttackShield; // negative damage values heal
        private Func<List<MyEntity>, RayD, bool, bool, long, float, MyTuple<bool, float>> _intersectEntToShieldFast; // fast check of entities for shield
        private Func<IMyTerminalBlock, Vector3D, long, float, bool, bool, bool, bool> _pointAttackShield; // negative damage values heal
        private Func<IMyTerminalBlock, Vector3D, long, float, bool, bool, bool, float?> _pointAttackShieldExt; // negative damage values heal
        private Func<IMyTerminalBlock, Vector3D, long, float, float, bool, bool, bool, float?> _pointAttackShieldCon; // negative damage values heal, conditional secondary damage
        private Func<IMyTerminalBlock, Vector3D, long, float, float, bool, bool, bool, float, float?> _pointAttackShieldHeat; // negative damage values heal, conditional secondary damage
        private Action<IMyTerminalBlock, int> _setShieldHeat;
        private Action<IMyTerminalBlock> _overLoad;
        private Action<IMyTerminalBlock, float> _setCharge;
        private Func<IMyTerminalBlock, RayD, Vector3D?> _rayIntersectShield;
        private Func<IMyTerminalBlock, LineD, Vector3D?> _lineIntersectShield;
        private Func<IMyTerminalBlock, Vector3D, bool> _pointInShield;
        private Func<IMyTerminalBlock, float> _getShieldPercent;
        private Func<IMyTerminalBlock, int> _getShieldHeat;
        private Func<IMyTerminalBlock, float> _getChargeRate;
        private Func<IMyTerminalBlock, int> _hpToChargeRatio;
        private Func<IMyTerminalBlock, float> _getMaxCharge;
        private Func<IMyTerminalBlock, float> _getCharge;
        private Func<IMyTerminalBlock, float> _getPowerUsed;
        private Func<IMyTerminalBlock, float> _getPowerCap;
        private Func<IMyTerminalBlock, float> _getMaxHpCap;
        private Func<IMyTerminalBlock, bool> _isShieldUp;
        private Func<IMyTerminalBlock, string> _shieldStatus;
        private Func<IMyTerminalBlock, IMyEntity, bool, bool> _entityBypass;
        private Func<IMyCubeGrid, bool> _gridHasShield;
        private Func<IMyCubeGrid, bool> _gridShieldOnline;
        private Func<IMyEntity, bool> _protectedByShield;
        private Func<IMyEntity, IMyTerminalBlock> _getShieldBlock;
        private Func<IMyEntity, bool, IMyTerminalBlock> _matchEntToShieldFast;
        private Func<MyEntity, bool, MyTuple<IMyTerminalBlock, MyTuple<bool, bool, float, float, float, int>, MyTuple<MatrixD, MatrixD>>?> _matchEntToShieldFastExt;
        private Func<MyEntity, bool, MyTuple<IMyTerminalBlock, MyTuple<bool, bool, float, float, float, int>, MyTuple<MatrixD, MatrixD>, MyTuple<bool, bool, float, float>>?> _matchEntToShieldFastDetails;
        private Func<IMyTerminalBlock, bool> _isShieldBlock;
        private Func<Vector3D, IMyTerminalBlock> _getClosestShield;
        private Func<IMyTerminalBlock, Vector3D, double> _getDistanceToShield;
        private Func<IMyTerminalBlock, Vector3D, Vector3D?> _getClosestShieldPoint;
        private Func<MyEntity, MyTuple<bool, bool, float, float, float, int>> _getShieldInfo;
        private Func<MyEntity, MyTuple<bool, bool, float, float>> _getModulationInfo;
        private Func<IMyTerminalBlock, Vector3D, bool, MyTuple<bool, int, int, float, float>> _getFaceInfo;
        private Func<IMyTerminalBlock, Vector3D, bool, MyTuple<bool, int, int, float, float, float>> _getFaceInfoAndPenChance;
        private Func<IMySlimBlock, bool> _isBlockProtected;
        private Func<MyEntity, MyTuple<bool, Vector3I>> _getFacesFast;
        private Action<MyEntity, ICollection<MyTuple<long, float, uint>>> _getLastAttackers;
        private Func<IMyTerminalBlock, bool> _isFortified;

        private Action<long> _addAtacker;

        private const long Channel = 1365616918;

        private bool _isReady;

        // BUG-260612.32: a "Compromised" broadcast from DefenseShields (API integrity
        // failure) now disables the API instead of being recorded and ignored.
        public bool IsReady
        {
            get { return _isReady && !Compromised; }
            private set { _isReady = value; }
        }

        public bool Compromised { get; private set; }

        private void HandleMessage(object o)
        {
            var message = o as string;
            // BUG-260612.32: honor Compromised even after init (the old _apiInit
            // early-return swallowed it).
            if (message != null && message == "Compromised")
            {
                Compromised = true;
                return;
            }

            if (_apiInit) return;
            var dict = o as IReadOnlyDictionary<string, Delegate>;

            if (dict == null || dict is ImmutableDictionary<string, Delegate>)
                return;

            var builder = ImmutableDictionary.CreateBuilder<string, Delegate>();
            foreach (var pair in dict)
                builder.Add(pair.Key, pair.Value);

            MyAPIGateway.Utilities.SendModMessage(Channel, builder.ToImmutable());

            ApiLoad(dict);
            // BUG-260612.31: ready only when the delegates this mod actually calls
            // resolved — optional ones may be missing on older/forked DS versions.
            IsReady = _isBlockProtected != null && _protectedByShield != null;
        }

        private bool _isRegistered;

        public bool Load()
        {
            if (!_isRegistered)
            {
                _isRegistered = true;
                MyAPIGateway.Utilities.RegisterMessageHandler(Channel, HandleMessage);
            }
            if (!IsReady)
                MyAPIGateway.Utilities.SendModMessage(Channel, "ApiEndpointRequest");
            return IsReady;
        }

        public void Unload()
        {
            if (_isRegistered)
            {
                _isRegistered = false;
                MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, HandleMessage);
            }
            IsReady = false;
        }

        // BUG-260612.31: tolerant lookup — the indexer threw on the first key missing
        // from an older/forked DefenseShields, leaving the API permanently bricked and
        // bubbling the exception into SE's mod-message dispatch.
        private static T Get<T>(IReadOnlyDictionary<string, Delegate> delegates, string name) where T : class
        {
            Delegate del;
            return delegates.TryGetValue(name, out del) ? del as T : null;
        }

        public void ApiLoad(IReadOnlyDictionary<string, Delegate> delegates)
        {
            _rayAttackShield = Get<Func<IMyTerminalBlock, RayD, long, float, bool, bool, Vector3D?>>(delegates, "RayAttackShield");
            _lineAttackShield = Get<Func<IMyTerminalBlock, LineD, long, float, bool, bool, Vector3D?>>(delegates, "LineAttackShield");
            _intersectEntToShieldFast = Get<Func<List<MyEntity>, RayD, bool, bool, long, float, MyTuple<bool, float>>>(delegates, "IntersectEntToShieldFast");
            _pointAttackShield = Get<Func<IMyTerminalBlock, Vector3D, long, float, bool, bool, bool, bool>>(delegates, "PointAttackShield");
            _pointAttackShieldExt = Get<Func<IMyTerminalBlock, Vector3D, long, float, bool, bool, bool, float?>>(delegates, "PointAttackShieldExt");
            _pointAttackShieldCon = Get<Func<IMyTerminalBlock, Vector3D, long, float, float, bool, bool, bool, float?>>(delegates, "PointAttackShieldCon");
            _pointAttackShieldHeat = Get<Func<IMyTerminalBlock, Vector3D, long, float, float, bool, bool, bool, float, float?>>(delegates, "PointAttackShieldHeat");
            _setShieldHeat = Get<Action<IMyTerminalBlock, int>>(delegates, "SetShieldHeat");
            _overLoad = Get<Action<IMyTerminalBlock>>(delegates, "OverLoadShield");
            _setCharge = Get<Action<IMyTerminalBlock, float>>(delegates, "SetCharge");
            _rayIntersectShield = Get<Func<IMyTerminalBlock, RayD, Vector3D?>>(delegates, "RayIntersectShield");
            _lineIntersectShield = Get<Func<IMyTerminalBlock, LineD, Vector3D?>>(delegates, "LineIntersectShield");
            _pointInShield = Get<Func<IMyTerminalBlock, Vector3D, bool>>(delegates, "PointInShield");
            _getShieldPercent = Get<Func<IMyTerminalBlock, float>>(delegates, "GetShieldPercent");
            _getShieldHeat = Get<Func<IMyTerminalBlock, int>>(delegates, "GetShieldHeat");
            _getChargeRate = Get<Func<IMyTerminalBlock, float>>(delegates, "GetChargeRate");
            _hpToChargeRatio = Get<Func<IMyTerminalBlock, int>>(delegates, "HpToChargeRatio");
            _getMaxCharge = Get<Func<IMyTerminalBlock, float>>(delegates, "GetMaxCharge");
            _getCharge = Get<Func<IMyTerminalBlock, float>>(delegates, "GetCharge");
            _getPowerUsed = Get<Func<IMyTerminalBlock, float>>(delegates, "GetPowerUsed");
            _getPowerCap = Get<Func<IMyTerminalBlock, float>>(delegates, "GetPowerCap");
            _getMaxHpCap = Get<Func<IMyTerminalBlock, float>>(delegates, "GetMaxHpCap");
            _isShieldUp = Get<Func<IMyTerminalBlock, bool>>(delegates, "IsShieldUp");
            _shieldStatus = Get<Func<IMyTerminalBlock, string>>(delegates, "ShieldStatus");
            _entityBypass = Get<Func<IMyTerminalBlock, IMyEntity, bool, bool>>(delegates, "EntityBypass");
            _gridHasShield = Get<Func<IMyCubeGrid, bool>>(delegates, "GridHasShield");
            _gridShieldOnline = Get<Func<IMyCubeGrid, bool>>(delegates, "GridShieldOnline");
            _protectedByShield = Get<Func<IMyEntity, bool>>(delegates, "ProtectedByShield");
            _getShieldBlock = Get<Func<IMyEntity, IMyTerminalBlock>>(delegates, "GetShieldBlock");
            _matchEntToShieldFast = Get<Func<IMyEntity, bool, IMyTerminalBlock>>(delegates, "MatchEntToShieldFast");
            _matchEntToShieldFastExt = Get<Func<MyEntity, bool, MyTuple<IMyTerminalBlock, MyTuple<bool, bool, float, float, float, int>, MyTuple<MatrixD, MatrixD>>?>>(delegates, "MatchEntToShieldFastExt");
            _matchEntToShieldFastDetails = Get<Func<MyEntity, bool, MyTuple<IMyTerminalBlock, MyTuple<bool, bool, float, float, float, int>, MyTuple<MatrixD, MatrixD>, MyTuple<bool, bool, float, float>>?>>(delegates, "MatchEntToShieldFastDetails");
            _isShieldBlock = Get<Func<IMyTerminalBlock, bool>>(delegates, "IsShieldBlock");
            _getClosestShield = Get<Func<Vector3D, IMyTerminalBlock>>(delegates, "GetClosestShield");
            _getDistanceToShield = Get<Func<IMyTerminalBlock, Vector3D, double>>(delegates, "GetDistanceToShield");
            _getClosestShieldPoint = Get<Func<IMyTerminalBlock, Vector3D, Vector3D?>>(delegates, "GetClosestShieldPoint");
            _getShieldInfo = Get<Func<MyEntity, MyTuple<bool, bool, float, float, float, int>>>(delegates, "GetShieldInfo");
            _getModulationInfo = Get<Func<MyEntity, MyTuple<bool, bool, float, float>>>(delegates, "GetModulationInfo");
            _getFaceInfo = Get<Func<IMyTerminalBlock, Vector3D, bool, MyTuple<bool, int, int, float, float>>>(delegates, "GetFaceInfo");
            _getFaceInfoAndPenChance = Get<Func<IMyTerminalBlock, Vector3D, bool, MyTuple<bool, int, int, float, float, float>>>(delegates, "GetFaceInfoAndPenChance");
            _addAtacker = Get<Action<long>>(delegates, "AddAttacker");
            _isBlockProtected = Get<Func<IMySlimBlock, bool>>(delegates, "IsBlockProtected");
            _getFacesFast = Get<Func<MyEntity, MyTuple<bool, Vector3I>>>(delegates, "GetFacesFast");
            _getLastAttackers = Get<Action<MyEntity, ICollection<MyTuple<long, float, uint>>>>(delegates, "GetLastAttackers");
            _isFortified = Get<Func<IMyTerminalBlock, bool>>(delegates, "IsFortified");

            // BUG-260612.31: set only after the (no longer throwing) load completed.
            _apiInit = true;
        }

        public Vector3D? RayAttackShield(IMyTerminalBlock block, RayD ray, long attackerId, float damage, bool energy, bool drawParticle) =>
            _rayAttackShield?.Invoke(block, ray, attackerId, damage, energy, drawParticle) ?? null;

        public Vector3D? LineAttackShield(IMyTerminalBlock block, LineD line, long attackerId, float damage, bool energy, bool drawParticle) =>
            _lineAttackShield?.Invoke(block, line, attackerId, damage, energy, drawParticle) ?? null;

        public MyTuple<bool, float> IntersectEntToShieldFast(List<MyEntity> entities, RayD ray, bool onlyIfOnline, bool enemyOnly, long requesterId, float maxRange) =>
            _intersectEntToShieldFast?.Invoke(entities, ray, onlyIfOnline, enemyOnly, requesterId, maxRange) ?? new MyTuple<bool, float>(false, float.MaxValue);

        public bool PointAttackShield(IMyTerminalBlock block, Vector3D pos, long attackerId, float damage, bool energy, bool drawParticle, bool posMustBeInside = false) =>
            _pointAttackShield?.Invoke(block, pos, attackerId, damage, energy, drawParticle, posMustBeInside) ?? false;

        public float? PointAttackShieldExt(IMyTerminalBlock block, Vector3D pos, long attackerId, float damage, bool energy, bool drawParticle, bool posMustBeInside = false) =>
            _pointAttackShieldExt?.Invoke(block, pos, attackerId, damage, energy, drawParticle, posMustBeInside) ?? null;

        public float? PointAttackShieldCon(IMyTerminalBlock block, Vector3D pos, long attackerId, float damage, float optionalDamage, bool energy, bool drawParticle, bool posMustBeInside = false) =>
            _pointAttackShieldCon?.Invoke(block, pos, attackerId, damage, optionalDamage, energy, drawParticle, posMustBeInside) ?? null;

        public float? PointAttackShieldHeat(IMyTerminalBlock block, Vector3D pos, long attackerId, float damage, float optionalDamage, bool energy, bool drawParticle, bool posMustBeInside = false, float heatScaler = 1) =>
            _pointAttackShieldHeat?.Invoke(block, pos, attackerId, damage, optionalDamage, energy, drawParticle, posMustBeInside, heatScaler) ?? null;

        public void SetShieldHeat(IMyTerminalBlock block, int value) => _setShieldHeat?.Invoke(block, value);

        public void OverLoadShield(IMyTerminalBlock block) => _overLoad?.Invoke(block);

        public void SetCharge(IMyTerminalBlock block, float value) => _setCharge.Invoke(block, value);

        public Vector3D? RayIntersectShield(IMyTerminalBlock block, RayD ray) => _rayIntersectShield?.Invoke(block, ray) ?? null;

        public Vector3D? LineIntersectShield(IMyTerminalBlock block, LineD line) => _lineIntersectShield?.Invoke(block, line) ?? null;

        public bool PointInShield(IMyTerminalBlock block, Vector3D pos) => _pointInShield?.Invoke(block, pos) ?? false;

        public float GetShieldPercent(IMyTerminalBlock block) => _getShieldPercent?.Invoke(block) ?? -1;

        public int GetShieldHeat(IMyTerminalBlock block) => _getShieldHeat?.Invoke(block) ?? -1;

        public float GetChargeRate(IMyTerminalBlock block) => _getChargeRate?.Invoke(block) ?? -1;

        public float HpToChargeRatio(IMyTerminalBlock block) => _hpToChargeRatio?.Invoke(block) ?? -1;

        public float GetMaxCharge(IMyTerminalBlock block) => _getMaxCharge?.Invoke(block) ?? -1;

        public float GetCharge(IMyTerminalBlock block) => _getCharge?.Invoke(block) ?? -1;

        public float GetPowerUsed(IMyTerminalBlock block) => _getPowerUsed?.Invoke(block) ?? -1;

        public float GetPowerCap(IMyTerminalBlock block) => _getPowerCap?.Invoke(block) ?? -1;

        public float GetMaxHpCap(IMyTerminalBlock block) => _getMaxHpCap?.Invoke(block) ?? -1;

        public bool IsShieldUp(IMyTerminalBlock block) => _isShieldUp?.Invoke(block) ?? false;

        public string ShieldStatus(IMyTerminalBlock block) => _shieldStatus?.Invoke(block) ?? string.Empty;

        public bool EntityBypass(IMyTerminalBlock block, IMyEntity entity, bool remove = false) => _entityBypass?.Invoke(block, entity, remove) ?? false;

        public bool GridHasShield(IMyCubeGrid grid) => _gridHasShield?.Invoke(grid) ?? false;

        public bool GridShieldOnline(IMyCubeGrid grid) => _gridShieldOnline?.Invoke(grid) ?? false;

        public bool ProtectedByShield(IMyEntity entity) => _protectedByShield?.Invoke(entity) ?? false;

        public IMyTerminalBlock GetShieldBlock(IMyEntity entity) => _getShieldBlock?.Invoke(entity) ?? null;

        public IMyTerminalBlock MatchEntToShieldFast(IMyEntity entity, bool onlyIfOnline) => _matchEntToShieldFast?.Invoke(entity, onlyIfOnline) ?? null;

        public MyTuple<IMyTerminalBlock, MyTuple<bool, bool, float, float, float, int>, MyTuple<MatrixD, MatrixD>>? MatchEntToShieldFastExt(MyEntity entity, bool onlyIfOnline) => _matchEntToShieldFastExt?.Invoke(entity, onlyIfOnline) ?? null;

        public MyTuple<IMyTerminalBlock, MyTuple<bool, bool, float, float, float, int>, MyTuple<MatrixD, MatrixD>, MyTuple<bool, bool, float, float>>? MatchEntToShieldFastDetails(MyEntity entity, bool onlyIfOnline) => _matchEntToShieldFastDetails?.Invoke(entity, onlyIfOnline) ?? null;

        public bool IsShieldBlock(IMyTerminalBlock block) => _isShieldBlock?.Invoke(block) ?? false;

        public IMyTerminalBlock GetClosestShield(Vector3D pos) => _getClosestShield?.Invoke(pos) ?? null;

        public double GetDistanceToShield(IMyTerminalBlock block, Vector3D pos) => _getDistanceToShield?.Invoke(block, pos) ?? -1;

        public Vector3D? GetClosestShieldPoint(IMyTerminalBlock block, Vector3D pos) => _getClosestShieldPoint?.Invoke(block, pos) ?? null;

        public MyTuple<bool, bool, float, float, float, int> GetShieldInfo(MyEntity entity) => _getShieldInfo?.Invoke(entity) ?? new MyTuple<bool, bool, float, float, float, int>();

        public MyTuple<bool, bool, float, float> GetModulationInfo(MyEntity entity) => _getModulationInfo?.Invoke(entity) ?? new MyTuple<bool, bool, float, float>();

        public MyTuple<bool, int, int, float, float> GetFaceInfo(IMyTerminalBlock block, Vector3D pos, bool posMustBeInside = false) => _getFaceInfo?.Invoke(block, pos, posMustBeInside) ?? new MyTuple<bool, int, int, float, float>();

        public MyTuple<bool, int, int, float, float, float> TAPI_GetFaceInfoAndPenChance(IMyTerminalBlock block, Vector3D pos, bool posMustBeInside = false) => _getFaceInfoAndPenChance?.Invoke(block, pos, posMustBeInside) ?? new MyTuple<bool, int, int, float, float, float>();

        public void AddAttacker(long attacker) => _addAtacker?.Invoke(attacker);

        public bool IsBlockProtected(IMySlimBlock block) => _isBlockProtected?.Invoke(block) ?? false;

        public MyTuple<bool, Vector3I> GetFacesFast(MyEntity entity) => _getFacesFast?.Invoke(entity) ?? new MyTuple<bool, Vector3I>();

        public void GetLastAttackers(MyEntity entity, ICollection<MyTuple<long, float, uint>> collection) => _getLastAttackers?.Invoke(entity, collection);

        public bool IsFortified(IMyTerminalBlock block) => _isFortified?.Invoke(block) ?? false;
    }
}