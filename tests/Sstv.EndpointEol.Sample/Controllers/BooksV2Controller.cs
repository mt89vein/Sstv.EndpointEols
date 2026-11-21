using Microsoft.AspNetCore.Mvc;

namespace Sstv.EndpointEol.Sample.Controllers;

/// <summary>
/// Books API v2. It is the replacement target of the deprecated v1 action, so Swagger UI can
/// deep-link to it from the v1 description.
/// </summary>
[ApiController]
[Route("api/v2/books")]
[Tags("Books")]
public sealed class BooksV2Controller : ControllerBase
{
    /// <summary>
    /// Deletes a book using the v2 API.
    /// </summary>
    /// <param name="id">Book identifier.</param>
    /// <returns>204 when deleted.</returns>
    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id) =>
        id == Guid.Empty ? BadRequest() : NoContent();
}
