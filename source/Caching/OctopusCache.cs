using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Octopus.Time;

namespace Octopus.Caching
{
    public sealed class OctopusCache : IOctopusCache
    {
        readonly IClock clock;
        readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>();

        public OctopusCache(IClock clock)
            => this.clock = clock;

        public TItem GetOrAdd<TItem>(string key, Func<TItem> valueFactory, TimeSpan expiresIn)
            where TItem : notnull
        {
            try
            {
                return (TItem)GetOrAddEntry(key, valueFactory, expiresIn).Item.Value;
            }
            catch
            {
                // Lazy initialization failed, we don't want to cache the exception
                // This could possibly evict a successful resolution that happened in the meantime, but that's not too terrible
                Delete(key);
                throw;
            }
        }

        public async Task<TItem> GetOrAdd<TItem>(string key, Func<Task<TItem>> valueFactory, TimeSpan expiresIn)
            where TItem : notnull
        {
            try
            {
                return await (Task<TItem>)GetOrAddEntry(key, valueFactory, expiresIn).Item.Value;
            }
            catch
            {
                // Lazy initialization failed, we don't want to cache the exception
                // This could possibly evict a successful resolution that happened in the meantime, but that's not too terrible
                Delete(key);
                throw;
            }
        }

        public TItem Update<TItem>(string key, TItem value, TimeSpan expiresIn)
            where TItem : notnull
        {
            lock (cache)
            {
                cache.Remove(key);
                cache[key] = new Entry(() => value, clock.GetUtcTime().Add(expiresIn));
                return (TItem) cache[key].Item.Value;
            }
        }

        Entry GetOrAddEntry<TItem>(string key, Func<TItem> valueFactory, TimeSpan expiresIn)
            where TItem : notnull
        {
            lock (cache)
            {
                foreach (var item in cache.Where(e => e.Value.HasExpired(clock)).ToArray())
                    cache.Remove(item.Key);

                if (!cache.ContainsKey(key))
                    cache[key] = new Entry(() => valueFactory(), clock.GetUtcTime().Add(expiresIn));

                return cache[key];
            }
        }

        public void Delete(string key)
        {
            lock (cache)
            {
                cache.Remove(key);
            }
        }

        public void RemoveWhere(Predicate<string> keyPredicate)
        {
            lock (cache)
            {
                var keysToRemove = cache.Keys.Where(key => keyPredicate(key)).ToArray();
                foreach (var key in keysToRemove)
                    cache.Remove(key);
            }
        }

        public void RemoveWhere<TItem>(Func<string, TItem, bool> valuePredicate)
            where TItem : notnull
        {
            lock (cache)
            {
                var keysToRemove = cache.Where(kvp => kvp.Value.Item.Value is TItem item && valuePredicate(kvp.Key, item)).Select(kvp => kvp.Key).ToArray();
                foreach (var key in keysToRemove)
                    cache.Remove(key);
            }
        }

        public void ResetAll()
        {
            lock (cache)
            {
                cache.Clear();
            }
        }

        class Entry
        {
            public Entry(Func<object> factory, DateTimeOffset expiry)
            {
                // We use Lazy<T> so that we can release the lock on the cache quickly without waiting for factory to complete
                // Lazy does internal locking to make sure the factory is not called twice
                Item = new Lazy<object>(factory);
                Expiry = expiry;
            }

            public Lazy<object> Item { get; }
            public DateTimeOffset Expiry { get; }

            public bool HasExpired(IClock clock) => Expiry < clock.GetUtcTime();
        }
    }
}
