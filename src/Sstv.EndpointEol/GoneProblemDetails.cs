using System.Text.Json.Serialization;

namespace Sstv.EndpointEol;

/// <summary>
/// Body of the <c>410 Gone</c> response sent to a blocked endpoint.
/// Replace it through <see cref="EndOfLifeOptions.GoneProblemDetailsFactory"/>
/// to align the payload with an application-specific error contract.
/// </summary>
public sealed record GoneProblemDetails
{
    /// <summary>
    /// Problem type URI describing the <c>410</c> status.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Short human-readable summary.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Status code, always 410.
    /// </summary>
    [JsonPropertyName("status")]
    public required int Status { get; init; }

    /// <summary>
    /// Human-readable explanation including the shutdown date and the replacement hint.
    /// </summary>
    [JsonPropertyName("detail")]
    public required string Detail { get; init; }

    /// <summary>
    /// Date the endpoint was shut down (<c>yyyy-MM-dd</c>).
    /// </summary>
    [JsonPropertyName("sunset")]
    public required string Sunset { get; init; }

    /// <summary>
    /// Replacement hint, as specified in the attribute.
    /// </summary>
    [JsonPropertyName("replacement")]
    public string? Replacement { get; init; }

    /// <summary>
    /// Trace id of the request, for correlation with server logs.
    /// </summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; init; }

    /// <summary>
    /// Extra fields merged into the payload as-is.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, object?>? Extensions { get; init; }
}