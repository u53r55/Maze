﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Indexing;
using NuGet.Protocol.Core.Types;
using Orcus.Administration.Modules.Extensions;
using Orcus.Administration.Modules.Utilities;

namespace Orcus.Administration.Modules.Models.Feed
{
    /// <summary>
    /// Consolidated live sources package feed enumerating packages and aggregating search results.
    /// </summary>
    public sealed class MultiSourcePackageFeed : IPackageFeed
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
        private const int PageSize = 25;

        private readonly SourceRepository[] _sourceRepositories;

        public bool IsMultiSource => _sourceRepositories.Length > 1;

        private class AggregatedContinuationToken : ContinuationToken
        {
            public string SearchString { get; set; }
            public IDictionary<string, ContinuationToken> SourceSearchCursors { get; set; } = new Dictionary<string, ContinuationToken>();
        }

        private class AggregatedRefreshToken : RefreshToken
        {
            public string SearchString { get; set; }
            public IDictionary<string, Task<SearchResult<IPackageSearchMetadata>>> SearchTasks { get; set; }
            public IDictionary<string, LoadingStatus> SourceSearchStatus { get; set; }
        }

        public MultiSourcePackageFeed(IEnumerable<SourceRepository> sourceRepositories)
        {
            if (sourceRepositories == null)
                throw new ArgumentNullException(nameof(sourceRepositories));

            _sourceRepositories = sourceRepositories.ToArray();

            if (!_sourceRepositories.Any())
                throw new ArgumentException("Collection of source repositories cannot be empty", nameof(sourceRepositories));
        }

        public async Task<SearchResult<IPackageSearchMetadata>> SearchAsync(string searchText, SearchFilter filter, CancellationToken cancellationToken)
        {
            var searchTasks = TaskCombinators.ObserveErrorsAsync(
                _sourceRepositories,
                r => r.PackageSource.Name,
                (r, t) => r.SearchAsync(searchText, filter, PageSize, t),
                LogError,
                cancellationToken);

            return await WaitForCompletionOrBailOutAsync(searchText, searchTasks, cancellationToken);
        }

        public async Task<SearchResult<IPackageSearchMetadata>> ContinueSearchAsync(ContinuationToken continuationToken, CancellationToken cancellationToken)
        {
            var searchToken = continuationToken as AggregatedContinuationToken;

            if (searchToken?.SourceSearchCursors == null)
            {
                throw new InvalidOperationException("Invalid token");
            }

            var searchTokens = _sourceRepositories
                .Join(searchToken.SourceSearchCursors,
                    r => r.PackageSource.Name,
                    c => c.Key,
                    (r, c) => new { Repository = r, NextToken = c.Value });

            var searchTasks = TaskCombinators.ObserveErrorsAsync(
                searchTokens,
                j => j.Repository.PackageSource.Name,
                (j, t) => j.Repository.SearchAsync(j.NextToken, PageSize, t),
                LogError,
                cancellationToken);

            return await WaitForCompletionOrBailOutAsync(searchToken.SearchString, searchTasks, cancellationToken);
        }

        public async Task<SearchResult<IPackageSearchMetadata>> RefreshSearchAsync(RefreshToken refreshToken, CancellationToken cancellationToken)
        {
            var searchToken = refreshToken as AggregatedRefreshToken;

            if (searchToken == null)
            {
                throw new InvalidOperationException("Invalid token");
            }

            return await WaitForCompletionOrBailOutAsync(searchToken.SearchString, searchToken.SearchTasks, cancellationToken);
        }

