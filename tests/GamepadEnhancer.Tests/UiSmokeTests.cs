using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GamepadEnhancer;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(GamepadEnhancer.Tests.TestAppBuilder))]

namespace GamepadEnhancer.Tests
{
    public class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
    }

    /// <summary>
    /// Creates the real application and main window without a display. It catches the errors a compiler cannot: a missing resource,
    /// a bad style or template, or a control that throws while it is being built or laid out.
    /// </summary>
    public class UiSmokeTests
    {
        [AvaloniaFact]
        public void WindowOpensAndEveryPageShows()
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            foreach (string key in MainWindow.PageKeys)
            {
                window.ShowPageForTests(key);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(key, window.CurrentPageKey);
            }
            window.Close();
        }

        [AvaloniaTheory]
        [InlineData(640, 520)]
        [InlineData(900, 640)]
        [InlineData(1280, 820)]
        public void EveryPageLaysOutAtEachWindowSize(int width, int height)
        {
            var window = new MainWindow { Width = width, Height = height };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (string key in MainWindow.PageKeys)
            {
                window.ShowPageForTests(key);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }
            window.Close();
        }
    }
}
