﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orcus.Administration.Modules.Utilities
{
    /// <summary>
    ///     Contains task execution strategies, such as parallel throttled execution.
    /// </summary>
    public static class TaskCombinators
    {
        public const int MaxDegreeOfParallelism = 16;

        public static async Task<IEnumerable<TValue>> ThrottledAsync<TSource, TValue>(
            IEnumerable<TSource> sources,
            Func<TSource, CancellationToken, Task<TValue>> valueSelector,
            CancellationToken cancellationToken)
        {
            var bag = new ConcurrentBag<TSource>(sources);
            var values = new ConcurrentQueue<TValue>();

            async Task TaskBody()
            {
                while (bag.TryTake(out var source))
                {
                    var value = await valueSelector(source, cancellationToken);
                    values.Enqueue(value);
                }
            }

            var tasks = Enumerable
                .Repeat(0, Math.Min(MaxDegreeOfParallelism, bag.Count))
                .Select(_ => Task.Run(TaskBody));

            await Task.WhenAll(tasks);

            return values;
        }

        public static IDictionary<string, Task<TValue>> ObserveErrorsAsync<TSource, TValue>(
            IEnumerable<TSource> sources,
            Func<TSource, string> keySelector,
            Func<TSource, CancellationToken, Task<TValue>> valueSelector,
            Action<Task, object> observeErrorAction,
            CancellationToken cancellationToken)
        {
            var tasks = sources
                .ToDictionary(
                    s => keySelector(s),
                    s =>
                    {
                        var valueTask = valueSelector(s, cancellationToken);
                        var ignored = valueTask.ContinueWith(
                            observeErrorAction,
                            s,
                            cancellationToken,
                            TaskContinuationOptions.OnlyOnFaulted,
                            TaskScheduler.Current);
                        return valueTask;
                    });

            return tasks;
        }
    }
}