        private async Task<SearchResult<IPackageSearchMetadata>> WaitForCompletionOrBailOutAsync(
            string searchText,
            IDictionary<string, Task<SearchResult<IPackageSearchMetadata>>> searchTasks,
            CancellationToken cancellationToken)
        {
            if (searchTasks.Count == 0)
            {
                return SearchResult.Empty<IPackageSearchMetadata>();
            }

            var aggregatedTask = Task.WhenAll(searchTasks.Values);

            RefreshToken refreshToken = null;
            if (aggregatedTask != await Task.WhenAny(aggregatedTask, Task.Delay(DefaultTimeout)))
            {
                refreshToken = new AggregatedRefreshToken
                {
                    SearchString = searchText,
                    SearchTasks = searchTasks,
                    RetryAfter = DefaultTimeout
                };
            }

            var partitionedTasks = searchTasks
                .ToLookup(t => t.Value.Status == TaskStatus.RanToCompletion);

            var completedOnly = partitionedTasks[true];

            SearchResult<IPackageSearchMetadata> aggregated;

            if (completedOnly.Any())
            {
                var results = await Task.WhenAll(completedOnly.Select(kv => kv.Value));
                aggregated = await AggregateSearchResultsAsync(searchText, results);
            }
            else
            {
                aggregated = SearchResult.Empty<IPackageSearchMetadata>();
            }

            aggregated.RefreshToken = refreshToken;

            var notCompleted = partitionedTasks[false];

            if (notCompleted.Any())
            {
                var statuses = notCompleted.ToDictionary(
                    kv => kv.Key,
                    kv => GetLoadingStatus(kv.Value.Status));

                foreach (var item in statuses)
                {
                    aggregated.SourceSearchStatus.Add(item);
                }

                var exceptions = notCompleted
                    .Where(kv => kv.Value.Exception != null)
                    .ToDictionary(
                        kv => kv.Key,
                        kv => (Exception)kv.Value.Exception);

                foreach (var item in exceptions)
                {
                    aggregated.SourceSearchException.Add(item);
                }
            }

            return aggregated;
        }

        private static LoadingStatus GetLoadingStatus(TaskStatus taskStatus)
        {
            switch (taskStatus)
            {
                case TaskStatus.Canceled:
                    return LoadingStatus.Cancelled;
                case TaskStatus.Created:
                case TaskStatus.RanToCompletion:
                case TaskStatus.Running:
                case TaskStatus.WaitingForActivation:
                case TaskStatus.WaitingForChildrenToComplete:
                case TaskStatus.WaitingToRun:
                    return LoadingStatus.Loading;
                case TaskStatus.Faulted:
                    return LoadingStatus.ErrorOccurred;
                default:
                    return LoadingStatus.Unknown;
            }
        }

        private async Task<SearchResult<IPackageSearchMetadata>> AggregateSearchResultsAsync(
            string searchText,
            IEnumerable<SearchResult<IPackageSearchMetadata>> results)
        {
            SearchResult<IPackageSearchMetadata> result;

            var nonEmptyResults = results.Where(r => r.Any()).ToArray();
            if (nonEmptyResults.Length == 0)
            {
                result = SearchResult.Empty<IPackageSearchMetadata>();
            }
            else if (nonEmptyResults.Length == 1)
            {
                result = SearchResult.FromItems(nonEmptyResults[0].Items);
            }
            else
            {
                var items = nonEmptyResults.Select(r => r.Items).ToArray();

                var indexer = new RelevanceSearchResultsIndexer();
                var aggregator = new SearchResultsAggregator(indexer, new PackageSearchMetadataSplicer());
                var aggregatedItems = await aggregator.AggregateAsync(
                    searchText, items);

                result = SearchResult.FromItems(aggregatedItems.ToArray());
                // set correct count of unmerged items
                result.RawItemsCount = items.Aggregate(0, (r, next) => r + next.Count);
            }

            result.SourceSearchStatus = results
                .SelectMany(r => r.SourceSearchStatus)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var cursors = results
                .Where(r => r.NextToken != null)
                .ToDictionary(r => r.SourceSearchStatus.Single().Key, r => r.NextToken);

            if (cursors.Keys.Any())
            {
                result.NextToken = new AggregatedContinuationToken
                {
                    SearchString = searchText,
                    SourceSearchCursors = cursors
                };
            }

            return result;
        }

        private void LogError(Task task, object state)
        {
        }
    }
}
