using Kd100Win.Device;

namespace Kd100Win.Engine;

/// <summary>Runs a callback later on the engine's thread.</summary>
internal interface IScheduler
{
    IDisposable After(int ms, Action action);
}

/// <summary>Shows the radial menu with the given item selected.</summary>
internal interface IRadialMenuView
{
    void Show(RadialMenu menu, int selected);
    void Hide();
}

/// <summary>
/// Turns KD100 reports into actions: button gestures (tap, double, hold, hold + dial menu),
/// the layer, the active profile and its dial mode, and dial acceleration.
/// </summary>
internal sealed class Mapper : IActionHost
{
    enum Phase
    {
        Simple,         // plain action, pressed while the button is held
        Down,           // gesture button pressed, outcome not known yet
        Holding,        // hold action pressed
        Menu,           // radial menu open, the dial moves the selection
        WaitingDouble,  // released once, waiting for a second press
        Consumed,       // second press of a double; nothing happens on release
    }

    sealed class ButtonState(ButtonBinding binding, Phase phase)
    {
        public ButtonBinding Binding { get; } = binding;
        public Phase Phase { get; set; } = phase;
        public IDisposable? Timer { get; set; }
        public int MenuSelection { get; set; } = -1;
    }

    static readonly ButtonBinding Nothing = new(NoAction.Instance, null, null, null);

    readonly ButtonState?[] _states = new ButtonState?[Kd100Protocol.ButtonCount];
    readonly Dictionary<string, int> _dialModeByProfile = new(StringComparer.OrdinalIgnoreCase);
    readonly IScheduler _scheduler;
    readonly IRadialMenuView? _menuView;
    readonly Func<long> _clock;
    int _buttons;
    int _layerHolds;
    bool _layerToggled;
    long _lastDialTick;
    int _lastDialDirection;

