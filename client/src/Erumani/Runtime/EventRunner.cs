using Srpg95.Data;

namespace Erumani.Runtime;

public enum EventEnd { None, EndEvent, MapWon, MapLost, GameOver, ReturnToTitle }

/// <summary>Per-frame input the interpreter cares about.</summary>
public readonly record struct EventInput(bool Confirm, bool Cancel, bool Up, bool Down, bool FastForward);

/// <summary>What the message window should show right now.</summary>
public sealed record MessageView(int Window, IReadOnlyList<string> Lines, int Face, bool FaceRight, bool FaceFlip);

/// <summary>
/// Runs one event script tree (FORMAT_NOTES §11.3 for the command semantics).
/// Blocking commands: message, character choice, wait, screen fades, shake,
/// execute movement and button input. Everything else runs instantly.
/// Behaviours marked "provisional" still need checking against the original runtime.
/// </summary>
public sealed class EventRunner
{
    readonly GameState _state;
    readonly IMapContext _map;
    readonly Audio _audio;
    readonly ScreenEffects _fx;
    readonly Random _rng = new();
    readonly Stack<(List<ScriptNode> Nodes, int Pc)> _stack = new();

    int _face, _waitFrames;
    bool _faceRight, _faceFlip, _waitButton, _waitFx, _waitMoves;

    public string Name { get; private set; } = "";
    public MessageView? Message { get; private set; }
    /// <summary>The message that stays visible underneath a character choice (as in the original).</summary>
    public MessageView? LastMessage { get; private set; }
    public string[]? Choice { get; private set; }
    public int ChoiceIndex { get; private set; }
    public EventEnd End { get; private set; }
    /// <summary>Set by "Next Map" (select_map): 0-based map index to go to on map completion.</summary>
    public int? NextMap { get; private set; }
    public bool Running => _stack.Count > 0;

    /// <summary>Mouse hover over a choice line.</summary>
    public void HoverChoice(int index)
    {
        if (Choice is not null && index >= 0 && index < Choice.Length) ChoiceIndex = index;
    }
    public List<string> Log { get; } = new();
    /// <summary>Holding fast-forward auto-advances messages (option).</summary>
    public bool FastForwardAdvancesText { get; set; } = true;
    /// <summary>Every message shown, for the backlog.</summary>
    public Action<MessageView>? Shown { get; set; }

    ChoiceNode? _choiceNode;
    int _messageAge;

    public EventRunner(GameState state, IMapContext map, Audio audio, ScreenEffects fx)
        => (_state, _map, _audio, _fx) = (state, map, audio, fx);

    public void Start(EventScript script)
    {
        _stack.Clear();
        Name = script.Name;
        End = EventEnd.None;
        Message = null;
        Choice = null;
        if (script.Error is not null)
        {
            Trace($"{script.Name}: damaged script not run ({script.Error})");   // T13: provisional
            return;
        }
        _stack.Push((script.Tree, 0));
    }

    void Trace(string s)
    {
        Log.Add(s);
        if (Log.Count > 200) Log.RemoveAt(0);
    }

    public void Update(EventInput input)
    {
        if (!Running) return;

        // --- blocked states ---
        if (Message is not null)
        {
            _messageAge++;
            bool auto = input.FastForward && FastForwardAdvancesText && _messageAge > 4;
            if (input.Confirm) Sfx.MessageAdvance();
            if (input.Confirm || auto) Message = null;
            else return;
        }
        if (Choice is not null)
        {
            if (input.Up) ChoiceIndex = (ChoiceIndex + Choice.Length - 1) % Choice.Length;
            if (input.Down) ChoiceIndex = (ChoiceIndex + 1) % Choice.Length;
            if (!input.Confirm) return;
            Sfx.Click();
            var body = ChoiceIndex < _choiceNode!.Cases.Count ? _choiceNode.Cases[ChoiceIndex] : new List<ScriptNode>();
            Trace($"choice -> {Choice[ChoiceIndex]}");
            Choice = null;
            LastMessage = null;
            _stack.Push((body, 0));
        }
        if (_waitFrames > 0) { _waitFrames -= input.FastForward ? 4 : 1; if (_waitFrames > 0) return; }
        if (_waitButton) { if (!input.Confirm) return; _waitButton = false; }
        if (_waitFx) { if (_fx.Busy) return; _waitFx = false; }
        if (_waitMoves) { if (_map.MovesRunning) return; _waitMoves = false; }

        // --- run until something blocks ---
        while (_stack.Count > 0)
        {
            var (nodes, pc) = _stack.Pop();
            if (pc >= nodes.Count) continue;
            _stack.Push((nodes, pc + 1));
            if (Execute(nodes[pc])) return;
        }
    }

    /// <summary>Executes one node; returns true if the interpreter must wait.</summary>
    bool Execute(ScriptNode node)
    {
        switch (node)
        {
            case MessageNode m:
                Message = LastMessage = new MessageView(m.Window, m.Lines, _face, _faceRight, _faceFlip);
                Shown?.Invoke(Message);
                _messageAge = 0;
                return true;
            case BranchNode b:
                bool ok = Evaluate(b.Condition);
                Trace($"if {Describe(b.Condition)} -> {ok}");
                if (ok) _stack.Push((b.Then, 0));
                return false;
            case ChoiceNode c:
                _choiceNode = c;
                Choice = c.Options;
                ChoiceIndex = 0;
                return true;
            case CommandNode { Command: var cmd }:
                return Run(cmd);
        }
        return false;
    }

