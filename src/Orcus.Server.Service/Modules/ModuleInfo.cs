﻿using NuGet.Packaging.Core;
using Orcus.Server.Connection.Modules;

namespace Orcus.Server.Service.Modules
{
    public class ModuleInfo
    {
        public bool IsLoaded { get; set; }
        public string Filename { get; set; }
        public PackageIdentity Id { get; set; }
        public ModuleDto Dto { get; set; }
    }
}