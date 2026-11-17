using System;
using System.Collections.Generic;
using System.Linq;
using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    internal sealed class RecordingSink : IInputSink
    {
        public List<string> Log { get; } = new();
        public void OnKey(string name, bool isDown) => Log.Add((isDown ? "k+" : "k-") + name);
        public void OnMouseButton(string button, bool isDown) => Log.Add((isDown ? "m+" : "m-") + button);
        public void OnWheel(int direction) => Log.Add("w" + direction);
        public void OnMouseMove(int dx, int dy) => Log.Add($"mv{dx},{dy}");
    }

    public class ExtendedKeyTests
    {
        [Theory]
        [InlineData("Insert", 0x2D)]
        [InlineData("Delete", 0x2E)]
        [InlineData("End", 0x23)]
        [InlineData("PageUp", 0x21)]
        [InlineData("PageDown", 0x22)]
        [InlineData("CapsLock", 0x14)]
        [InlineData("Numpad0", 0x60)]
        [InlineData("Numpad9", 0x69)]
        [InlineData("NumpadAdd", 0x6B)]
        [InlineData("Minus", 0xBD)]
        [InlineData("Equals", 0xBB)]
        [InlineData("Semicolon", 0xBA)]
        [InlineData("Backtick", 0xC0)]
        [InlineData("Slash", 0xBF)]
        [InlineData("F13", 0x7C)]
        [InlineData("F24", 0x87)]
        public void NewKeysRoundTrip(string name, int vk)
        {
            Assert.Equal(name, KeyNames.FromVirtualKey(vk));
            Assert.Contains(vk, KeyNames.ToVirtualKeys(name));
            Assert.Equal(InputKind.Keyboard, InputChoices.KindOf(name));
            Assert.Equal(name, InputChoices.Canonical(name.ToLowerInvariant()));
        }

        [Fact]
        public void ExistingKeysAreUnchanged()
        {
            Assert.Equal("Space", KeyNames.FromVirtualKey(0x20));
            Assert.Equal("W", KeyNames.FromVirtualKey(0x57));
            Assert.Equal("F12", KeyNames.FromVirtualKey(0x7B));
            Assert.Equal("Shift", KeyNames.FromVirtualKey(0xA1));
            Assert.Equal(new[] { 0x10, 0xA0, 0xA1 }, KeyNames.ToVirtualKeys("Shift"));
            Assert.Equal("VK91", KeyNames.FromVirtualKey(0x91));          // Scroll Lock stays the block hotkey, never a binding name
        }

        [Fact]
        public void EveryOfferedKeyIsUniqueAndMapsToARealKey()
        {
            var keys = InputChoices.Keyboard;
            Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (string k in keys)
            {
                Assert.NotEmpty(KeyNames.ToVirtualKeys(k));
                Assert.False(string.IsNullOrWhiteSpace(InputChoices.DisplayName(k)));
            }
            Assert.True(keys.Length > 110);                               // the old list had 69
        }

        [Fact]
        public void FriendlyNames()
        {
            Assert.Equal("Page Up", InputChoices.DisplayName("PageUp"));
            Assert.Equal("Num 5", InputChoices.DisplayName("Numpad5"));
            Assert.Equal("Num +", InputChoices.DisplayName("NumpadAdd"));
        }
    }

    public class CaptureRulesTests
    {
        [Fact]
        public void AnyKeyMouseButtonOrWheelCanBeBoundToAButton()
        {
            Assert.True(InputCapture.TryAcceptForAction(ControllerEngine.ActionACross, CapturedKind.Key, "Delete", out string i, out _)); Assert.Equal("Delete", i);
            Assert.True(InputCapture.TryAcceptForAction(ControllerEngine.ActionACross, CapturedKind.MouseButton, "Mouse X2", out i, out _)); Assert.Equal("Mouse X2", i);
            Assert.True(InputCapture.TryAcceptForAction(ControllerEngine.ActionACross, CapturedKind.Wheel, "Wheel Down", out i, out _)); Assert.Equal("Wheel Down", i);
        }

        [Fact]
        public void LeftStickDirectionsTakeKeysOnly()
        {
            Assert.True(InputCapture.TryAcceptForAction(ControllerEngine.ActionLeftStickUp, CapturedKind.Key, "NumpadAdd", out _, out _));
            Assert.False(InputCapture.TryAcceptForAction(ControllerEngine.ActionLeftStickUp, CapturedKind.MouseButton, "Mouse Left", out _, out string err));
            Assert.Contains("keyboard", err);
            Assert.False(InputCapture.TryAcceptForAction(ControllerEngine.ActionLeftStickUp, CapturedKind.Wheel, "Wheel Up", out _, out _));
        }

        [Fact]
        public void UnsupportedKeysAreRefusedWithAMessage()
        {
            Assert.False(InputCapture.TryAcceptForAction(ControllerEngine.ActionACross, CapturedKind.Key, "VK91", out string input, out string error));
            Assert.Null(input);
            Assert.Contains("VK91", error);
        }

        [Fact]
        public void PaddlesTakeKeysAndMouseButtonsButNotTheWheel()
        {
            Assert.True(InputCapture.TryAcceptForPaddle(CapturedKind.Key, "PageDown", out PaddleTargetKind k, out string i, out _));
            Assert.Equal(PaddleTargetKind.Key, k); Assert.Equal("PageDown", i);
            Assert.True(InputCapture.TryAcceptForPaddle(CapturedKind.MouseButton, "Mouse X1", out k, out i, out _));
            Assert.Equal(PaddleTargetKind.MouseButton, k);
            Assert.False(InputCapture.TryAcceptForPaddle(CapturedKind.Wheel, "Wheel Up", out _, out _, out string err));
            Assert.Contains("wheel", err);
        }
    }

    public class CapturingSinkTests
    {
        [Fact]
        public void PassesEverythingThroughWhenNotCapturing()
        {
            var inner = new RecordingSink();
            var sink = new CapturingSink(inner);
            sink.OnKey("W", true); sink.OnMouseButton("Mouse Left", true); sink.OnWheel(1); sink.OnMouseMove(3, 4); sink.OnKey("W", false);
            Assert.Equal(new[] { "k+W", "m+Mouse Left", "w1", "mv3,4", "k-W" }, inner.Log);
            Assert.False(sink.IsCapturing);
        }

        [Fact]
        public void TakesExactlyTheNextPressAndHidesItFromTheEngine()
        {
            var inner = new RecordingSink();
            var sink = new CapturingSink(inner);
            var got = new List<CapturedInput>();
            sink.BeginCapture(got.Add);
            Assert.True(sink.IsCapturing);
            sink.OnKey("Delete", true);                 // captured
            sink.OnKey("Delete", false);                // the release still reaches the engine (nothing can stick)
            sink.OnKey("R", true);                      // a second press is normal input again
            Assert.Single(got);
            Assert.Equal(CapturedKind.Key, got[0].Kind);
            Assert.Equal("Delete", got[0].Name);
            Assert.Equal(new[] { "k-Delete", "k+R" }, inner.Log);
            Assert.False(sink.IsCapturing);
        }

        [Fact]
        public void CapturesMouseButtonsAndWheelNotches()
        {
            var inner = new RecordingSink();
            var sink = new CapturingSink(inner);
            CapturedInput seen = default;
            sink.BeginCapture(c => seen = c);
            sink.OnMouseButton("Mouse X1", true);
            Assert.Equal(CapturedKind.MouseButton, seen.Kind); Assert.Equal("Mouse X1", seen.Name);

            sink.BeginCapture(c => seen = c);
            sink.OnWheel(-1);
            Assert.Equal(CapturedKind.Wheel, seen.Kind); Assert.Equal("Wheel Down", seen.Name);
            Assert.Empty(inner.Log);
        }

        [Fact]
        public void ReleasesAndMovementAreNeverSwallowedAndCancelRestoresPassThrough()
        {
            var inner = new RecordingSink();
            var sink = new CapturingSink(inner);
            sink.BeginCapture(_ => { });
            sink.OnKey("A", false); sink.OnMouseButton("Mouse Left", false); sink.OnMouseMove(1, 2);
            sink.EndCapture();
            sink.OnKey("A", true);
            Assert.Equal(new[] { "k-A", "m-Mouse Left", "mv1,2", "k+A" }, inner.Log);
        }

        [Fact]
        public void AThrowingListenerCannotBreakInput()
        {
            var inner = new RecordingSink();
            var sink = new CapturingSink(inner);
            sink.BeginCapture(_ => throw new InvalidOperationException("boom"));
            sink.OnKey("A", true);                      // swallowed, no exception
            sink.OnKey("B", true);
            Assert.Equal(new[] { "k+B" }, inner.Log);
        }
    }
}
