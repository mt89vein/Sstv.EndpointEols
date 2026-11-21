using Microsoft.AspNetCore.Mvc;

namespace Sstv.EndpointEol.Sample.Controllers;

/// <summary>
/// Reports API. The export action is already announced as deprecated but is not blocked yet:
/// it demonstrates the <c>active</c> phase, where only the sunset date is exposed.
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Tags("Reports")]
public sealed class ReportsController : ControllerBase
{
    /// <summary>
    /// Starts a report export job.
    /// </summary>
    /// <returns>Accepted job descriptor.</returns>
    [HttpPost("export")]
    [EndOfLife("2099-01-01", WarnAfter = "2098-01-01", ReplacedBy = "/api/v2/reports/export")]
    public IActionResult Export() => Accepted(new { jobId = Guid.NewGuid(), status = "queued" });
}
