﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCache.Strategies
{
    /// <summary>
    /// A strategy to asynchronously access or modify strongly-typed data in a Cache
    /// </summary>
    public class BulkCacheStrategyAsync<TKey, TResult> : BulkCacheStrategyIncomplete<TKey, TResult>
    {
        internal BulkCacheStrategyAsync(ICache cache, string baseKey, ICollection<TKey> keys)
            : base(cache, baseKey, keys)
        {
        }

        internal Func<CachedValue<TResult>, Task<CacheValidationResult>> ValidateCallback { get; set; }
        internal Func<ICollection<TKey>, Task<ICollection<KeyValuePair<TKey, TResult>>>> RetrieveCallback { get; set; }

        /// <summary>
        /// Invalidates the cached value if the specified asynchronous validation delegate returns CacheValidationResult.Invalid
        /// </summary>
        /// <param name="validate">An asynchronous delegate that validates the cached value</param>
        /// <returns>An updated cache strategy that includes the invalidation strategy</returns>
        public BulkCacheStrategyAsync<TKey, TResult> ValidateAsync(Func<CachedValue<TResult>, Task<CacheValidationResult>> validate)
        {
            this.ValidateCallback = cachedvalue => validate(cachedvalue);
            return this;
        }

        /// <summary>
        /// Invalidates the cached value if the specified asynchronous validation delegate returns false
        /// </summary>
        /// <param name="validate">An asynchronous delegate that validates the cached value</param>
        /// <returns>An updated cache strategy that includes the invalidation strategy</returns>
        public BulkCacheStrategyAsync<TKey, TResult> InvalidateIfAsync(Func<CachedValue<TResult>, Task<bool>> validate)
        {
            Func<CachedValue<TResult>, Task<CacheValidationResult>> val = async existing => (await validate(existing)) ? CacheValidationResult.Valid : CacheValidationResult.Invalid;
            this.ValidateCallback = val;
            return this;
        }

        /// <summary>
        /// Invalidates the cached value if the specified validation delegate returns CacheValidationResult.Invalid
        /// </summary>
        /// <param name="validate">A delegate that validates the cached value</param>
        /// <returns>An updated cache strategy that includes the invalidation strategy</returns>
        public BulkCacheStrategyAsync<TKey, TResult> Validate(Func<CachedValue<TResult>, CacheValidationResult> validate)
        {
            Func<CachedValue<TResult>, Task<CacheValidationResult>> val = existing => Task.FromResult(validate(existing));
            return this.ValidateAsync(val);
        }

        /// <summary>
        /// Invalidates the cached value if the specified validation delegate returns false
        /// </summary>
        /// <param name="validate">A delegate that validates the cached value</param>
        /// <returns>An updated cache strategy that includes the invalidation strategy</returns>
        public BulkCacheStrategyAsync<TKey, TResult> InvalidateIf(Func<CachedValue<TResult>, bool> validate)
        {
            Func<CachedValue<TResult>, Task<bool>> val = existing => Task.FromResult(validate(existing));
            return this.InvalidateIfAsync(val); ;
        }

        /// <summary>
        /// Asynchronously gets all cached results
        /// </summary>
        public async Task<IList<CachedValue<TResult>>> GetAllAsync()
        {
            var keysToLoad = Keys.ToList();
            var results = new List<CachedValue<TResult>>(Keys.Count);

            foreach (TKey key in Keys)
            {
                string itemKey = GetItemKey(key);
                CacheStrategyAsync<TResult> itemStrategy = new CacheStrategyAsync<TResult>(Cache, itemKey).WithRegion(Region);

                if (RetrieveCallback != null)
                {
                    itemStrategy = itemStrategy.RetrieveUsingAsync(async () =>
                    {
                        var awaitedResults = await RetrieveCallback(new[] { key });
                        return awaitedResults.First().Value;
                    });
                }

                if (ValidateCallback != null)
                    itemStrategy = itemStrategy.ValidateAsync(ValidateCallback);

                CachedValue<TResult> cachedValue = await itemStrategy.GetAsync();
                if (cachedValue != null)
                {
                    keysToLoad.Remove(key);
                    results.Add(cachedValue);
                }
            }

            if (RetrieveCallback != null)
            {
                ICollection<KeyValuePair<TKey, TResult>> newResults = await RetrieveCallback(keysToLoad);

                foreach (KeyValuePair<TKey, TResult> result in newResults)
                {
                    string itemKey = GetItemKey(result.Key);
                    TResult value = result.Value;

                    CachedValue<TResult> cachedValue = Cache.Set(itemKey, Region, value, DefaultExpiration);

                    results.Add(cachedValue);
                }
            }

            return results;
        }

        /// <summary>
        /// Asynchronously gets all cached values
        /// </summary>
        public async Task<IList<TResult>> GetAllValuesAsync()
        {
            IList<CachedValue<TResult>> results = await GetAllAsync();
            return results.Select(s => s.Value)
                          .ToList();
        }
    }

}
