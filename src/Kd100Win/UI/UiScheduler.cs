using Kd100Win.Engine;

namespace Kd100Win.UI;

/// <summary>Runs engine callbacks (hold and double-press timeouts) on the UI thread.</summary>
internal sealed class UiScheduler : IScheduler
{
    public IDisposable After(int ms, Action action)
    {
        var timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, ms) };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            action();
        };
        timer.Start();
        return timer;
    }
}
