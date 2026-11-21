using Microsoft.AspNetCore.Mvc;

namespace Sstv.EndpointEol.Sample.Controllers;

/// <summary>
/// Reports API v2. It is the replacement target of the deprecated v1 export action, so Swagger UI
/// can deep-link to it from the v1 description.
/// </summary>
[ApiController]
[Route("api/v2/reports")]
[Tags("Reports")]
public sealed class ReportsV2Controller : ControllerBase
{
    /// <summary>
    /// Starts a report export job using the v2 API.
    /// </summary>
    /// <returns>Accepted job descriptor.</returns>
    [HttpPost("export")]
    public IActionResult Export() => Accepted(new { jobId = Guid.NewGuid(), status = "queued" });
}
