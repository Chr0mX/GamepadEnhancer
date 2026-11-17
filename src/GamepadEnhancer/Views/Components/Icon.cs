using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace GamepadEnhancer
{
    /// <summary>
    /// A stroke icon from <see cref="Icons"/>. It draws in the inherited Foreground, so inside a button, nav item or badge it follows
    /// that control's text colour (hover, active, disabled) without any per-state styling.
    /// </summary>
    internal sealed class Icon : TemplatedControl
    {
        public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<Icon, string>(nameof(Kind));

        static Icon()
        {
            AffectsRender<Icon>(KindProperty, ForegroundProperty);
        }

        public Icon()
        {
            Width = 16;
            Height = 16;
            IsHitTestVisible = false;
            Focusable = false;
        }

        public string Kind
        {
            get => GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            Geometry geometry = Icons.Get(Kind);
            if (geometry == null || Foreground == null) return;
            double scale = System.Math.Min(Bounds.Width, Bounds.Height) / 24.0;
            if (scale <= 0) return;
            var pen = new Pen(Foreground, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
                context.DrawGeometry(null, pen, geometry);
        }
    }
}
