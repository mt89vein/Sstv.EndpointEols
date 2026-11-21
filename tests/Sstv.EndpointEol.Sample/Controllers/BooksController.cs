using Microsoft.AspNetCore.Mvc;
using Sstv.EndpointEol.Sample.Models;

namespace Sstv.EndpointEol.Sample.Controllers;

/// <summary>
/// Books API. The <c>DELETE</c> action is in its warning phase and is scheduled for removal.
/// </summary>
[ApiController]
[Route("api/v1/books")]
[Tags("Books")]
public sealed class BooksController : ControllerBase
{
    private static readonly Book[] _books =
    [
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "The Pragmatic Programmer", "Andrew Hunt"),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Designing Data-Intensive Applications", "Martin Kleppmann")
    ];

    /// <summary>
    /// Returns all books.
    /// </summary>
    /// <returns>All books.</returns>
    [HttpGet]
    public ActionResult<IReadOnlyList<Book>> GetAll() => Ok(_books);

    /// <summary>
    /// Returns a single book.
    /// </summary>
    /// <param name="id">Book identifier.</param>
    /// <returns>The book, or 404 when it does not exist.</returns>
    [HttpGet("{id:guid}")]
    public ActionResult<Book> GetById(Guid id)
    {
        var book = Array.Find(_books, x => x.Id == id);

        return book is null ? NotFound() : Ok(book);
    }

    /// <summary>
    /// Deletes a book. Kept for backwards compatibility only: it will be shut down on 2027-01-01,
    /// warnings start on 2026-01-01 and consumers are pointed to the v2 endpoint.
    /// </summary>
    /// <param name="id">Book identifier.</param>
    /// <returns>204 when deleted.</returns>
    [HttpDelete("{id:guid}")]
    [EndOfLife("2027-01-01", WarnAfter = "2026-01-01", ReplacedBy = "/api/v2/books/{id}")]
    public IActionResult Delete(Guid id) =>
        Array.Exists(_books, x => x.Id == id) ? NoContent() : NotFound();
}
