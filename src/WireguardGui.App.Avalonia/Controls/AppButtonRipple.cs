using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace WireguardGui.App.Avalonia.Controls;

internal static class AppButtonRipple
{
    private const double ExpandPixels = 10;
    private const int Steps = 16;
    private static readonly TimeSpan StepInterval = TimeSpan.FromMilliseconds(14);
    private static readonly TimeSpan WaveStagger = TimeSpan.FromMilliseconds(50);
    private static readonly Dictionary<Border, DispatcherTimer> ActiveTimers = new();

    public static void Play(IReadOnlyList<Border> waves, IBrush rippleBrush)
    {
        for (var i = 0; i < waves.Count; i++)
        {
            var peakOpacity = 0.46 - i * 0.115;
            var delay = TimeSpan.FromMilliseconds(i * WaveStagger.TotalMilliseconds);
            SpawnWave(waves[i], rippleBrush, peakOpacity, delay);
        }
    }

    private static void SpawnWave(Border wave, IBrush rippleBrush, double peakOpacity, TimeSpan delay)
    {
        StopWave(wave);

        if (delay <= TimeSpan.Zero)
        {
            RunWave(wave, rippleBrush, peakOpacity);
            return;
        }

        DispatcherTimer? delayTimer = null;
        delayTimer = new DispatcherTimer { Interval = delay };
        delayTimer.Tick += (_, _) =>
        {
            delayTimer!.Stop();
            RunWave(wave, rippleBrush, peakOpacity);
        };
        delayTimer.Start();
    }

    private static void RunWave(Border wave, IBrush rippleBrush, double peakOpacity)
    {
        wave.BorderBrush = rippleBrush;
        wave.Margin = new Thickness(0);
        wave.Opacity = peakOpacity;

        var step = 0;
        DispatcherTimer? timer = null;
        timer = new DispatcherTimer { Interval = StepInterval };
        timer.Tick += (_, _) =>
        {
            step++;
            var progress = EaseOutCubic((double)step / Steps);
            var spread = ExpandPixels * progress;

            wave.Margin = new Thickness(-spread);
            wave.Opacity = peakOpacity * (1 - progress);

            if (step < Steps)
                return;

            StopWave(wave);
            wave.Margin = new Thickness(0);
            wave.Opacity = 0;
            wave.BorderBrush = Brushes.Transparent;
        };

        ActiveTimers[wave] = timer;
        timer.Start();
    }

    private static void StopWave(Border wave)
    {
        if (!ActiveTimers.Remove(wave, out var timer))
            return;

        timer.Stop();
    }

    private static double EaseOutCubic(double value) =>
        1 - Math.Pow(1 - value, 3);
}
