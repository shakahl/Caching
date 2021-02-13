using System;
using System.Threading.Tasks;

namespace Octopus.Caching
{
    public interface IOctopusCache
    {
        TItem GetOrAdd<TItem>(string key, Func<TItem> valueFactory, TimeSpan expiresIn) where TItem : notnull;
        void Delete(string key);
        void RemoveWhere(Predicate<string> keyPredicate);
        void RemoveWhere<TItem>(Func<string, TItem, bool> valuePredicate) where TItem : notnull;
        void ResetAll();
    }
}
