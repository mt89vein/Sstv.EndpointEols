using Microsoft.AspNetCore.Mvc;

namespace Sstv.EndpointEol.Sample.Controllers;

/// <summary>
/// Legacy API. The whole controller is past its block date: with
/// <c>EndOfLife:EnforcementMode=Enforce</c> every request receives <c>410 Gone</c>.
/// </summary>
[ApiController]
[Route("api/v1/legacy")]
[Tags("Legacy")]
[EndOfLife("2026-01-01", WarnAfter = "2025-01-01", ReplacedBy = "https://example.com/migrate-legacy")]
public sealed class LegacyController : ControllerBase
{
    /// <summary>
    /// Legacy status endpoint.
    /// </summary>
    /// <returns>Legacy status payload.</returns>
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { status = "legacy" });
}
