using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Maze.Modules.Api.ModelBinding;
using Maze.Modules.Api.Parameters;
using Maze.Service.Commander.Commanding.Internal;

namespace Maze.Service.Commander.Commanding.ModelBinding
{
    /// <summary>
    /// An <see cref="IValueProvider"/> adapter for data stored in an <see cref="IQueryCollection"/>.
    /// </summary>
    public class QueryStringValueProvider : IValueProvider
    {
        private readonly CultureInfo _culture;
        private readonly IQueryCollection _values;
        private PrefixContainer _prefixContainer;

        /// <summary>
        /// Creates a value provider for <see cref="IQueryCollection"/>.
        /// </summary>
        /// <param name="bindingSource">The <see cref="BindingSource"/> for the data.</param>
        /// <param name="values">The key value pairs to wrap.</param>
        /// <param name="culture">The culture to return with ValueProviderResult instances.</param>
        public QueryStringValueProvider(
            BindingSource bindingSource,
            IQueryCollection values,
            CultureInfo culture)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            _values = values;
            _culture = culture;
        }

        public CultureInfo Culture => _culture;
        
        protected PrefixContainer PrefixContainer
        {
            get
            {
                if (_prefixContainer == null)
                {
                    _prefixContainer = new PrefixContainer(_values.Keys);
                }

                return _prefixContainer;
            }
        }

        /// <inheritdoc />
        public bool ContainsPrefix(string prefix)
        {
            return PrefixContainer.ContainsPrefix(prefix);
        }

        /// <inheritdoc />
        public virtual IDictionary<string, string> GetKeysFromPrefix(string prefix)
        {
            if (prefix == null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            return PrefixContainer.GetKeysFromPrefix(prefix);
        }

        /// <inheritdoc />
        public ValueProviderResult GetValue(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var values = _values[key];
            if (values.Count == 0)
            {
                return ValueProviderResult.None;
            }
            else
            {
                return new ValueProviderResult(values, _culture);
            }
        }
    }
}