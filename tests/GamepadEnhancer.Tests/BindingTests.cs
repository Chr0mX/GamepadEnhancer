using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GamepadEnhancer;
using Xunit;

namespace GamepadEnhancer.Tests
{
    public class ConditionTests
    {
        private static BindingDef Def(BindingCondition c, bool toggle = false, string input = "Mouse Left") => new()
        {
            Id = "b", Input = input, Condition = c, Toggle = toggle,
            Outputs = new[] { new BindingOutput { Kind = BindingOutputKind.PadButton, Name = "FACE_SOUTH" } }
        };

        private static EvaluationResult Step(ConditionEvaluator e, BindingDef d, long now, params (bool Down, long T)[] edges) =>
            e.Update(d, now, edges.Select(x => new InputEdge(x.Down, x.T)).ToList());

        [Fact]
        public void HeldRunsWhileTheInputIsHeld()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Held);
            Assert.False(Step(e, d, 0).Running);
            Assert.True(Step(e, d, 10, (true, 10)).Running);
            Assert.True(Step(e, d, 20).Running);
            Assert.False(Step(e, d, 30, (false, 30)).Running);
        }

        [Fact]
        public void HeldSeesAPressShorterThanOneTick()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Held);
            Assert.True(Step(e, d, 10, (true, 2), (false, 6)).Running);   // pressed and released inside one update: still one running tick
            Assert.False(Step(e, d, 20).Running);
        }

        [Fact]
        public void UnheldRunsWhileTheInputIsNotHeld()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Unheld);
            Assert.True(Step(e, d, 0).Running);
            Assert.False(Step(e, d, 10, (true, 10)).Running);
            Assert.False(Step(e, d, 20).Running);
            Assert.True(Step(e, d, 30, (false, 30)).Running);
        }

        [Fact]
        public void PressRunsOnceAndNotAgainWhileHeld()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Press);
            Assert.True(Step(e, d, 10, (true, 10)).Running);
            Assert.True(Step(e, d, 40).Running);                     // inside the pulse
            Assert.False(Step(e, d, 10 + BindingDef.PulseMs + 5).Running);   // the pulse is over
            Assert.False(Step(e, d, 1000).Running);                  // still held: it does not fire again
            Assert.False(Step(e, d, 1010, (false, 1010)).Running);   // and releasing does nothing
        }

        [Fact]
        public void ReleaseRunsOnlyWhenTheInputIsReleased()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Release);
            Assert.False(Step(e, d, 10, (true, 10)).Running);
            Assert.False(Step(e, d, 500).Running);
            Assert.True(Step(e, d, 510, (false, 510)).Running);
            Assert.False(Step(e, d, 510 + BindingDef.PulseMs + 5).Running);
        }

        [Fact]
        public void TapNeedsAQuickPressAndRelease()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Tap);
            Step(e, d, 10, (true, 10));
            Assert.True(Step(e, d, 100, (false, 100)).Running);            // 90 ms: a tap
            Step(e, d, 1000);
            Step(e, d, 1010, (true, 1010));
            Assert.False(Step(e, d, 1500, (false, 1500)).Running);         // 490 ms: too slow
        }

        [Fact]
        public void DoubleNeedsTwoPressesInsideTheWindowAndFiresOnce()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Double);
            Assert.False(Step(e, d, 10, (true, 10)).Running);
            Assert.Equal(BindingState.WaitingDouble, Step(e, d, 60, (false, 60)).State);
            Assert.True(Step(e, d, 200, (true, 200)).Running);              // confirmed on the second press
            Step(e, d, 300, (false, 300));
            Assert.False(Step(e, d, 400, (true, 400)).Running);             // a third press starts a new pair, it does not fire again
        }

        [Fact]
        public void DoubleOutsideTheWindowDoesNotFire()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Double);
            Step(e, d, 10, (true, 10)); Step(e, d, 40, (false, 40));
            Assert.False(Step(e, d, 900, (true, 900)).Running);             // 890 ms later: too slow, it is a new first press
            Assert.Equal(BindingState.WaitingDouble, Step(e, d, 920).State);
        }

        [Fact]
        public void HoldFiresOnceAfterTheDuration()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Hold);
            Assert.Equal(BindingState.WaitingHold, Step(e, d, 10, (true, 10)).State);
            Assert.False(Step(e, d, 400).Running);
            Assert.True(Step(e, d, 520).Running);                           // 510 ms held
            Assert.False(Step(e, d, 520 + BindingDef.PulseMs + 5).Running);
            Assert.False(Step(e, d, 3000).Running);                         // still held: no repeat
        }

        [Fact]
        public void HoldReleasedEarlyDoesNothing()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Hold);
            Step(e, d, 10, (true, 10));
            Assert.False(Step(e, d, 300, (false, 300)).Running);
            Assert.False(Step(e, d, 900).Running);
        }

        [Fact]
        public void HoldReleasedAfterTheDurationWithoutAnUpdateInBetweenStillCounts()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Hold);
            Assert.True(Step(e, d, 700, (true, 10), (false, 650)).Running);
        }

        [Theory]
        [InlineData(BindingCondition.Press)]
        [InlineData(BindingCondition.Release)]
        [InlineData(BindingCondition.Double)]
        [InlineData(BindingCondition.Tap)]
        [InlineData(BindingCondition.Hold)]
        public void ToggleLatchesAndTheNextActivationStops(BindingCondition c)
        {
            var e = new ConditionEvaluator(); var d = Def(c, toggle: true);
            long t = 0;
            // first activation
            Activate(e, d, c, ref t);
            var r = Step(e, d, t + 700);          // long after the physical input is back to released
            Assert.True(r.Running); Assert.True(r.Latched); Assert.Equal(BindingState.ToggledActive, r.State);
            Assert.False(e.Down);
            // still running much later
            Assert.True(Step(e, d, t + 5000).Running);
            // second activation stops it
            t += 5000;
            Activate(e, d, c, ref t);
            Assert.False(Step(e, d, t + 700).Running);
        }

        private static void Activate(ConditionEvaluator e, BindingDef d, BindingCondition c, ref long t)
        {
            t += 1000;
            switch (c)
            {
                case BindingCondition.Press: Step(e, d, t, (true, t)); Step(e, d, t + 100, (false, t + 100)); t += 100; break;
                case BindingCondition.Release: Step(e, d, t, (true, t)); Step(e, d, t + 100, (false, t + 100)); t += 100; break;
                case BindingCondition.Tap: Step(e, d, t, (true, t)); Step(e, d, t + 50, (false, t + 50)); t += 50; break;
                case BindingCondition.Double:
                    Step(e, d, t, (true, t)); Step(e, d, t + 30, (false, t + 30));
                    Step(e, d, t + 120, (true, t + 120)); Step(e, d, t + 160, (false, t + 160)); t += 160; break;
                case BindingCondition.Hold:
                    Step(e, d, t, (true, t)); Step(e, d, t + 600, (false, t + 600)); t += 600; break;
            }
        }

        [Fact]
        public void OnePressTogglesExactlyOnceEvenIfHeldForAWhile()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Press, toggle: true);
            Assert.True(Step(e, d, 10, (true, 10)).Latched);
            for (long t = 20; t < 3000; t += 10) Assert.True(Step(e, d, t).Latched);   // held: it stays latched, it does not flip back
            Assert.True(Step(e, d, 3010, (false, 3010)).Latched);
        }

        [Fact]
        public void ToggleDoesNothingForHeldAndUnheld()
        {
            var held = Def(BindingCondition.Held, toggle: true);
            Assert.False(held.EffectiveToggle);
            var e = new ConditionEvaluator();
            Assert.True(Step(e, held, 10, (true, 10)).Running);
            Assert.False(Step(e, held, 20, (false, 20)).Running);       // normal Held behaviour: no latch
            Assert.False(Def(BindingCondition.Unheld, toggle: true).EffectiveToggle);
        }

        [Fact]
        public void DisablingAToggledBindingStopsIt()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Press, toggle: true);
            Assert.True(Step(e, d, 10, (true, 10)).Latched);
            Step(e, d, 20, (false, 20));
            var r = Step(e, d.With(enabled: false), 30);
            Assert.False(r.Running); Assert.Equal(BindingState.Disabled, r.State);
            Assert.False(Step(e, d, 40).Running);                         // re-enabled: not latched any more
        }

        [Fact]
        public void AnalogBindingsAreAlwaysRunningWhileEnabled()
        {
            var e = new ConditionEvaluator();
            var d = new BindingDef { Id = "a", Input = BindingInputs.MouseMovement, Condition = BindingCondition.Press };
            Assert.True(d.IsAnalog);
            Assert.True(Step(e, d, 10).Running);
            Assert.True(Step(e, d, 20, (true, 20), (false, 21)).Running);   // never put through a digital condition
            Assert.False(Step(e, d.With(enabled: false), 30).Running);
        }

        [Fact]
        public void APressTheEvaluatorNeverSawIsIgnoredOnRelease()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Release);
            Assert.False(Step(e, d, 10, (false, 10)).Running);
        }

        [Fact]
        public void SeededLevelIsNotAPress()
        {
            var e = new ConditionEvaluator(); var d = Def(BindingCondition.Press);
            e.SeedLevel(true);
            Assert.False(Step(e, d, 10).Running);
            Assert.True(e.Down);
        }
    }

    public class OutputOwnershipTests
    {
        [Fact]
        public void AnOutputStaysDownUntilItsLastOwnerLetsGo()
        {
            var o = new OutputOwnership();
            Assert.True(o.Claim("a", "Key:W"));
            Assert.False(o.Claim("b", "Key:W"));          // already down
            Assert.False(o.Release("a", "Key:W"));        // b still holds it
            Assert.True(o.IsHeld("Key:W"));
            Assert.True(o.Release("b", "Key:W"));
            Assert.False(o.IsHeld("Key:W"));
        }

        [Fact]
        public void TriggerLevelIsTheLargestMagnitude()
        {
            var o = new OutputOwnership();
            o.Claim("a", "PadButton:RT", 40);
            Assert.Equal(40, o.Level("PadButton:RT"));
            Assert.True(o.Claim("b", "PadButton:RT", 90));
            Assert.Equal(90, o.Level("PadButton:RT"));
            Assert.True(o.Release("b", "PadButton:RT"));
            Assert.Equal(40, o.Level("PadButton:RT"));
        }

        [Fact]
        public void ReleaseAllOnlyTouchesTheOwner()
        {
            var o = new OutputOwnership();
            o.Claim("a", "Key:A"); o.Claim("a", "Key:B"); o.Claim("b", "Key:B");
            var changed = o.ReleaseAll("a");
            Assert.Equal(new[] { "Key:A" }, changed);
            Assert.True(o.IsHeld("Key:B"));
            Assert.Equal(new[] { "Key:B" }, o.ReleaseEverything());
            Assert.Empty(o.HeldOutputs);
        }
    }

    public class BindingRuntimeTests
    {
        private static BindingOutput Pad(string n, int mag = 100) => new() { Kind = BindingOutputKind.PadButton, Name = n, Magnitude = mag };
        private static BindingOutput Key(string n) => new() { Kind = BindingOutputKind.Key, Name = n };
        private static BindingDef Def(string id, string input, BindingCondition c, bool toggle, params BindingOutput[] outs) =>
            new() { Id = id, Input = input, Condition = c, Toggle = toggle, Outputs = outs };
        private static int Bit(string action) => 1 << Array.IndexOf(ControllerEngine.BindingActions, action);

        [Fact]
        public void HeldBindingHoldsItsPadButtonOnlyWhileTheInputIsDown()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "Mouse Left", BindingCondition.Held, false, Pad("FACE_SOUTH")) });
            r.OnEdge("Mouse Left", true, 10); r.Update(10);
            Assert.Equal(Bit("FACE_SOUTH"), r.PadMask);
            Assert.True(r.Snapshot()[0].Running);
            r.OnEdge("Mouse Left", false, 30); r.Update(30);
            Assert.Equal(0, r.PadMask);
            Assert.False(r.Snapshot()[0].Running);
        }

        [Fact]
        public void EnabledIsNotRunning()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "Space", BindingCondition.Press, false, Pad("FACE_SOUTH")),
                                  Def("b", "Q", BindingCondition.Held, false, Pad("LB")).With(enabled: false) });
            r.Update(10);
            var s = r.Snapshot();
            Assert.Equal(BindingState.Waiting, s[0].State); Assert.False(s[0].Running);
            Assert.Equal(BindingState.Disabled, s[1].State); Assert.False(s[1].Running);
        }

        [Fact]
        public void ToggledBindingKeepsRunningAfterTheInputIsReleasedAndStopsOnTheNextPress()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "F", BindingCondition.Press, true, Pad("RB")) });
            r.OnEdge("F", true, 10); r.OnEdge("F", false, 90); r.Update(100);
            Assert.Equal(Bit("RB"), r.PadMask);
            var s = r.Snapshot()[0];
            Assert.True(s.Running); Assert.True(s.Latched); Assert.False(s.PhysicalDown); Assert.Equal(BindingState.ToggledActive, s.State);
            r.Update(5000);
            Assert.Equal(Bit("RB"), r.PadMask);
            r.OnEdge("F", true, 5100); r.OnEdge("F", false, 5150); r.Update(5200);
            Assert.Equal(0, r.PadMask);
        }

        [Fact]
        public void TwoBindingsShareAnOutputWithoutReleasingEachOther()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "Q", BindingCondition.Held, false, Pad("FACE_SOUTH")),
                                  Def("b", "E", BindingCondition.Held, false, Pad("FACE_SOUTH")) });
            r.OnEdge("Q", true, 10); r.OnEdge("E", true, 12); r.Update(20);
            r.OnEdge("Q", false, 30); r.Update(40);
            Assert.Equal(Bit("FACE_SOUTH"), r.PadMask);          // b still holds it
            r.OnEdge("E", false, 50); r.Update(60);
            Assert.Equal(0, r.PadMask);
        }

        [Fact]
        public void KeyOutputsAreInjectedAndReleased()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "Mouse Right", BindingCondition.Held, false, Key("Shift")) });
            r.OnEdge("Mouse Right", true, 10); r.Update(10);
            Assert.Contains(r.PendingForTests(), p => p.Name == "Shift" && p.Down);
            Assert.True(r.IsInjected("Shift"));
            r.OnEdge("Mouse Right", false, 30); r.Update(30);
            Assert.Contains(r.PendingForTests(), p => p.Name == "Shift" && !p.Down);
        }

        [Fact]
        public void TriggerOutputCarriesItsMagnitude()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "Mouse Left", BindingCondition.Held, false, Pad("RT", 50)) });
            r.OnEdge("Mouse Left", true, 10); r.Update(10);
            Assert.InRange(r.TriggerLevel(false), 126, 129);
            Assert.Equal(0, r.TriggerLevel(true));
            Assert.NotEqual(0, r.PadMask & Bit("RT"));
        }

        [Fact]
        public void ReleaseAllStopsEverythingIncludingLatchedBindings()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "F", BindingCondition.Press, true, Pad("RB"), Key("Ctrl")),
                                  Def("b", "G", BindingCondition.Held, false, Pad("LB")) });
            r.OnEdge("F", true, 10); r.OnEdge("F", false, 20); r.OnEdge("G", true, 30); r.Update(40);
            Assert.NotEqual(0, r.PadMask);
            r.ReleaseAll();                                          // device disconnected / shutdown
            Assert.Equal(0, r.PadMask);
            Assert.All(r.Snapshot(), s => Assert.False(s.Running));
            Assert.Contains(r.PendingForTests(), p => p.Name == "Ctrl" && !p.Down);
            r.Update(50);
            Assert.Equal(0, r.PadMask);                              // nothing comes back by itself
        }

        [Fact]
        public void EditingAnotherBindingKeepsALatchButEditingTheLatchedOneResetsIt()
        {
            var a = Def("a", "F", BindingCondition.Press, true, Pad("RB"));
            var b = Def("b", "G", BindingCondition.Held, false, Pad("LB"));
            var r = new BindingRuntime();
            r.SetBindings(new[] { a, b });
            r.OnEdge("F", true, 10); r.OnEdge("F", false, 20); r.Update(30);
            Assert.NotEqual(0, r.PadMask & Bit("RB"));
            r.SetBindings(new[] { a, b.With(outputs: new[] { Pad("LT") }) });     // an unrelated edit
            r.Update(40);
            Assert.NotEqual(0, r.PadMask & Bit("RB"));
            r.SetBindings(new[] { a.With(condition: BindingCondition.Tap), b });   // the latched binding itself changes
            r.Update(50);
            Assert.Equal(0, r.PadMask & Bit("RB"));
            Assert.Equal(BindingState.Waiting, r.Snapshot()[0].State);
        }

        [Fact]
        public void RemovingARunningBindingReleasesItsOutputs()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "Space", BindingCondition.Held, false, Pad("FACE_SOUTH"), Key("X")) });
            r.OnEdge("Space", true, 10); r.Update(20);
            r.SetBindings(Array.Empty<BindingDef>());
            Assert.Equal(0, r.PadMask);
            Assert.Contains(r.PendingForTests(), p => p.Name == "X" && !p.Down);
        }

        [Fact]
        public void RepeatedStatesAndStrayReleasesAreIgnored()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "Space", BindingCondition.Release, false, Pad("FACE_SOUTH")) });
            r.OnEdge("Space", false, 5);                              // a release with no press
            r.OnEdge("Space", true, 10); r.OnEdge("Space", true, 11); // a repeated down
            r.Update(20);
            Assert.Equal(0, r.PadMask);
            r.OnEdge("Space", false, 30); r.Update(30);
            Assert.NotEqual(0, r.PadMask);                            // exactly one release counted
        }

        [Fact]
        public void ABindingCreatedWhileTheKeyIsHeldDoesNotCountItAsAPress()
        {
            var r = new BindingRuntime();
            r.OnEdge("Space", true, 1); r.Update(2);
            r.SetBindings(new[] { Def("a", "Space", BindingCondition.Press, false, Pad("FACE_SOUTH")) });
            r.Update(10);
            Assert.Equal(0, r.PadMask);
        }

        [Fact]
        public void ManualBindingsSuppressTheAutomaticMappingOfTheirInput()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "Mouse Left", BindingCondition.Press, false, Pad("RT")),
                                  Def("b", "Q", BindingCondition.Held, false, Pad("LB")).With(enabled: false) });
            Assert.True(r.Suppresses("Mouse Left"));
            Assert.False(r.Suppresses("Q"));                           // a disabled binding does not take the input away
            Assert.False(r.Suppresses("W"));
        }

        [Fact]
        public void AnalogBindingsReportRunningAndHoldNoOutputs()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { new BindingDef { Id = "m", Input = BindingInputs.MouseMovement, Outputs = new[] { Pad("RT") } } });
            r.Update(10);
            Assert.True(r.Snapshot()[0].Running);
            Assert.Equal(0, r.PadMask);
        }
    }

    public class BindingProfileTests
    {
        [Fact]
        public void BindingsRoundTripThroughAProfileFile()
        {
            var defs = new[]
            {
                new BindingDef { Id = "x", Input = "Mouse Left", Condition = BindingCondition.Hold, Toggle = true, HoldMs = 700,
                    Outputs = new[] { new BindingOutput { Kind = BindingOutputKind.PadButton, Name = "RT", Magnitude = 60 },
                                      new BindingOutput { Kind = BindingOutputKind.Key, Name = "Shift" } } }
            };
            var p = new InputProfile { AdvancedBindings = BindingEntries.Build(defs) };
            string json = JsonSerializer.Serialize(p);
            var back = JsonSerializer.Deserialize<InputProfile>(json);
            var read = BindingEntries.Resolve(back.AdvancedBindings);
            Assert.True(BindingEntries.Equivalent(defs, read));
            Assert.Equal("x", read[0].Id);
            Assert.Equal(700, read[0].HoldMs);
        }

        [Fact]
        public void ProfilesWithoutBindingsAreWrittenWithoutTheBlock()
        {
            Assert.Null(BindingEntries.Build(Array.Empty<BindingDef>()));
            string json = JsonSerializer.Serialize(new InputProfile());
            Assert.DoesNotContain("AdvancedBindings", json);
            Assert.Null(JsonSerializer.Deserialize<InputProfile>("{\"Name\":\"old\"}").AdvancedBindings);
        }

        [Fact]
        public void InvalidEntriesAreCleanedAndReported()
        {
            var entries = new List<BindingEntry>
            {
                new() { Input = "NotAKey", Condition = "Press" },
                new() { Input = "Space", Condition = "Sideways", Toggle = true, DoubleWindowMs = 5, TapMaxMs = 99999, HoldMs = 1,
                        Outputs = new List<BindingOutputEntry> {
                            new() { Kind = "Key", Name = "Space" },            // presses its own input
                            new() { Kind = "Key", Name = "Nonsense" },
                            new() { Kind = "PadButton", Name = "RT", Magnitude = 500 } } }
            };
            var warnings = new List<string>();
            var list = BindingEntries.Resolve(entries, warnings);
            Assert.Single(list);
            Assert.Equal(BindingCondition.Held, list[0].Condition);
            Assert.Equal(BindingEntries.MinDoubleMs, list[0].DoubleWindowMs);
            Assert.Equal(BindingEntries.MaxTapMs, list[0].TapMaxMs);
            Assert.Equal(BindingEntries.MinHoldMs, list[0].HoldMs);
            Assert.Single(list[0].Outputs);
            Assert.Equal(100, list[0].Outputs[0].Magnitude);
            Assert.True(warnings.Count >= 4);
        }

        [Fact]
        public void ImportKeepsValidBindingsAndEquivalenceSeesThem()
        {
            const string json = "{\"Schema\":2,\"AdvancedBindings\":[{\"Input\":\"Q\",\"Condition\":\"Tap\",\"Outputs\":[{\"Kind\":\"PadButton\",\"Name\":\"LB\"}]}]}";
            InputProfile p = ProfileImport.Parse(json, out string error, out var warnings);
            Assert.Null(error);
            Assert.Single(p.AdvancedBindings);
            InputProfile plain = ProfileImport.Parse("{\"Schema\":2}", out _, out _);
            Assert.False(GameProfiles.Equivalent(p, plain));
            Assert.True(GameProfiles.Equivalent(plain, ProfileImport.Parse("{\"Schema\":2}", out _, out _)));
        }

        [Fact]
        public void ConditionListMatchesTheSpecification()
        {
            Assert.Equal(7, BindingConditions.All.Length);
            Assert.All(new[] { BindingCondition.Held, BindingCondition.Unheld }, c => Assert.True(BindingConditions.IsRepeating(c)));
            Assert.All(new[] { BindingCondition.Press, BindingCondition.Release, BindingCondition.Double, BindingCondition.Tap, BindingCondition.Hold },
                       c => Assert.True(BindingConditions.IsRunOnce(c)));
        }

        [Fact]
        public void CompatibilityWarningNamesAnOutputTheControllerLacks()
        {
            var def = new BindingDef { Input = "Q", Outputs = new[] { new BindingOutput { Kind = BindingOutputKind.PadButton, Name = "TOUCHPAD" } } };
            Assert.NotNull(BindingEntries.CompatibilityWarning(def, ControllerLayouts.Get("xbox360")));
            Assert.Null(BindingEntries.CompatibilityWarning(def, ControllerLayouts.Get("dualshock4")));
        }
    }

    public class TurboAndLayerTests
    {
        private static BindingOutput Pad(string n) => new() { Kind = BindingOutputKind.PadButton, Name = n };
        private static BindingOutput Key(string n) => new() { Kind = BindingOutputKind.Key, Name = n };
        private static BindingOutput Layer(int n) => new() { Kind = BindingOutputKind.Layer, Name = n.ToString() };
        private static int Bit(string action) => 1 << Array.IndexOf(ControllerEngine.BindingActions, action);
        private static BindingDef Def(string id, string input, BindingCondition c, int layer, bool toggle = false, int turbo = 0, BindingOutput outs = null) =>
            new() { Id = id, Input = input, Condition = c, Layer = layer, Toggle = toggle, TurboHz = turbo,
                    Outputs = outs == null ? Array.Empty<BindingOutput>() : new[] { outs } };
        private static void Down(BindingRuntime r, string input, long t) { r.OnEdge(input, true, t); }
        private static void Up(BindingRuntime r, string input, long t) { r.OnEdge(input, false, t); }

        // ── turbo ───────────────────────────────────────────────────────────────

        [Fact]
        public void TurboPulsesTheOutputWhileHeldAndKeepsTheBindingRunning()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("a", "Mouse Left", BindingCondition.Held, 0, turbo: 10, outs: Pad("FACE_SOUTH")) });   // 10 per second: 100 ms cycle
            Down(r, "Mouse Left", 0); r.Update(0);
            Assert.Equal(Bit("FACE_SOUTH"), r.PadMask);                    // on phase (0-50 ms)
            r.Update(60);
            Assert.Equal(0, r.PadMask);                                     // off phase (50-100 ms) ...
            Assert.True(r.Snapshot()[0].Running);                           // ... but the binding is still running
            r.Update(110);
            Assert.Equal(Bit("FACE_SOUTH"), r.PadMask);                    // next cycle
            Up(r, "Mouse Left", 120); r.Update(120);
            Assert.Equal(0, r.PadMask);
            Assert.False(r.Snapshot()[0].Running);
        }

        [Fact]
        public void TurboGoesAwayWhenSwitchedOff()
        {
            var a = Def("a", "Q", BindingCondition.Held, 0, turbo: 10, outs: Pad("LB"));
            var r = new BindingRuntime();
            r.SetBindings(new[] { a });
            Down(r, "Q", 0); r.Update(0); r.Update(60);
            Assert.Equal(0, r.PadMask);
            r.SetBindings(new[] { a.With(turboHz: 0) });                   // turbo off: the binding restarts solid
            r.Update(70);
            Assert.Equal(Bit("LB"), r.PadMask);
        }

        [Fact]
        public void TurboIsClampedOnLoad()
        {
            var list = BindingEntries.Resolve(new[] { new BindingEntry { Input = "Q", TurboHz = 500 }, new BindingEntry { Input = "W", TurboHz = -3 } });
            Assert.Equal(BindingDef.MaxTurboHz, list[0].TurboHz);
            Assert.Equal(0, list[1].TurboHz);
        }

        // ── layers ──────────────────────────────────────────────────────────────

        [Fact]
        public void AHeldShiftSwitchesTheLayerOnAndReleasingItRevertsToBase()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[]
            {
                Def("shift", "4", BindingCondition.Held, 0, outs: Layer(2)),
                Def("base", "Space", BindingCondition.Held, 0, outs: Pad("FACE_SOUTH")),
                Def("l2", "Space", BindingCondition.Held, 2, outs: Pad("FACE_EAST")),
            });
            Down(r, "Space", 5); r.Update(10);
            Assert.Equal(Bit("FACE_SOUTH"), r.PadMask);                    // base layer
            Up(r, "Space", 15); Down(r, "4", 16); r.Update(20);
            Assert.Equal(2, r.ActiveLayer);
            Down(r, "Space", 25); r.Update(30);
            Assert.Equal(Bit("FACE_EAST"), r.PadMask);                      // layer 2 replaces the base binding of Space
            Up(r, "4", 35); r.Update(40);
            Assert.Equal(0, r.ActiveLayer);
            Assert.Equal(Bit("FACE_SOUTH"), r.PadMask);                    // back to the base layer: the held Space is the base binding again
        }

        [Fact]
        public void ALayerBindingDoesNothingWhileItsLayerIsOff()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("l2", "Space", BindingCondition.Held, 2, outs: Pad("FACE_EAST")) });
            Down(r, "Space", 5); r.Update(10);
            Assert.Equal(0, r.PadMask);
            Assert.Equal(BindingState.OtherLayer, r.Snapshot()[0].State);
        }

        [Fact]
        public void TabStyleToggleSwitchesALayerOnAndOff()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[]
            {
                Def("tab", "Tab", BindingCondition.Press, 0, toggle: true, outs: Layer(1)),
                Def("l1", "Q", BindingCondition.Held, 1, outs: Pad("LB")),
            });
            Down(r, "Tab", 5); Up(r, "Tab", 25); r.Update(30);
            Assert.Equal(1, r.ActiveLayer);                                 // stays on after Tab is released
            Down(r, "Q", 40); r.Update(50);
            Assert.Equal(Bit("LB"), r.PadMask);
            Up(r, "Q", 60); Down(r, "Tab", 100); Up(r, "Tab", 120); r.Update(130);
            Assert.Equal(0, r.ActiveLayer);                                 // the next Tab press reverts to base
            Down(r, "Q", 140); r.Update(150);
            Assert.Equal(0, r.PadMask);
        }

        [Fact]
        public void TheHighestActiveLayerWinsAndReleasingItFallsBackToTheLowerOne()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[]
            {
                Def("tab", "Tab", BindingCondition.Press, 0, toggle: true, outs: Layer(1)),
                Def("four", "4", BindingCondition.Held, 0, outs: Layer(2)),
                Def("five", "5", BindingCondition.Held, 0, outs: Layer(2)),
            });
            Down(r, "Tab", 1); Up(r, "Tab", 5); r.Update(10);
            Assert.Equal(1, r.ActiveLayer);
            Down(r, "4", 20); r.Update(30);
            Assert.Equal(2, r.ActiveLayer);
            Down(r, "5", 35); Up(r, "4", 40); r.Update(50);
            Assert.Equal(2, r.ActiveLayer);                                 // 5 still holds layer 2
            Up(r, "5", 60); r.Update(70);
            Assert.Equal(1, r.ActiveLayer);                                 // Tab's latch is still there
        }

        [Fact]
        public void AnUnmapBindingSilencesTheAutomaticMappingOnlyOnItsLayer()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[]
            {
                Def("shift", "4", BindingCondition.Held, 0, outs: Layer(2)),
                Def("unmapW", "W", BindingCondition.Held, 2),                // no outputs: W does nothing on layer 2
            });
            Assert.False(r.Suppresses("W"));                                 // base layer: the automatic W mapping works
            Down(r, "4", 5); r.Update(10);
            Assert.True(r.Suppresses("W"));                                  // layer 2: un-mapped
            Up(r, "4", 20); r.Update(30);
            Assert.False(r.Suppresses("W"));
        }

        [Fact]
        public void ABaseUnmapSilencesTheInputEverywhere()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("unmap", "W", BindingCondition.Held, 0) });
            Assert.True(r.Suppresses("W"));
        }

        [Fact]
        public void ALatchedBindingDoesNotSurviveItsLayerEnding()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[]
            {
                Def("shift", "4", BindingCondition.Held, 0, outs: Layer(2)),
                Def("latch", "F", BindingCondition.Press, 2, toggle: true, outs: Pad("RB")),
            });
            Down(r, "4", 1); r.Update(5);
            Down(r, "F", 10); Up(r, "F", 20); r.Update(30);
            Assert.Equal(Bit("RB"), r.PadMask);
            Up(r, "4", 40); r.Update(50);
            Assert.Equal(0, r.PadMask);                                      // layer gone: released
            Down(r, "4", 60); r.Update(70);
            Assert.Equal(0, r.PadMask);                                      // and not latched any more when the layer returns
        }

        [Fact]
        public void LayerAndInputKeysSharingANumberAreNotConfused()
        {
            // the keyboard key "2" and "Layer 2" are different things
            var list = BindingEntries.Resolve(new[]
            {
                new BindingEntry { Input = "2", Outputs = new List<BindingOutputEntry> { new() { Kind = "Layer", Name = "2" } } }
            });
            Assert.Single(list);
            Assert.Single(list[0].Outputs);
            Assert.Equal(2, list[0].Outputs[0].LayerNumber);
        }

        [Fact]
        public void OnlyBaseLayerBindingsCanSwitchALayer()
        {
            var warnings = new List<string>();
            var list = BindingEntries.Resolve(new[]
            {
                new BindingEntry { Input = "Q", Layer = 2, Outputs = new List<BindingOutputEntry> { new() { Kind = "Layer", Name = "1" } } },
                new BindingEntry { Input = "E", Layer = 9, Outputs = new List<BindingOutputEntry> { new() { Kind = "Layer", Name = "7" } } }
            }, warnings);
            Assert.Equal(0, list[0].Layer);
            Assert.Equal(1, list[0].ActivatesLayer);
            Assert.Empty(list[1].Outputs);                                   // layer 7 does not exist
            Assert.NotEmpty(warnings);
        }

        [Fact]
        public void DisconnectDropsTheActiveLayer()
        {
            var r = new BindingRuntime();
            r.SetBindings(new[] { Def("shift", "4", BindingCondition.Held, 0, outs: Layer(2)) });
            Down(r, "4", 1); r.Update(5);
            Assert.Equal(2, r.ActiveLayer);
            r.ReleaseAll();
            Assert.Equal(0, r.ActiveLayer);
        }

        [Fact]
        public void TheDescribedControllerSetupWorksAsBindings()
        {
            // Tab toggles layer 1, 4 or 5 shifts to layer 2, W/A/S/D are un-mapped on layer 2, Mouse Left on layer 2 pulls the right trigger with turbo
            var r = new BindingRuntime();
            r.SetBindings(new[]
            {
                Def("tab", "Tab", BindingCondition.Press, 0, toggle: true, outs: Layer(1)),
                Def("k4", "4", BindingCondition.Held, 0, outs: Layer(2)),
                Def("k5", "5", BindingCondition.Held, 0, outs: Layer(2)),
                Def("w", "W", BindingCondition.Held, 2), Def("a", "A", BindingCondition.Held, 2),
                Def("s", "S", BindingCondition.Held, 2), Def("d", "D", BindingCondition.Held, 2),
                Def("fire", "Mouse Left", BindingCondition.Held, 2, turbo: 10, outs: Pad("RT")),
            });
            Down(r, "5", 1); r.Update(5);
            Assert.True(r.Suppresses("W") && r.Suppresses("A") && r.Suppresses("S") && r.Suppresses("D"));
            Down(r, "Mouse Left", 10); r.Update(10);
            Assert.NotEqual(0, r.PadMask & Bit("RT"));
            r.Update(70);
            Assert.Equal(0, r.PadMask & Bit("RT"));                          // turbo off phase
            Up(r, "5", 80); r.Update(90);
            Assert.False(r.Suppresses("W"));
            Assert.Equal(0, r.PadMask);
        }
    }
}
