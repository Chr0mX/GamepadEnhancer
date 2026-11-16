using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace GamepadEnhancer
{
    /// <summary>
    /// A separate window for the controller monitor: optionally always on top, and optionally an overlay (no frame, transparent
    /// background, only the controller). In overlay mode drag with the left button to move it; Esc or a right click closes it.
    /// </summary>
    internal sealed class MonitorWindow : Window
    {
        private readonly MonitorPanel panel;

        public MonitorWindow(ControllerEngine engine, AppSettings settings, PadView view)
        {
            bool overlay = settings.MonitorOverlayMode;
            OverlayMode = overlay;
            Title = "GamepadEnhancer Monitor";
            Topmost = settings.MonitorAlwaysOnTop;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = !overlay;

            panel = new MonitorPanel(engine)
            {
                View = view,
                RefreshHz = settings.MonitorRefreshHz,
                ScalePercent = settings.MonitorScalePercent,
                Compact = settings.MonitorCompact,
                Minimal = overlay
            };

            if (overlay)
            {
                SystemDecorations = SystemDecorations.None;
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
                Background = Brushes.Transparent;
                CanResize = false;
                Content = panel;
                PointerPressed += (_, e) =>
                {
                    PointerPointProperties p = e.GetCurrentPoint(this).Properties;
                    if (p.IsLeftButtonPressed) BeginMoveDrag(e);
                    else if (p.IsRightButtonPressed) Close();
                };
            }
            else
            {
                Background = new SolidColorBrush(Color.Parse("#16171C"));
                Content = new Border { Padding = new Thickness(12), Child = panel };
            }

            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            Opened += (_, _) => panel.Start();
            Closed += (_, _) => panel.Stop();
        }

        public PadView View { get => panel.View; set => panel.View = value; }

        /// <summary>Whether this window was built as a frameless overlay (that can only change by building a new window).</summary>
        public bool OverlayMode { get; }

        /// <summary>Applies everything that can change while the window is open.</summary>
        public void ApplyLive(AppSettings settings, PadView view)
        {
            Topmost = settings.MonitorAlwaysOnTop;
            panel.View = view;
            panel.RefreshHz = settings.MonitorRefreshHz;
            panel.ScalePercent = settings.MonitorScalePercent;
            panel.Compact = settings.MonitorCompact;
        }
    }
}
