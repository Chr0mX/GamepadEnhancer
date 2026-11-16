using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace GamepadEnhancer
{
    /// <summary>
    /// The live controller monitor: a <see cref="ControllerView"/> plus readouts (connection, stick/trigger values, update
    /// rate, recent button events). It reads the engine's lock-free <see cref="PadMonitor"/> on its own UI timer, so the
    /// controller output never waits on it. While started it registers as a viewer, which is what makes the engine build snapshots.
    /// </summary>
    internal sealed class MonitorPanel : UserControl
    {
        private const int HistoryLines = 10;
        private static readonly FontFamily Mono = new("Consolas, Menlo, monospace");

        private readonly PadMonitor monitor;
        private readonly ControllerView pad = new();
        private readonly TextBlock headline = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBlock detail = new() { FontFamily = Mono, FontSize = 12 };
        private readonly TextBlock history = new() { FontFamily = Mono, FontSize = 12 };
        private readonly StackPanel readouts;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly PadEvent[] eventBuffer = new PadEvent[64];
        private readonly LinkedList<string> lines = new();

        private PadView view = PadView.Final;
        private bool running, compact, minimal;
        private double lastTickMs, lastTextMs, rateMs;
        private long cursor, rateBase, lastVersion = -1;
        private double updatesPerSecond;
        private bool animating;

        public MonitorPanel(ControllerEngine engine)
        {
            monitor = engine.Snapshots;

            readouts = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            readouts.Children.Add(headline);
            readouts.Children.Add(detail);
            readouts.Children.Add(history);

            var root = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
            pad.HorizontalAlignment = HorizontalAlignment.Center;
            root.Children.Add(pad);
            root.Children.Add(readouts);
            Content = root;

            ScalePercent = 100;
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0) };
            timer.Tick += (_, _) => OnTick();
        }

        public PadView View
        {
            get => view;
            set { if (view != value) { view = value; cursor = monitor.EventCount; lines.Clear(); lastVersion = -1; } }
        }

        /// <summary>UI refresh rate (30..144 Hz). Independent of how often the engine sends reports.</summary>
        public int RefreshHz
        {
            get => (int)Math.Round(1000.0 / timer.Interval.TotalMilliseconds);
            set => timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(value, 30, 144));
        }

        /// <summary>Size of the drawn controller in percent of its base 440x300 (50..200).</summary>
        public int ScalePercent
        {
            set
            {
                double k = Math.Clamp(value, 50, 200) / 100.0;
                pad.Width = ControllerView.CanvasW * k;
                pad.Height = ControllerView.CanvasH * k;
            }
        }

        /// <summary>Compact: no button names, no event history.</summary>
        public bool Compact
        {
            set { compact = value; pad.ShowLabels = !value && !minimal; history.IsVisible = !value && !minimal; }
        }

        /// <summary>Minimal (overlay): only the controller, on a transparent background, no readouts.</summary>
        public bool Minimal
        {
            set
            {
                minimal = value;
                readouts.IsVisible = !value;
                pad.TransparentBackground = value;
                pad.ShowLabels = !compact && !value;
                history.IsVisible = !compact && !value;
            }
        }

        public void Start()
        {
            if (running) return;
            running = true;
            monitor.AddViewer();
            cursor = monitor.EventCount;
            rateBase = monitor.ReportsSent;
            rateMs = clock.Elapsed.TotalMilliseconds;
            lastTickMs = rateMs;
            lastVersion = -1;
            timer.Start();
        }

        public void Stop()
        {
            if (!running) return;
            running = false;
            timer.Stop();
            monitor.RemoveViewer();
        }

        private void OnTick()
        {
            double now = clock.Elapsed.TotalMilliseconds;
            double dt = now - lastTickMs;
            lastTickMs = now;

            pad.Family = monitor.Family;
            long version = monitor.Version;
            // Nothing new and no stick still easing back: skip the work entirely.
            if (version == lastVersion && !animating && now - lastTextMs < 500) return;
            lastVersion = version;

            PadState st = monitor.Get(view);
            animating = pad.Update(st, dt);
            if (minimal) return;

            if (now - lastTextMs >= 66)
            {
                lastTextMs = now;
                if (now - rateMs >= 500)
                {
                    long reports = monitor.ReportsSent;
                    updatesPerSecond = (reports - rateBase) * 1000.0 / (now - rateMs);
                    rateBase = reports;
                    rateMs = now;
                }
                UpdateText(st);
            }
        }

        private static string F(double v) => v.ToString("+0.00;-0.00;+0.00", CultureInfo.InvariantCulture);

        private void UpdateText(PadState st)
        {
            PadFamily family = monitor.Family;
            string status = !monitor.Connected ? "not connected"
                : monitor.IsRemote ? "connected (sending to the other PC)" : "connected (ViGEm virtual controller)";
            headline.Text = $"{PadLabels.FamilyName(family)}: {status}";

            var sb = new StringBuilder();
            sb.Append("View   ").Append(PadLabels.ViewName(view)).Append('\n');
            sb.Append("LS     X ").Append(F(st.Lx)).Append("  Y ").Append(F(st.Ly)).Append('\n');
            sb.Append("RS     X ").Append(F(st.Rx)).Append("  Y ").Append(F(st.Ry)).Append('\n');
            sb.Append(family == PadFamily.Xbox360 ? "LT/RT  " : "L2/R2  ")
              .Append(st.Lt.ToString("0.00", CultureInfo.InvariantCulture)).Append(" / ")
              .Append(st.Rt.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("Output ").Append(updatesPerSecond.ToString("0", CultureInfo.InvariantCulture)).Append(" reports/s   Display ")
              .Append(RefreshHz.ToString(CultureInfo.InvariantCulture)).Append(" Hz");
            detail.Text = sb.ToString();

            int n = monitor.CopyEventsSince(ref cursor, eventBuffer);
            for (int i = 0; i < n; i++)
            {
                PadEvent e = eventBuffer[i];
                if (e.View != view) continue;
                string t = TimeSpan.FromMilliseconds(e.TickMs).ToString(@"mm\:ss\.f", CultureInfo.InvariantCulture);
                lines.AddFirst($"{t}  {PadLabels.For(family, e.Action),-10} {(e.Pressed ? "pressed" : "released")}");
                if (lines.Count > HistoryLines) lines.RemoveLast();
            }
            if (n > 0 || history.Text == null)
                history.Text = lines.Count == 0 ? "Button history: (none yet)" : "Button history\n" + string.Join("\n", lines);
        }
    }
}
