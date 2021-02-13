using System;
using Octopus.Time;

namespace Tests
{
    public class FixedClock : IClock
    {
        private DateTimeOffset now;

        public FixedClock(DateTimeOffset now) => this.now = now;

        public void Set(DateTimeOffset value) => this.now = value;

        public void WindForward(TimeSpan time) => this.now = this.now.Add(time);

        public DateTimeOffset GetUtcTime() => this.Clone().now.ToUniversalTime();

        public DateTimeOffset GetLocalTime() => this.Clone().now.ToLocalTime();

        private FixedClock Clone() => (FixedClock) this.MemberwiseClone();
    }
}
