using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using Octopus.Caching;
using Octopus.Time;

namespace Tests
{
    [TestFixture]
    public class OctopusCacheFixture
    {
        readonly FixedClock clock = new FixedClock(new DateTimeOffset(2000,
            1,
            1,
            1,
            1,
            1,
            TimeSpan.FromHours(2)));

        readonly Func<Guid> factory = () => Guid.NewGuid();

        [Test]
        public void CachedItemRetrievedInThePastReturnsSameItem()
        {
            var cache = new OctopusCache(clock);
            Guid GetOrAdd() => cache.GetOrAdd("key", factory, TimeSpan.FromHours(1));
            var originalResult = GetOrAdd();
            clock.WindForward(TimeSpan.FromSeconds(-2)); // Computer clock may have adjusted
            GetOrAdd().Should().Be(originalResult);
        }

        [Test]
        public void CachedItemRetrievedJustBeforeExpiryReturnsTheSameItem()
        {
            var cache = new OctopusCache(clock);
            Guid GetOrAdd() => cache.GetOrAdd("key", factory, TimeSpan.FromHours(1));

            var originalResult = GetOrAdd();
            clock.WindForward(TimeSpan.FromHours(1).Subtract(TimeSpan.FromTicks(1)));
            GetOrAdd().Should().Be(originalResult);
        }

        [Test]
        public void CachedItemRetrievedJustAfterExpiryReturnsADifferentItem()
        {
            var cache = new OctopusCache(clock);
            Guid GetOrAdd() => cache.GetOrAdd("key", factory, TimeSpan.FromHours(1));
            var originalResult = GetOrAdd();
            clock.WindForward(TimeSpan.FromHours(1).Add(TimeSpan.FromTicks(1)));
            GetOrAdd().Should().NotBe(originalResult);
        }

        [Test]
        [Timeout(1000)]
        public void ASecondThreadAccessingDuringTheFactoryStillGetsTheSameItem()
        {
            Guid DelayedFactory()
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(300));
                return Guid.NewGuid();
            }

            var cache = new OctopusCache(clock);
            Guid GetOrAdd() => cache!.GetOrAdd("key", DelayedFactory, TimeSpan.FromHours(1));

            Guid? result1 = null;
            Guid? result2 = null;

            var thread1 = new Thread(() => result1 = GetOrAdd());
            var thread2 = new Thread(() => result2 = GetOrAdd());
            thread1.Start();
            thread2.Start();

            while (thread1.IsAlive || thread2.IsAlive)
                Thread.Sleep(TimeSpan.FromMilliseconds(10));

            result1.Should().Be(result2);
        }

        [Test]
        [Timeout(1000)]
        public void RetrievingByADifferentKeyShouldNotBlock()
        {
            var continueHandle = new EventWaitHandle(false, EventResetMode.ManualReset);

            Guid DelayedFactory()
            {
                continueHandle!.WaitOne();
                return Guid.NewGuid();
            }

            var cache = new OctopusCache(clock);

            Guid? result2 = null;

            var thread1 = new Thread(() => cache.GetOrAdd("key1", DelayedFactory, TimeSpan.FromHours(1)));
            var thread2 = new Thread(() => result2 = cache.GetOrAdd("key2", factory, TimeSpan.FromHours(1)));
            thread1.Start();
            thread2.Start();

            while (thread2.IsAlive)
                Thread.Sleep(TimeSpan.FromMilliseconds(10));

            result2.Should().HaveValue();
            continueHandle.Set();

            while (thread1.IsAlive)
                Thread.Sleep(TimeSpan.FromMilliseconds(10));
        }

        [Test]
        [Description("This test proves we handle exceptions being thrown in the initialization code, by allowing the exception to bubble up to the caller, and allowing the initialization to be attempted again.")]
        public void WhenTheInitializerThrows_WeShouldEvictAndTryAgain()
        {
            var cacheKey = "test";
            var initializationCalls = 0;
            var expectedExceptions = new List<DivideByZeroException>();

            var cache = new OctopusCache(clock);
            for (var i = 0; i < 10; i++)
            {
                var callCounter = i;
                try
                {
                    var cached = cache.GetOrAdd(cacheKey,
                        () =>
                        {
                            initializationCalls++;
                            // Fail on the first few calls, and succeed thereafter - simulates a SQL Server being unavailable for a while
                            if (callCounter < 5) throw new DivideByZeroException();
                            return $"value-{callCounter}";
                        },
                        TimeSpan.FromSeconds(1));

                    cached.Should().Be("value-5", "the value I cached after failing the first few times should be returned consistently until the cache expires");
                }
                catch (DivideByZeroException dbze)
                {
                    expectedExceptions.Add(dbze);
                }
            }

            expectedExceptions.Should().HaveCount(5, "the first few initialization calls should have thrown an exception");
            initializationCalls.Should().Be(6, "the initialization function should have failed a few times, then called one more time successfully once it starts working");
        }

        [Test]
        public void RemovingSomethingFromTheCacheWorksAsExpected()
        {
            var cacheKey = "test";
            var initializationCalls = 0;

            var cache = new OctopusCache(clock);
            for (var i = 0; i < 10; i++)
            {
                var callCounter = i;
                var cached = cache.GetOrAdd(cacheKey,
                    () =>
                    {
                        initializationCalls++;
                        return $"value-{callCounter}";
                    },
                    TimeSpan.FromHours(1));

                if (callCounter <= 5)
                    cached.Should().Be("value-0", "the value I initially cached should be returned consistently until the item is expired or deleted manually");
                else
                    cached.Should().Be("value-6", "the value should be reevaluated after being removed from the cache");

                if (callCounter == 5)
                    cache.Delete(cacheKey);
            }

            initializationCalls.Should().Be(2, "we should only initialize if there is no existing value in the cache and return the cached instance from there");
        }

        [Test]
        public void ClearingTheCacheShouldRemoveEverything()
        {
            var initializationCalls = 0;

            var cache = new OctopusCache(clock);
            for (var i = 0; i < 10; i++)
            {
                var callCounter = i;
                var cached = cache.GetOrAdd($"key-{callCounter}",
                    () =>
                    {
                        initializationCalls++;
                        return $"value-{callCounter}";
                    },
                    TimeSpan.FromHours(1));

                cached.Should().Be($"value-{callCounter}", "the value I initially cached should be returned consistently until the cache is cleared");
            }

            cache.RemoveWhere(key => true);

            for (var i = 0; i < 10; i++)
            {
                var callCounter = i;
                var cached = cache.GetOrAdd($"key-{callCounter}",
                    () =>
                    {
                        initializationCalls++;
                        return $"value-{callCounter}";
                    },
                    TimeSpan.FromHours(1));

                cached.Should().Be($"value-{callCounter}", "the value I initially set after the first expired should be returned consistently until the cache expires again");
            }

            initializationCalls.Should().Be(20, "after clearing the cache we should have to reinitialize everything");
        }
    }
}
