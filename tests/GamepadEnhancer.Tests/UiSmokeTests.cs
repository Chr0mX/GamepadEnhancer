using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Skia;
using System.IO;
using Avalonia.Threading;
using GamepadEnhancer;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(GamepadEnhancer.Tests.TestAppBuilder))]

namespace GamepadEnhancer.Tests
{
    public class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
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

        [AvaloniaFact]
        public void TheOneDiagramFollowsThePageAndTheLayoutSwitch()
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ShowPageForTests("controller");
            Assert.Equal(DiagramMode.Controls, window.DiagramForTests.Mode);

            window.ShowPageForTests("paddles");
            Assert.Equal(DiagramMode.Paddles, window.DiagramForTests.Mode);
            Assert.True(PaddleSlots.TryParseId(window.DiagramForTests.SelectedId, out _));   // a paddle is selected, so the editor is useful at once

            window.ShowPageForTests("controller");
            Assert.Equal(DiagramMode.Controls, window.DiagramForTests.Mode);

            window.SelectLayoutForTests("xbox360");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PadFamily.Xbox360, window.FamilyForTests);
            Assert.Equal(PadFamily.Xbox360, window.DiagramForTests.Family);
            window.SelectLayoutForTests("dualshock4");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PadFamily.DualShock4, window.DiagramForTests.Family);
            window.Close();
        }

        /// <summary>
        /// Renders each page to a PNG (set GE_SCREENSHOT_DIR; CI uploads the folder) so the real layout can be looked at.
        /// It never fails on pixels, only on exceptions.
        /// </summary>
        [AvaloniaFact]
        public void RenderEveryPageToAnImage()
        {
            string dir = System.Environment.GetEnvironmentVariable("GE_SCREENSHOT_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            foreach ((int w, int h) in new[] { (1280, 820), (900, 640) })
            {
                var window = new MainWindow { Width = w, Height = h };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                foreach (string key in MainWindow.PageKeys)
                {
                    window.ShowPageForTests(key);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(dir, $"{key}-{w}.png"));
                }
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(720, 520)]
        [InlineData(900, 640)]
        [InlineData(1280, 820)]
        [InlineData(1920, 1080)]
        public void NoPageNeedsHorizontalScrolling(int width, int height)
        {
            var window = new MainWindow { Width = width, Height = height };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var wide = new System.Collections.Generic.List<string>();
            foreach (string key in MainWindow.PageKeys)
            {
                window.ShowPageForTests(key);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var sv = window.CurrentPageScroller;
                if (sv != null && sv.Extent.Width > sv.Viewport.Width + 1.5)
                    wide.Add($"{key}: content {sv.Extent.Width:0} px wide, viewport {sv.Viewport.Width:0} px");
            }
            window.Close();
            Assert.True(wide.Count == 0, $"At {width}x{height} these pages overflow sideways: " + string.Join("; ", wide));
        }

        /// <summary>The bundled Inter and JetBrains Mono must really be the fonts in use, not a silent system fallback.</summary>
        [AvaloniaFact]
        public void BundledFontsResolveToInterAndJetBrainsMono()
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (var (key, expected) in new[] { ("UiFont", "Inter"), ("MonoFont", "JetBrains Mono") })
            {
                Assert.True(Application.Current.TryGetResource(key, Application.Current.ActualThemeVariant, out object value), key + " is missing");
                var family = Assert.IsType<FontFamily>(value);
                Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out IGlyphTypeface glyphs), key + " did not resolve to a font");
                Assert.True(glyphs.FamilyName.Contains(expected, System.StringComparison.OrdinalIgnoreCase), $"{key} resolved to \"{glyphs.FamilyName}\", expected {expected}");
            }
            window.Close();
        }

        [AvaloniaFact]
        public void ProfileMenuHoldsEveryProfileAction()
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var headers = window.ProfileMenuForTests();
            foreach (string wanted in new[] { "Save", "Save as…", "Duplicate…", "Rename…", "Delete", "Import…", "Export…", "Open profiles folder" })
                Assert.Contains(wanted, headers);
            Assert.Contains(headers, h => h.StartsWith("No profile loaded"));
            Assert.Contains("no profile", window.HeaderProfileTextForTests);
            window.Close();
        }

        [AvaloniaFact]
        public void LabelsAndPictureFollowTheSelectedLayout()
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.ShowPageForTests("controller");

            window.SelectLayoutForTests("dualshock4");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PadFamily.DualShock4, window.FamilyForTests);
            Assert.Equal(PadFamily.DualShock4, window.DiagramForTests.Family);
            Assert.Equal("Cross", window.BindingTitleForTests(Inputs.FaceSouth));
            Assert.Equal("Triangle", window.BindingTitleForTests(Inputs.FaceNorth));
            Assert.Equal("Cross", window.DiagramChipTitleForTests(Inputs.FaceSouth));

            window.SelectLayoutForTests("xbox360");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PadFamily.Xbox360, window.FamilyForTests);
            Assert.Equal(PadFamily.Xbox360, window.DiagramForTests.Family);
            Assert.Equal("A", window.BindingTitleForTests(Inputs.FaceSouth));
            Assert.Equal("Y", window.BindingTitleForTests(Inputs.FaceNorth));
            Assert.Equal("A", window.DiagramChipTitleForTests(Inputs.FaceSouth));
            window.Close();
        }

        [AvaloniaFact]
        public void RearPaddlesAreHiddenForPs4AndShownForXbox()
        {
            var window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.SelectLayoutForTests("dualshock4");
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.PaddleNavVisibleForTests);
            Assert.DoesNotContain(ControllerLayout.For(PadFamily.DualShock4), s => PaddleSlots.TryParseId(s.Id, out _));

            window.SelectLayoutForTests("xbox360");
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.PaddleNavVisibleForTests);
            Assert.Contains(ControllerLayout.For(PadFamily.Xbox360), s => PaddleSlots.TryParseId(s.Id, out _));

            // being on the paddle page when switching to PS4 leaves it
            window.ShowPageForTests("paddles");
            window.SelectLayoutForTests("dualshock4");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("dashboard", window.CurrentPageKey);
            window.Close();
        }
    }
}
