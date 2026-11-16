using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GamepadEnhancer
{
    /// <summary>One step of a recoil pattern: hold (X, Y) percent for IntervalMs.</summary>
    public sealed class RecoilStep : INotifyPropertyChanged
    {
        private int _x;
        private int _y;
        private double _intervalMs = 80;

        public int X { get => _x; set => Set(ref _x, value); }
        public int Y { get => _y; set => Set(ref _y, value); }
        public double IntervalMs { get => _intervalMs; set => Set(ref _intervalMs, value); }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
