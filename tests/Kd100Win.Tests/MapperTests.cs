using Kd100Win.Config;
using Kd100Win.Device;
using Kd100Win.Engine;

namespace Kd100Win.Tests;

public class MapperTests
{
    // Only actions that never call SendInput, so the tests do not type into the desktop.
    const string Json = """
        {
          "menus": { "m": [
            { "label": "One", "action": "dial:First" },
            { "label": "Two", "action": "dial:Second" },
            { "label": "Three", "action": "layer:toggle" }
          ] },
          "profiles": [
            {
              "name": "Default",
              "buttons": {
                "0": "dial:next", "1": "layer", "2": "layer:toggle", "3": "dial:Second",
                "4": { "tap": "dial:Second", "double": "layer:toggle" },
                "5": { "tap": "dial:Second", "hold": "layer" },
                "6": { "tap": "dial:Second", "menu": "m" }
              },
              "dial": [ { "name": "First", "cw": "none", "ccw": "none" }, { "name": "Second", "cw": "none", "ccw": "none" } ],
              "layer": { "dial": [ { "name": "Layered", "cw": "none", "ccw": "none" } ] }
            },
            { "name": "App", "apps": [ "app" ], "dial": [ { "name": "AppMode", "cw": "none", "ccw": "none" } ] }
          ]
        }
        """;

    sealed class ManualScheduler : IScheduler
    {
        readonly List<(Action Action, bool[] Cancelled)> _pending = [];

        public IDisposable After(int ms, Action action)
        {
            var cancelled = new bool[1];
            _pending.Add((action, cancelled));
            return new Cancel(cancelled);
        }

        public void RunAll()
        {
            var due = _pending.ToList();
            _pending.Clear();
            foreach (var (action, cancelled) in due)
            {
                if (!cancelled[0])
                    action();
            }
        }

        sealed class Cancel(bool[] flag) : IDisposable
        {
            public void Dispose() => flag[0] = true;
        }
    }

    sealed class FakeMenuView : IRadialMenuView
    {
        public int Selected { get; private set; } = -1;
        public bool Visible { get; private set; }

        public void Show(RadialMenu menu, int selected) => (Visible, Selected) = (true, selected);

        public void Hide() => Visible = false;
    }

    readonly ManualScheduler _scheduler = new();
    readonly FakeMenuView _menu = new();

    Mapper NewMapper() => new(ConfigStore.Parse(Json), _scheduler, _menu);

    static Kd100Report Buttons(params int[] held) => new(ReportKind.Buttons, held.Sum(b => 1 << b), 0);

    static Kd100Report Dial(int direction) => new(ReportKind.Dial, 0, direction);

    static void Click(Mapper m, int button)
    {
        m.OnReport(Buttons(button));
        m.OnReport(Buttons());
    }

    [Fact]
    public void Dial_button_cycles_modes_per_profile()
    {
        var m = NewMapper();
        Assert.Equal("First", m.DialMode!.Name);
        Click(m, 0);
        Assert.Equal("Second", m.DialMode!.Name);
        Click(m, 0);
        Assert.Equal("First", m.DialMode!.Name);

        m.OnForegroundProcess("app");
        Assert.Equal("AppMode", m.DialMode!.Name);
        m.OnForegroundProcess("explorer");
        Assert.Equal("First", m.DialMode!.Name);
    }

    [Fact]
    public void Buttons_missing_in_an_app_profile_fall_back_to_default()
    {
        var m = NewMapper();
        m.OnForegroundProcess("app");
        Click(m, 3);
        Assert.Equal("AppMode", m.DialMode!.Name); // "dial:Second" does not exist in App; mode unchanged
        m.OnForegroundProcess("other");
        Click(m, 3);
        Assert.Equal("Second", m.DialMode!.Name);
    }

