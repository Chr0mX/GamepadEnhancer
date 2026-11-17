using Avalonia;
using Avalonia.Controls;

namespace GamepadEnhancer
{
    /// <summary>
    /// One setting: a title and a short explanation on the left, the control (toggle, number box, dropdown, ...) on the right.
    /// Used for every setting in the app so spacing, alignment and wrapping are identical everywhere. The template is in
    /// Styles/Components.axaml. On a narrow window the control drops under the text.
    /// </summary>
    internal sealed class SettingRow : ContentControl
    {
        public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<SettingRow, string>(nameof(Title), "");
        public static readonly StyledProperty<string> DescriptionProperty = AvaloniaProperty.Register<SettingRow, string>(nameof(Description), "");

        public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    }

    /// <summary>A titled group of settings or content: the one panel treatment used across all pages.</summary>
    internal sealed class Section : ContentControl
    {
        public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<Section, string>(nameof(Title), "");
        public static readonly StyledProperty<string> DescriptionProperty = AvaloniaProperty.Register<Section, string>(nameof(Description), "");
        public static readonly StyledProperty<object> ActionsProperty = AvaloniaProperty.Register<Section, object>(nameof(Actions));

        public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
        /// <summary>Optional buttons shown at the right of the header.</summary>
        public object Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    }
}
