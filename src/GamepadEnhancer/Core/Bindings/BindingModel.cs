using System;
using System.Collections.Generic;
using System.Linq;

namespace GamepadEnhancer
{
    /// <summary>When a digital binding runs. <see cref="Held"/> and <see cref="Unheld"/> repeat; the rest run once per qualifying event.</summary>
    public enum BindingCondition { Held, Unheld, Press, Release, Double, Tap, Hold }

    public enum BindingOutputKind { PadButton, Key, MouseButton }

    /// <summary>What a binding is doing right now. Disabled / Waiting say nothing is running; Running and ToggledActive mean its outputs are held.</summary>
    public enum BindingState { Disabled, Waiting, Running, ToggledActive, WaitingHold, WaitingDouble }

    /// <summary>One thing a binding presses: a virtual pad button, a keyboard key or a mouse button. Triggers have a magnitude (percent).</summary>
    public sealed class BindingOutput
    {
        public BindingOutputKind Kind { get; init; }
        public string Name { get; init; } = "None";
        /// <summary>1..100. Only the pad triggers (LT / RT) use it; everything else is on or off.</summary>
        public int Magnitude { get; init; } = 100;

        /// <summary>The identity used for output ownership: two bindings pressing the same thing share one key.</summary>
        public string Key => Kind + ":" + Name;
        public bool IsTrigger => Kind == BindingOutputKind.PadButton
            && (string.Equals(Name, Inputs.Lt, StringComparison.OrdinalIgnoreCase) || string.Equals(Name, Inputs.Rt, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// One binding: a physical input, how it qualifies (the condition), whether it latches (toggle) and what it presses. Immutable.
    /// </summary>
    public sealed class BindingDef
    {
        public const int DefaultDoubleWindowMs = 300, DefaultTapMaxMs = 200, DefaultHoldMs = 500;
        /// <summary>How long a run-once binding's outputs stay down, so the game can see a press that would otherwise last under one tick.</summary>
        public const int PulseMs = 60;

        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        /// <summary>A keyboard key, "Mouse Left" ... "Mouse X2", "Wheel Up" / "Wheel Down", or an analog input (see <see cref="BindingInputs"/>).</summary>
        public string Input { get; init; } = "None";
        public bool Enabled { get; init; } = true;
        public BindingCondition Condition { get; init; } = BindingCondition.Held;
        public bool Toggle { get; init; }
        public int DoubleWindowMs { get; init; } = DefaultDoubleWindowMs;
        public int TapMaxMs { get; init; } = DefaultTapMaxMs;
        public int HoldMs { get; init; } = DefaultHoldMs;
        public IReadOnlyList<BindingOutput> Outputs { get; init; } = Array.Empty<BindingOutput>();

        public bool IsAnalog => BindingInputs.IsAnalog(Input);

        /// <summary>Toggle only means something for the run-once conditions (it turns them into a latch); Held and Unheld ignore it.</summary>
        public bool EffectiveToggle => Toggle && BindingConditions.IsRunOnce(Condition) && !IsAnalog;

        /// <summary>True when swapping this definition for <paramref name="other"/> must not disturb a running binding.</summary>
        public bool SameBehaviour(BindingDef other)
        {
            if (other == null || Input != other.Input || Condition != other.Condition || EffectiveToggle != other.EffectiveToggle
                || Enabled != other.Enabled || DoubleWindowMs != other.DoubleWindowMs || TapMaxMs != other.TapMaxMs || HoldMs != other.HoldMs
                || Outputs.Count != other.Outputs.Count)
                return false;
            for (int i = 0; i < Outputs.Count; i++)
                if (Outputs[i].Key != other.Outputs[i].Key || Outputs[i].Magnitude != other.Outputs[i].Magnitude) return false;
            return true;
        }

        public BindingDef With(string input = null, BindingCondition? condition = null, bool? toggle = null, bool? enabled = null,
            IReadOnlyList<BindingOutput> outputs = null, int? doubleWindowMs = null, int? tapMaxMs = null, int? holdMs = null) => new()
        {
            Id = Id,
            Input = input ?? Input,
            Condition = condition ?? Condition,
            Toggle = toggle ?? Toggle,
            Enabled = enabled ?? Enabled,
            Outputs = outputs ?? Outputs,
            DoubleWindowMs = doubleWindowMs ?? DoubleWindowMs,
            TapMaxMs = tapMaxMs ?? TapMaxMs,
            HoldMs = holdMs ?? HoldMs
        };

        /// <summary>A copy with a new id (copy / paste).</summary>
        public BindingDef Clone() => new()
        {
            Input = Input, Condition = Condition, Toggle = Toggle, Enabled = Enabled, Outputs = Outputs,
            DoubleWindowMs = DoubleWindowMs, TapMaxMs = TapMaxMs, HoldMs = HoldMs
        };
    }

    public static class BindingConditions
    {
        public static readonly BindingCondition[] All =
            { BindingCondition.Held, BindingCondition.Unheld, BindingCondition.Press, BindingCondition.Release,
              BindingCondition.Double, BindingCondition.Tap, BindingCondition.Hold };

        public static bool IsRepeating(BindingCondition c) => c == BindingCondition.Held || c == BindingCondition.Unheld;
        public static bool IsRunOnce(BindingCondition c) => !IsRepeating(c);

        public static string Title(BindingCondition c) => c.ToString();

        public static string Description(BindingCondition c) => c switch
        {
            BindingCondition.Held => "Repeats: runs while the input is held down.",
            BindingCondition.Unheld => "Repeats: runs while the input is not held down.",
            BindingCondition.Press => "Run once: when the input is pressed.",
            BindingCondition.Release => "Run once: when the input is released.",
            BindingCondition.Double => "Run once: when the input is double pressed.",
            BindingCondition.Tap => "Run once: when the input is pressed and released quickly.",
            _ => "Run once: when the input has been held for the hold time."
        };

        public static bool TryParse(string text, out BindingCondition condition) =>
            Enum.TryParse(text?.Trim(), true, out condition) && Enum.IsDefined(typeof(BindingCondition), condition);
    }

    /// <summary>Which inputs a binding can use, and which of them are analog (always running, never put through a condition).</summary>
    public static class BindingInputs
    {
        /// <summary>Mouse movement. Analog: it has a continuous value, so it has no pressed / released states.</summary>
        public const string MouseMovement = "Mouse Movement";
        public const string RemoteRightStick = "Remote Right Stick";

        public static readonly string[] Analog = { MouseMovement, RemoteRightStick };

        public static bool IsAnalog(string input) =>
            input != null && Analog.Any(a => string.Equals(a, input, StringComparison.OrdinalIgnoreCase));

        /// <summary>The canonical spelling of a digital or analog input, or null when it is not one a binding can use.</summary>
        public static string Canonical(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string t = input.Trim();
            string a = Analog.FirstOrDefault(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
            if (a != null) return a;
            return InputChoices.Keyboard.Concat(InputChoices.MouseButtons).Concat(InputChoices.MouseWheel)
                .FirstOrDefault(n => string.Equals(n, t, StringComparison.OrdinalIgnoreCase));
        }
    }
}
