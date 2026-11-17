using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GamepadEnhancer
{
    internal static class Dialogs
    {
        /// <summary>A Yes / No question; returns true for Yes.</summary>
        public static async Task<bool> ConfirmAsync(Window owner, string title, string message)
        {
            bool result = false;
            var yes = new Button { Content = "Yes", MinWidth = 80 };
            var no = new Button { Content = "No", MinWidth = 80 };
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(18),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } }
                    }
                }
            };
            yes.Click += (_, _) => { result = true; dialog.Close(); };
            no.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(owner);
            return result;
        }

        public static async Task ShowMessageAsync(Window owner, string title, string message)
        {
            var ok = new Button { Content = "OK", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right };
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(18),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        ok
                    }
                }
            };
            ok.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(owner);
        }
    }
}