    bool Run(Command c)
    {
        switch (c.Op)
        {
            case Op.FaceGraphic:
                _faceRight = c.Payload[0] == 1;
                _faceFlip = c.Payload[1] == 1;
                _face = c.U16(2);
                return false;
            case Op.SwitchOperation:
                if (c.U16(2) < GameState.SwitchCount) _state.Switches[c.U16(2)] = c.U16(0) == 1;
                Trace($"switch {c.U16(2)} = {(c.U16(0) == 1 ? "ON" : "OFF")}");
                return false;
            case Op.ItemManagement:
                _state.AddItem(c.U16(2), c.U16(0) == 1 ? c.U16(4) : -c.U16(4));
                return false;
            case Op.GoldManagement:
                _state.Gold = Math.Max(0, _state.Gold + (c.U16(0) == 1 ? c.I32(2) : -c.I32(2)));
                return false;
            case Op.ScreenDisplay:
            case Op.EraseScreen:
                _fx.StartFade(c.Op == Op.EraseScreen, c.Payload[0], c.Payload[1]);
                _waitFx = true;
                return true;
            case Op.PictureDisplay:
                _fx.ShowPicture(c.Str(6), c.Payload[1], c.Payload[0], c.Payload[2] == 1);
                return false;
            case Op.ErasePicture:
                _fx.ErasePicture(c.U16(0));
                return false;
            case Op.ScrollScreen:
                _map.ScrollTo(c.U16(0), c.U16(2));   // provisional: instant, centred on the tile
                return false;
            case Op.FlashTintScreen:
                _fx.FlashTint(c.U16(0), c.I16(2), c.I16(4), c.I16(6), c.I16(8));
                return false;
            case Op.ShakeScreen:
                _fx.Shake(c.U16(0));
                _waitFx = true;
                return true;
            case Op.ChangeAllyUnit:
                if (c.U16(0) == 1) _state.Party.Add(c.U16(2)); else _state.Party.Remove(c.U16(2));
                return false;
            case Op.UnitClassChange:
                _state.ClassOverride[c.U16(0)] = c.U16(2);
                return false;
            case Op.UnitEquipmentChange:
                _state.WeaponOverride[c.U16(0)] = c.U16(2);   // provisional: slot implied by item type
                return false;
            case Op.MoveUnit:
                _map.QueueMove(c.U16(0), c.Payload.Skip(4).Take(c.U16(2)).ToArray());
                return false;
            case Op.ExecuteMovement:
                _map.ExecuteMoves();
                _waitMoves = true;
                return true;
            case Op.Bgm:
                _audio.PlayBgm(c.Str(0));
                return false;
            case Op.SoundEffect:
                _audio.PlaySe(c.Str(0));
                return false;
            case Op.VariableOperation:
                RunVariable(c);
                return false;
            case Op.ButtonInput:
                _waitButton = true;
                return true;
            case Op.Wait:
                _waitFrames = c.U16(0) * 6;   // tenths of a second at 60 fps
                return true;
            case Op.SelectMap:
                NextMap = c.U16(0);
                Trace($"next map = {NextMap}");
                return false;
            case Op.EndEvent: Finish(EventEnd.EndEvent); return true;
            case Op.MapComplete: Finish(c.U16(0) == 1 ? EventEnd.MapWon : EventEnd.MapLost); return true;
            case Op.GameOver: Finish(EventEnd.GameOver); return true;
            case Op.ReturnToTitle: Finish(EventEnd.ReturnToTitle); return true;
            default:
                Trace($"unhandled {c.Op}");
                return false;
        }
    }

    void Finish(EventEnd how)
    {
        End = how;
        _stack.Clear();
        Trace($"end: {how}");
    }

    void RunVariable(Command c)
    {
        int v = c.U16(0);
        if (v >= GameState.VariableCount) return;
        int operand = c.U16(2) switch
        {
            1 => c.U16(6) < GameState.VariableCount ? _state.Variables[c.U16(6)] : 0,
            2 => _rng.Next(0, c.U16(8) + 1),     // provisional: 0..max inclusive
            _ => c.I16(4),
        };
        ref int x = ref _state.Variables[v];
        switch (c.U16(10))
        {
            case 0: x = operand; break;
            case 1: x += operand; break;
            case 2: x -= operand; break;
            case 3: x *= operand; break;
            case 4: if (operand != 0) x /= operand; break;   // provisional: divide by 0 leaves the value
        }
    }

    bool Evaluate(Command c)
    {
        var p = c.Payload;
        switch (c.U16(0))
        {
            case 0: return _state.Switches[Math.Min((int)c.U16(2), GameState.SwitchCount - 1)] == (p[12] == 0);
            case 1:
                // provisional: After = at or after, Before = before, Exact = equal (phase counter)
                int t = c.U16(4);
                return p[13] switch { 0 => _map.Phase >= t, 1 => _map.Phase < t, _ => _map.Phase == t };
            case 2: return (_state.ItemCount(c.U16(6)) > 0) == (p[14] == 0);
            case 3: return _map.UnitState(c.U16(8), p[15]);
            case 4:
                int val = _state.Variables[Math.Min((int)c.U16(10), GameState.VariableCount - 1)], cmp = c.I16(16);
                return c.U16(18) switch { 0 => val >= cmp, 1 => val <= cmp, _ => val == cmp };
        }
        return false;
    }

    static string Describe(Command c) => c.U16(0) switch
    {
        0 => $"switch {c.U16(2)} {(c.Payload[12] == 0 ? "ON" : "OFF")}",
        1 => $"turn {c.U16(4)} mode {c.Payload[13]}",
        2 => $"item {c.U16(6)} {(c.Payload[14] == 0 ? "owned" : "not owned")}",
        3 => $"unit {c.U16(8)} state {c.Payload[15]}",
        4 => $"var {c.U16(10)} {(c.U16(18) switch { 0 => ">=", 1 => "<=", _ => "==" })} {c.I16(16)}",
        _ => "?",
    };
}
