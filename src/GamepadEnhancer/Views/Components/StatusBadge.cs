using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Shapes;

namespace GamepadEnhancer
{
    internal enum BadgeState { Off, Ok, Warn, Error }

    /// <summary>A status dot followed by text ("Connected", "Off"). One component for every connection/status indicator.</summary>
    internal sealed class StatusBadge : StackPanel
    {
        private readonly Ellipse dot = new() { Classes = { "dot" }, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
        private BadgeState state = BadgeState.Off;

        public StatusBadge()
        {
            Orientation = Orientation.Horizontal;
            Spacing = 8;
            Children.Add(dot);
            Children.Add(label);
        }

        public string Text { get => label.Text; set => label.Text = value; }

        public BadgeState State
        {
            get => state;
            set
            {
                if (state == value) return;
                state = value;
                dot.Classes.Set("ok", value == BadgeState.Ok);
                dot.Classes.Set("warn", value == BadgeState.Warn);
                dot.Classes.Set("err", value == BadgeState.Error);
            }
        }

        public void Set(BadgeState s, string text) { State = s; Text = text; }
    }
}
