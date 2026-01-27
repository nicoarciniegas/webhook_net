var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Get configuration values
var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
var verifyToken = Environment.GetEnvironmentVariable("VERIFY_TOKEN");

// Configure Kestrel to listen on all interfaces
app.Urls.Clear();
app.Urls.Add($"http://0.0.0.0:{port}");

// Route for GET requests (webhook verification)
app.MapGet("/", (HttpContext context) =>
{
    var query = context.Request.Query;
    var mode = query["hub.mode"].ToString();
    var challenge = query["hub.challenge"].ToString();
    var token = query["hub.verify_token"].ToString();

    if (mode == "subscribe" && token == verifyToken)
    {
        Console.WriteLine("WEBHOOK VERIFIED");
        // Responder con texto plano, no JSON
        return Results.Text(challenge, "text/plain");
    }
    else
    {
        return Results.StatusCode(403);
    }
});

// Route for POST requests (receive webhook messages)
app.MapPost("/", async (HttpContext context) =>
{
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    Console.WriteLine($"\n\nWebhook received {timestamp}\n");

    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    
    // Parse and pretty print JSON
    try
    {
        var jsonDocument = System.Text.Json.JsonDocument.Parse(body);
        var prettyJson = System.Text.Json.JsonSerializer.Serialize(
            jsonDocument.RootElement, 
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );
        Console.WriteLine(prettyJson);
    }
    catch
    {
        Console.WriteLine(body);
    }

    return Results.Ok();
});

// Log startup
Console.WriteLine($"\nListening on port {port}\n");

// Start the server
app.Run();