    public Mapper(CompiledConfig config, IScheduler scheduler, IRadialMenuView? menuView = null, Func<long>? clock = null)
    {
        Config = config;
        Profile = config.Default;
        _scheduler = scheduler;
        _menuView = menuView;
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public CompiledConfig Config { get; private set; }

    public Profile Profile { get; private set; }

    public bool LayerActive => _layerHolds > 0 || _layerToggled;

    public bool Paused { get; set; }

    /// <summary>
    /// Raised for user-visible state changes (profile, dial mode, layer) with a short description,
    /// or null when the on-screen popup should just disappear (a held layer key was released).
    /// </summary>
    public event Action<string?>? StateChanged;

    /// <summary>Raised for every button/dial event with what it did, for the input monitor.</summary>
    public event Action<string>? Activity;

    public IReadOnlyList<DialMode> DialModes =>
        LayerActive && Profile.LayerDial.Count > 0 ? Profile.LayerDial : Profile.Dial.Count > 0 ? Profile.Dial : Config.Default.Dial;

    public DialMode? DialMode
    {
        get
        {
            var modes = DialModes;
            if (modes.Count == 0)
                return null;
            int i = _dialModeByProfile.GetValueOrDefault(DialKey, 0);
            return modes[Math.Clamp(i, 0, modes.Count - 1)];
        }
    }

    string DialKey => Profile.Name + (LayerActive && Profile.LayerDial.Count > 0 ? "/layer" : "");

    public void SetConfig(CompiledConfig config, string? processName)
    {
        ReleaseAll();
        Config = config;
        _dialModeByProfile.Clear();
        _layerToggled = false;
        Profile = config.ForProcess(processName);
    }

    public void OnForegroundProcess(string? processName)
    {
        var p = Config.ForProcess(processName);
        if (p == Profile)
            return;
        Profile = p;
        _layerToggled = false;
        StateChanged?.Invoke(Describe());
    }

    public void OnReport(Kd100Report report)
    {
        switch (report.Kind)
        {
            case ReportKind.Buttons:
                OnButtons(report.Buttons);
                break;
            case ReportKind.Dial:
                OnDial(report.DialDirection);
                break;
        }
    }

    void OnButtons(int mask)
    {
        int changed = mask ^ _buttons;
        _buttons = mask;
        for (int b = 0; b < Kd100Protocol.ButtonCount; b++)
        {
            if ((changed >> b & 1) == 0)
                continue;
            if ((mask >> b & 1) != 0)
                OnPress(b);
            else
                OnRelease(b);
        }
    }

    void OnPress(int b)
    {
        if (_states[b] is { Phase: Phase.WaitingDouble } waiting)
        {
            waiting.Timer?.Dispose();
            waiting.Phase = Phase.Consumed;
            Activity?.Invoke($"button {b} double -> {waiting.Binding.Double}");
            Fire(waiting.Binding.Double);
            return;
        }

        var binding = Resolve(b);
        Activity?.Invoke($"button {b} ({Kd100Protocol.Describe(b)}) down -> {binding} [{Profile.Name}{(LayerActive ? ", layer" : "")}]");
        if (Paused)
            return;

        if (binding.IsSimple)
        {
            _states[b] = new ButtonState(binding, Phase.Simple);
            binding.Tap.Press(this);
            return;
        }

        var state = new ButtonState(binding, Phase.Down);
        _states[b] = state;
        if (binding.Hold is { } hold)
        {
            state.Timer = _scheduler.After(Config.HoldMs, () =>
            {
                if (_states[b] != state || state.Phase != Phase.Down)
                    return;
                state.Phase = Phase.Holding;
                Activity?.Invoke($"button {b} hold -> {hold}");
                hold.Press(this);
            });
        }
    }

    void OnRelease(int b)
    {
        var state = _states[b];
        Activity?.Invoke($"button {b} up");
        if (state == null)
            return;

        switch (state.Phase)
        {
            case Phase.Simple:
                _states[b] = null;
                state.Binding.Tap.Release(this);
                break;
            case Phase.Holding:
                _states[b] = null;
                state.Binding.Hold!.Release(this);
                break;
            case Phase.Menu:
                _states[b] = null;
                CloseMenu(state, execute: true);
                break;
            case Phase.Down when state.Binding.Double != null:
                state.Timer?.Dispose();
                state.Phase = Phase.WaitingDouble;
                state.Timer = _scheduler.After(Config.DoubleMs, () =>
                {
                    if (_states[b] != state || state.Phase != Phase.WaitingDouble)
                        return;
                    _states[b] = null;
                    Fire(state.Binding.Tap);
                });
                break;
            case Phase.Down:
                state.Timer?.Dispose();
                _states[b] = null;
                Fire(state.Binding.Tap);
                break;
            default:
                _states[b] = null;
                break;
        }
    }

    void OnDial(int direction)
    {
        if (MenuTarget() is { } menuState)
        {
            MoveMenu(menuState, direction);
            return;
        }

        var mode = DialMode;
        long now = _clock();
        long dt = direction == _lastDialDirection ? now - _lastDialTick : long.MaxValue;
        _lastDialTick = now;
        _lastDialDirection = direction;

        if (mode == null)
        {
            Activity?.Invoke($"dial {(direction > 0 ? "CW" : "CCW")} -> (no dial modes)");
            return;
        }
        int repeats = Repeats(mode, dt);
        var action = direction > 0 ? mode.Cw : mode.Ccw;
        Activity?.Invoke($"dial {(direction > 0 ? "CW" : "CCW")} -> {action} x{repeats} [{Profile.Name}: {mode.Name}]");
        if (!Paused)
            action.Step(this, repeats);
    }

    /// <summary>The open menu, or a held button whose menu the dial should open.</summary>
    ButtonState? MenuTarget()
    {
        ButtonState? candidate = null;
        foreach (var s in _states)
        {
            if (s is { Phase: Phase.Menu })
                return s;
            if (s is { Phase: Phase.Down, Binding.Menu: not null })
                candidate ??= s;
        }
        return candidate;
    }

    void MoveMenu(ButtonState state, int direction)
    {
        var menu = state.Binding.Menu!;
        int count = menu.Items.Count;
        if (state.Phase != Phase.Menu)
        {
            // The first detent opens the menu: clockwise selects the first item, counter-clockwise the last.
            state.Timer?.Dispose();
            state.Phase = Phase.Menu;
            state.MenuSelection = direction > 0 ? 0 : count - 1;
        }
        else
        {
            state.MenuSelection = ((state.MenuSelection + direction) % count + count) % count;
        }
        Activity?.Invoke($"menu {menu.Name}: {menu.Items[state.MenuSelection].Label}");
        _menuView?.Show(menu, state.MenuSelection);
    }

    void CloseMenu(ButtonState state, bool execute)
    {
        _menuView?.Hide();
        var menu = state.Binding.Menu!;
        if (execute && state.MenuSelection >= 0 && state.MenuSelection < menu.Items.Count)
        {
            var item = menu.Items[state.MenuSelection];
            Activity?.Invoke($"menu {menu.Name} -> {item.Label}: {item.Action}");
            Fire(item.Action);
        }
    }

    void Fire(KdAction? action)
    {
        if (action != null && !Paused)
            action.Step(this, 1);
    }

    /// <summary>
    /// Detents closer together than ~150 ms count as "fast". The multiplier ramps from 1
    /// (slow) to 1 + acceleration (detents 20 ms or less apart).
    /// </summary>
    public static int Repeats(DialMode mode, long msSinceLastStep)
    {
        double speed = Math.Clamp((150.0 - msSinceLastStep) / 130.0, 0, 1);
        return Math.Max(1, (int)Math.Round(mode.Step * (1 + mode.Acceleration * speed)));
    }

    ButtonBinding Resolve(int button)
    {
        if (LayerActive && Profile.LayerButtons[button] is { } layered)
            return layered;
        return Profile.Buttons[button] ?? Config.Default.Buttons[button] ?? Nothing;
    }

    public void ReleaseAll()
    {
        for (int b = 0; b < _states.Length; b++)
        {
            var s = _states[b];
            _states[b] = null;
            if (s == null)
                continue;
            s.Timer?.Dispose();
            switch (s.Phase)
            {
                case Phase.Simple: s.Binding.Tap.Release(this); break;
                case Phase.Holding: s.Binding.Hold!.Release(this); break;
                case Phase.Menu: CloseMenu(s, execute: false); break;
            }
        }
        _buttons = 0;
        _layerHolds = 0;
    }

    public string Describe()
    {
        string s = Profile.Name;
        if (DialMode is { } m)
            s += $" · {m.Name}";
        if (LayerActive)
            s += " · layer 2";
        return s;
    }

    string DescribeLayer() => (LayerActive ? "Layer 2" : "Layer 1") + (DialMode is { } m ? $" · dial: {m.Name}" : "");

    void IActionHost.HoldLayer(bool down)
    {
        bool was = LayerActive;
        _layerHolds = Math.Max(0, _layerHolds + (down ? 1 : -1));
        if (was != LayerActive)
            StateChanged?.Invoke(LayerActive ? DescribeLayer() : null);
    }

    void IActionHost.ToggleLayer()
    {
        _layerToggled = !_layerToggled;
        StateChanged?.Invoke(DescribeLayer());
    }

    void IActionHost.CycleDialMode(int delta)
    {
        var modes = DialModes;
        if (modes.Count == 0)
            return;
        int i = _dialModeByProfile.GetValueOrDefault(DialKey, 0);
        _dialModeByProfile[DialKey] = ((i + delta) % modes.Count + modes.Count) % modes.Count;
        StateChanged?.Invoke(Describe());
    }

    void IActionHost.SelectDialMode(string name)
    {
        var modes = DialModes;
        for (int i = 0; i < modes.Count; i++)
        {
            if (modes[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _dialModeByProfile[DialKey] = i;
                StateChanged?.Invoke(Describe());
                return;
            }
        }
        Log.Info($"dial: no mode named \"{name}\" in profile {Profile.Name}");
    }

    /// <summary>Selects a dial mode from the tray menu.</summary>
    public void SelectDialMode(int index)
    {
        if (index >= 0 && index < DialModes.Count)
        {
            _dialModeByProfile[DialKey] = index;
            StateChanged?.Invoke(Describe());
        }
    }
}
