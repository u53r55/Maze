﻿using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using Orcus.Server.Connection.JsonConverters;
using Orcus.Server.Service.ModulesV2.Config.Base;

namespace Orcus.Server.Service.ModulesV2.Config
{
    /// <summary>
    ///     Provides all required modules including their dependencies
    /// </summary>
    public interface IModulesLock
    {
        /// <summary>
        ///     All modules including their dependencies
        /// </summary>
        IImmutableDictionary<PackageIdentity, IList<PackageDependencyGroup>> Modules { get; }

        /// <summary>
        ///     The local path to the module file
        /// </summary>
        string Path { get; }

        /// <summary>
        ///     Reload the config file from disk
        /// </summary>
        Task Reload();

        /// <summary>
        ///     Add a new module
        /// </summary>
        /// <param name="id">The module identity</param>
        /// <param name="dependencies">It's depdendencies</param>
        Task Add(PackageIdentity id, IList<PackageDependencyGroup> dependencies);

        /// <summary>
        ///     Remove a module
        /// </summary>
        /// <param name="id">The module identity</param>
        Task Remove(PackageIdentity id);

        /// <summary>
        ///     Replace the whole module list
        /// </summary>
        /// <param name="modules">The new list</param>
        Task Replace(IDictionary<PackageIdentity, IList<PackageDependencyGroup>> modules);
    }

    public class ModulesLock : JsonObjectFile<IImmutableDictionary<PackageIdentity, IList<PackageDependencyGroup>>>,
        IModulesLock
    {
        private readonly IImmutableDictionary<PackageIdentity, IList<PackageDependencyGroup>> _empty;

        public ModulesLock(string path) : base(path)
        {
            _empty =
                new Dictionary<PackageIdentity, IList<PackageDependencyGroup>>().ToImmutableDictionary(PackageIdentity
                    .Comparer);
            Modules = _empty;

            JsonSettings.Converters.Add(new PackageIdentityConverter());
            JsonSettings.Converters.Add(new NuGetVersionConverter());
        }

        public IImmutableDictionary<PackageIdentity, IList<PackageDependencyGroup>> Modules { get; private set; }

        public virtual async Task Reload()
        {
            var data = await Load();
            Modules = data == null
                ? _empty
                : data.ToImmutableDictionary(PackageIdentity.Comparer);
        }

        public virtual Task Add(PackageIdentity id, IList<PackageDependencyGroup> dependencies)
        {
            Modules = Modules.Add(id, dependencies);
            return Save(Modules);
        }

        public virtual Task Remove(PackageIdentity id)
        {
            Modules = Modules.Remove(id);
            return Save(Modules);
        }

        public Task Replace(IDictionary<PackageIdentity, IList<PackageDependencyGroup>> modules)
        {
            Modules = modules.ToImmutableDictionary(PackageIdentity.Comparer);
            return Save(Modules);
        }
    }
}