    [Fact]
    public void Layer_is_active_while_held_and_switches_dial_modes()
    {
        var m = NewMapper();
        m.OnReport(Buttons(1));
        Assert.True(m.LayerActive);
        Assert.Equal("Layered", m.DialMode!.Name);
        m.OnReport(Buttons());
        Assert.False(m.LayerActive);
        Assert.Equal("First", m.DialMode!.Name);
    }

    [Fact]
    public void Layer_toggle()
    {
        var m = NewMapper();
        Click(m, 2);
        Assert.True(m.LayerActive);
        Click(m, 2);
        Assert.False(m.LayerActive);
    }

    [Fact]
    public void Repeated_button_reports_do_not_retrigger()
    {
        var m = NewMapper();
        m.OnReport(Buttons(0));
        m.OnReport(Buttons(0)); // the device resends the state while a key is held
        m.OnReport(Buttons(0));
        m.OnReport(Buttons());
        Assert.Equal("Second", m.DialMode!.Name);
    }

    [Fact]
    public void Single_tap_fires_after_the_double_timeout()
    {
        var m = NewMapper();
        Click(m, 4);
        Assert.Equal("First", m.DialMode!.Name); // still waiting for a second press
        _scheduler.RunAll();
        Assert.Equal("Second", m.DialMode!.Name);
        Assert.False(m.LayerActive);
    }

    [Fact]
    public void Double_tap_fires_only_the_double_action()
    {
        var m = NewMapper();
        Click(m, 4);
        Click(m, 4);
        _scheduler.RunAll();
        Assert.True(m.LayerActive);
        Assert.Equal("Layered", m.DialMode!.Name); // the tap ("dial:Second") never fired
    }

    [Fact]
    public void Hold_fires_hold_action_until_release_and_skips_tap()
    {
        var m = NewMapper();
        m.OnReport(Buttons(5));
        _scheduler.RunAll(); // hold timeout
        Assert.True(m.LayerActive);
        m.OnReport(Buttons());
        Assert.False(m.LayerActive);
        Assert.Equal("First", m.DialMode!.Name);
    }

    [Fact]
    public void Short_press_of_a_hold_button_is_a_tap()
    {
        var m = NewMapper();
        Click(m, 5);
        _scheduler.RunAll(); // the cancelled hold timer must not fire
        Assert.False(m.LayerActive);
        Assert.Equal("Second", m.DialMode!.Name);
    }

    [Fact]
    public void Holding_a_menu_button_and_turning_the_dial_picks_an_item()
    {
        var m = NewMapper();
        m.OnReport(Buttons(6));
        m.OnReport(Dial(1));
        Assert.True(_menu.Visible);
        Assert.Equal(0, _menu.Selected);
        m.OnReport(Dial(1));
        m.OnReport(Dial(1));
        m.OnReport(Dial(1)); // wraps around
        Assert.Equal(0, _menu.Selected);
        m.OnReport(Dial(-1));
        Assert.Equal(2, _menu.Selected);
        m.OnReport(Buttons());
        Assert.False(_menu.Visible);
        Assert.True(m.LayerActive); // item "Three" ran; the tap did not
        Assert.Equal("Layered", m.DialMode!.Name);
    }

    [Fact]
    public void Menu_button_without_turning_is_a_tap()
    {
        var m = NewMapper();
        Click(m, 6);
        Assert.False(_menu.Visible);
        Assert.Equal("Second", m.DialMode!.Name);
    }

    [Theory]
    [InlineData(500, 1)]
    [InlineData(150, 1)]
    [InlineData(20, 3)]
    [InlineData(5, 3)]
    public void Acceleration(long ms, int expected)
    {
        var mode = new DialMode("m", NoAction.Instance, NoAction.Instance, 1, 2);
        Assert.Equal(expected, Mapper.Repeats(mode, ms));
    }

    [Fact]
    public void Step_without_acceleration_is_constant()
    {
        var mode = new DialMode("m", NoAction.Instance, NoAction.Instance, 4, 0);
        Assert.Equal(4, Mapper.Repeats(mode, 1));
        Assert.Equal(4, Mapper.Repeats(mode, long.MaxValue));
    }
}
