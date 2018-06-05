﻿using Orcus.Modules.Api;
using Orcus.Modules.Api.Routing;

namespace PowerUserTools.ActiveConnections
{
    [Route("[module]")]
    public class ActiveConnectionsController : OrcusController
    {
        [OrcusGet]
        public IActionResult GetConnections()
        {
            return Ok(null);
        }
    }
}