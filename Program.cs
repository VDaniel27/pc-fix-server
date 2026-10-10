using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5001", "https://localhost:7124")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

var rateLimiter = new ConcurrentDictionary<string, (int count, DateTime reset)>();

app.MapPost("/api/chat", async (HttpContext ctx, IHttpClientFactory factory, IConfiguration config) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var now = DateTime.UtcNow;
    var entry = rateLimiter.GetOrAdd(ip, _ => (0, now.AddHours(1)));
    if (now > entry.reset) entry = (0, now.AddHours(1));
    if (entry.count >= 50)
        return Results.Json(new { error = new { message = "Límite de 50 mensajes por hora alcanzado. Intenta más tarde." } }, statusCode: 429);
    rateLimiter[ip] = (entry.count + 1, entry.reset);

    var apiKey = config["Gemini:ApiKey"]
        ?? throw new InvalidOperationException("Gemini:ApiKey no configurada en appsettings.json.");

    var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();

    // Envolver con system instruction
    var payload = new
    {
        system_instruction = new
        {
            parts = new[] { new { text = "Eres un experto en mantenimiento y reparación de PCs y laptops. Ayudas a personas de cualquier nivel técnico con diagnósticos, guías paso a paso, y recomendaciones de componentes. Responde siempre en español de forma clara y amigable." } }
        },
        contents = body.GetProperty("contents")
    };

    var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent?key={apiKey}";
    var http = factory.CreateClient();
    var response = await http.PostAsJsonAsync(url, payload);
    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
    return Results.Json(result, statusCode: (int)response.StatusCode);
});

app.Run();
