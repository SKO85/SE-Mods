using Draygo.API;
using ProtoBuf;
using Sandbox.ModAPI;
using SKONanobotBuildAndRepairSystem.Handlers;
using System;
using System.Text;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace SKONanobotBuildAndRepairSystem.Testing
{
    /// <summary>HUD content sent from the server to the client that started the tests (dedicated server).</summary>
    [ProtoContract]
    public class MsgTestHud
    {
        [ProtoMember(1)] public bool Clear;
        [ProtoMember(2)] public string List;
        [ProtoMember(3)] public string Current;
    }

    /// <summary>
    /// Test harness HUD (TextHudAPI, like the debug HUD): top right the session's tests as a table (state, run, time,
    /// note), bottom right the running test. Built on the server by TestRunner; drawn directly in single player /
    /// on a listen server, sent to the calling client on a dedicated server. Testing variant only.
    /// </summary>
    internal static class TestHud
    {
        public const ushort MsgId = 40150;
        private const double Scale = 0.55;
        private const double Padding = 0.012;
        private static readonly Vector2D ListOrigin = new Vector2D(0.36, 0.98);
        private static readonly Vector2D CurrentOrigin = new Vector2D(0.36, -0.52);
        private static readonly Color BgColor = new Color(0, 0, 0, 170);

        private static HudAPIv2.HUDMessage _list, _current;
        private static HudAPIv2.BillBoardHUDMessage _listBg, _currentBg;
        private static readonly StringBuilder _listText = new StringBuilder();
        private static readonly StringBuilder _currentText = new StringBuilder();
        private static bool _handlerRegistered;
        private static bool _shown;

        // ------------------------------------------------------------ client side

        public static void Register()
        {
            if (MyAPIGateway.Utilities == null || MyAPIGateway.Utilities.IsDedicated || _handlerRegistered) return;
            try
            {
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(MsgId, OnMessage);
                _handlerRegistered = true;
            }
            catch { }
        }

        public static void Unregister()
        {
            try
            {
                if (_handlerRegistered) MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(MsgId, OnMessage);
            }
            catch { }
            _handlerRegistered = false;
            Destroy();
        }

        private static void OnMessage(ushort id, byte[] data, ulong sender, bool fromServer)
        {
            if (!fromServer || data == null) return;
            try
            {
                var msg = MyAPIGateway.Utilities.SerializeFromBinary<MsgTestHud>(data);
                if (msg == null) return;
                if (msg.Clear) Hide();
                else Show(msg.List, msg.Current);
            }
            catch { }
        }

        // ------------------------------------------------------------ server side

        /// <summary>Show / update the HUD of the player who started the tests.</summary>
        public static void Publish(ulong steamId, string list, string current)
        {
            if (IsLocal(steamId)) { Show(list, current); return; }
            Send(steamId, new MsgTestHud { List = list, Current = current });
        }

        /// <summary>Remove the HUD of the player who started the tests.</summary>
        public static void PublishClear(ulong steamId)
        {
            if (IsLocal(steamId)) { Hide(); return; }
            Send(steamId, new MsgTestHud { Clear = true });
        }

        private static bool IsLocal(ulong steamId)
        {
            if (MyAPIGateway.Utilities.IsDedicated) return false;
            return steamId == 0 || MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.MultiplayerActive
                || steamId == MyAPIGateway.Multiplayer.MyId;
        }

        private static void Send(ulong steamId, MsgTestHud msg)
        {
            if (steamId == 0 || MyAPIGateway.Multiplayer == null) return;
            try
            {
                // Direct send on purpose: test UI traffic stays out of the mod's NetStats (the @sync tests measure those).
                MyAPIGateway.Multiplayer.SendMessageTo(MsgId, MyAPIGateway.Utilities.SerializeToBinary(msg), steamId, true);
            }
            catch { }
        }

        // ------------------------------------------------------------ drawing

        private static bool EnsureCreated()
        {
            if (_list != null) return true;
            if (!HudHandler.IsApiReady) return false;
            _listBg = NewBackground(ListOrigin);
            _currentBg = NewBackground(CurrentOrigin);
            _list = NewText(_listText, ListOrigin);
            _current = NewText(_currentText, CurrentOrigin);
            return true;
        }

        private static HudAPIv2.BillBoardHUDMessage NewBackground(Vector2D origin)
        {
            var bg = new HudAPIv2.BillBoardHUDMessage(
                Material: MyStringId.GetOrCompute("Square"), Origin: origin, BillBoardColor: BgColor, Scale: 1d,
                Width: 0.01f, Height: 0.01f, HideHud: false, Shadowing: true, Blend: BlendTypeEnum.PostPP);
            bg.Visible = false;
            return bg;
        }

        private static HudAPIv2.HUDMessage NewText(StringBuilder text, Vector2D origin)
        {
            var msg = new HudAPIv2.HUDMessage(
                Message: text, Origin: origin, Scale: Scale, HideHud: false, Shadowing: true, ShadowColor: Color.Black,
                Blend: BlendTypeEnum.PostPP, Font: "monospace");
            msg.InitialColor = Color.White;
            msg.Visible = false;
            return msg;
        }

        public static void Show(string list, string current)
        {
            if (!EnsureCreated()) return;
            _listText.Clear().Append(list ?? "");
            _currentText.Clear().Append(current ?? "");
            Fit(_list, _listBg, ListOrigin, _listText.Length > 0);
            Fit(_current, _currentBg, CurrentOrigin, _currentText.Length > 0);
            _shown = true;
        }

        public static void Hide()
        {
            if (!_shown) return;
            if (_list != null) { _list.Visible = false; _listBg.Visible = false; }
            if (_current != null) { _current.Visible = false; _currentBg.Visible = false; }
            _shown = false;
        }

        private static void Fit(HudAPIv2.HUDMessage text, HudAPIv2.BillBoardHUDMessage bg, Vector2D origin, bool visible)
        {
            text.Visible = visible;
            bg.Visible = visible;
            if (!visible) return;
            var size = text.GetTextLength();
            var w = Math.Abs(size.X) + Padding * 2;
            var h = Math.Abs(size.Y) + Padding * 2;
            bg.Origin = new Vector2D(origin.X - Padding + w / 2, origin.Y + Padding - h / 2);
            bg.Width = (float)w;
            bg.Height = (float)h;
        }

        private static void Destroy()
        {
            try
            {
                if (_list != null) { _list.Visible = false; _list.DeleteMessage(); }
                if (_current != null) { _current.Visible = false; _current.DeleteMessage(); }
                if (_listBg != null) { _listBg.Visible = false; _listBg.DeleteMessage(); }
                if (_currentBg != null) { _currentBg.Visible = false; _currentBg.DeleteMessage(); }
            }
            catch { }
            _list = _current = null;
            _listBg = _currentBg = null;
            _shown = false;
        }
    }
}
