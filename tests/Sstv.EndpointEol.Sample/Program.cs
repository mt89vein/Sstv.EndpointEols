using Microsoft.OpenApi;
using Sstv.EndpointEol;
using Sstv.EndpointEol.Swashbuckle;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Sstv.EndpointEol sample",
        Version = "v1",
        Description = "Try endpoint end-of-life management: headers, phases, blocking, diagnostics and Swagger annotations."
    });
});

builder.Services.AddEndOfLife(builder.Configuration.GetSection("EndOfLife"));
builder.Services.AddEndOfLifeSwagger();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Sstv.EndpointEol sample v1");
    options.DocumentTitle = "Sstv.EndpointEol sample";
    options.EnableDeepLinking();
});

app.UseRouting();
app.UseEndOfLife();

app.MapControllers();

// Minimal API endpoints are supported as well: WithEndOfLife attaches the same metadata.
app.MapGet("/api/v1/legacy/ping", static () => Results.Ok(new { pong = true }))
    .WithTags("Legacy")
    .WithEndOfLife(blockAfter: "2026-01-01", warnAfter: "2025-01-01", replacedBy: "/api/v2/ping");

// The replacement target referenced by the deprecated Minimal API endpoint above.
app.MapGet("/api/v2/ping", static () => Results.Ok(new { pong = true }))
    .WithTags("Legacy");

app.MapEndOfLifeDiagnostics("/api/v1/diagnostics/end-of-life");

app.MapGet("/", static () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

app.Run();
