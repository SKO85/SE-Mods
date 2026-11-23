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
    /// Test harness HUD (TextHudAPI, default font like the debug HUD): the session table anchored to the top-right
    /// corner, the running test anchored to the top-left corner. The table arrives as lines with tab-separated
    /// cells; each column is its own text block at a fixed offset (the default font is not monospaced). Built on the
    /// server by TestRunner; drawn directly in single player / on a listen server, sent to the calling client on a
    /// dedicated server. Testing variant only.
    /// </summary>
    internal static class TestHud
    {
        public const ushort MsgId = 40150;
        private const double Scale = 0.7;
        private const double Padding = 0.014;
        private const double Edge = 0.985;          // screen edge margin (HUD space -1..1)
        private const int Columns = 5;
        // Column offsets in HUD units at Scale 0.7: Test, Run, State, Time, Note.
        private static readonly double[] ColumnX = { 0.0, 0.085, 0.135, 0.225, 0.31 };
        private static readonly Color BgColor = new Color(0, 0, 0, 175);

        private static HudAPIv2.HUDMessage[] _cols;
        private static HudAPIv2.HUDMessage _current;
        private static HudAPIv2.BillBoardHUDMessage _listBg, _currentBg;
        private static readonly StringBuilder[] _colText = new StringBuilder[Columns];
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
            if (_cols != null) return true;
            if (!HudHandler.IsApiReady) return false;
            _listBg = NewBackground();
            _currentBg = NewBackground();
            _cols = new HudAPIv2.HUDMessage[Columns];
            for (int i = 0; i < Columns; i++)
            {
                _colText[i] = new StringBuilder();
                _cols[i] = NewText(_colText[i]);
            }
            _current = NewText(_currentText);
            return true;
        }

        private static HudAPIv2.BillBoardHUDMessage NewBackground()
        {
            var bg = new HudAPIv2.BillBoardHUDMessage(
                Material: MyStringId.GetOrCompute("Square"), Origin: Vector2D.Zero, BillBoardColor: BgColor, Scale: 1d,
                Width: 0.01f, Height: 0.01f, HideHud: false, Shadowing: true, Blend: BlendTypeEnum.PostPP);
            bg.Visible = false;
            return bg;
        }

        private static HudAPIv2.HUDMessage NewText(StringBuilder text)
        {
            var msg = new HudAPIv2.HUDMessage(
                Message: text, Origin: Vector2D.Zero, Scale: Scale, HideHud: false, Shadowing: true, ShadowColor: Color.Black,
                Blend: BlendTypeEnum.PostPP);
            msg.InitialColor = Color.White;
            msg.Visible = false;
            return msg;
        }

        public static void Show(string list, string current)
        {
            if (!EnsureCreated()) return;
            FillColumns(list ?? "");
            _currentText.Clear().Append(current ?? "");
            LayoutList();
            LayoutCurrent();
            _shown = true;
        }

        /// <summary>Lines with tab-separated cells -> one text block per column (lines without tabs go to column 0).</summary>
        private static void FillColumns(string list)
        {
            for (int c = 0; c < Columns; c++) _colText[c].Clear();
            var lines = list.Split('\n');
            for (int l = 0; l < lines.Length; l++)
            {
                if (l == lines.Length - 1 && lines[l].Length == 0) break;
                var cells = lines[l].Split('\t');
                for (int c = 0; c < Columns; c++)
                {
                    if (c < cells.Length) _colText[c].Append(cells[c]);
                    _colText[c].Append('\n');
                }
            }
        }

        private static void LayoutList()
        {
            var visible = _colText[0].Length > 0;
            for (int c = 0; c < Columns; c++) _cols[c].Visible = visible;
            _listBg.Visible = visible;
            if (!visible) return;
            // Measure: width = widest column end (offset + its text), height = column 0 (it has every line).
            double width = 0, height = 0;
            for (int c = 0; c < Columns; c++)
            {
                _cols[c].Origin = new Vector2D(0, 0);
                var size = _cols[c].GetTextLength();
                var end = ColumnX[c] + Math.Abs(size.X);
                if (end > width) width = end;
                if (Math.Abs(size.Y) > height) height = Math.Abs(size.Y);
            }
            // Anchor the panel's top-right corner to the screen's top-right corner.
            var left = Edge - Padding - width;
            var top = Edge - Padding;
            for (int c = 0; c < Columns; c++) _cols[c].Origin = new Vector2D(left + ColumnX[c], top);
            SetBackground(_listBg, left, top, width, height);
        }

        private static void LayoutCurrent()
        {
            var visible = _currentText.Length > 0;
            _current.Visible = visible;
            _currentBg.Visible = visible;
            if (!visible) return;
            _current.Origin = new Vector2D(0, 0);
            var size = _current.GetTextLength();
            var width = Math.Abs(size.X);
            var height = Math.Abs(size.Y);
            // Anchor the panel's top-left corner to the screen's top-left corner.
            var left = -Edge + Padding;
            var top = Edge - Padding;
            _current.Origin = new Vector2D(left, top);
            SetBackground(_currentBg, left, top, width, height);
        }

        private static void SetBackground(HudAPIv2.BillBoardHUDMessage bg, double left, double top, double width, double height)
        {
            var w = width + Padding * 2;
            var h = height + Padding * 2;
            bg.Origin = new Vector2D(left - Padding + w / 2, top + Padding - h / 2);
            bg.Width = (float)w;
            bg.Height = (float)h;
        }

        public static void Hide()
        {
            if (!_shown) return;
            if (_cols != null) for (int c = 0; c < Columns; c++) _cols[c].Visible = false;
            if (_listBg != null) _listBg.Visible = false;
            if (_current != null) { _current.Visible = false; _currentBg.Visible = false; }
            _shown = false;
        }

        private static void Destroy()
        {
            try
            {
                if (_cols != null) for (int c = 0; c < Columns; c++) { _cols[c].Visible = false; _cols[c].DeleteMessage(); }
                if (_current != null) { _current.Visible = false; _current.DeleteMessage(); }
                if (_listBg != null) { _listBg.Visible = false; _listBg.DeleteMessage(); }
                if (_currentBg != null) { _currentBg.Visible = false; _currentBg.DeleteMessage(); }
            }
            catch { }
            _cols = null;
            _current = null;
            _listBg = _currentBg = null;
            _shown = false;
        }
    }
}
