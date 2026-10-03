using System;

namespace DesktopMonitor
{
    // The card's temperature history: the last Capacity points, one per interval, oldest first.
    // A missing reading is stored as a gap (null) so the time axis stays honest.
    public sealed class History
    {
        private readonly double?[] _points;
        private readonly TimeSpan _every;
        private int _count, _next;
        private DateTime _lastAt = DateTime.MinValue;

        public History(int capacity, TimeSpan every)
        {
            _points = new double?[capacity];
            _every = every;
        }

        public int Capacity { get { return _points.Length; } }

        public int Count { get { return _count; } }

        // Adds the value when at least one interval has passed since the last point. Returns true when a point was added.
        public bool Offer(DateTime now, double? value)
        {
            if (_lastAt != DateTime.MinValue && now - _lastAt < _every) return false;
            _lastAt = now;
            _points[_next] = value;
            _next = (_next + 1) % _points.Length;
            if (_count < _points.Length) _count++;
            return true;
        }

        public double?[] ToArray()
        {
            var result = new double?[_count];
            int start = (_next - _count + _points.Length) % _points.Length;
            for (int i = 0; i < _count; i++) result[i] = _points[(start + i) % _points.Length];
            return result;
        }

        public double? Peak
        {
            get
            {
                double? peak = null;
                foreach (double? p in ToArray())
                    if (p.HasValue && (!peak.HasValue || p.Value > peak.Value)) peak = p;
                return peak;
            }
        }
    }
}
