namespace Sstv.EndpointEol.Sample.Models;

/// <summary>
/// Book returned by the sample API.
/// </summary>
/// <param name="Id">Book identifier.</param>
/// <param name="Title">Book title.</param>
/// <param name="Author">Book author.</param>
public sealed record Book(Guid Id, string Title, string Author);
