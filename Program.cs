using Microsoft.Data.Sqlite;
var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Inicializar base de datos SQLite y tabla de usuarios
var dbPath = "users.db";
using (var connection = new SqliteConnection($"Data Source={dbPath}"))
{
    connection.Open();
    var tableCmd = connection.CreateCommand();
    tableCmd.CommandText = @"CREATE TABLE IF NOT EXISTS users (wa_id TEXT PRIMARY KEY);";
    tableCmd.ExecuteNonQuery();
}

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

        // Extraer wa_id del mensaje entrante
        var root = jsonDocument.RootElement;
        if (root.TryGetProperty("entry", out var entryArray) && entryArray.GetArrayLength() > 0)
        {
            var entry = entryArray[0];
            if (entry.TryGetProperty("changes", out var changesArray) && changesArray.GetArrayLength() > 0)
            {
                var change = changesArray[0];
                if (change.TryGetProperty("value", out var valueObj) &&
                    valueObj.TryGetProperty("contacts", out var contactsArray) && contactsArray.GetArrayLength() > 0)
                {
                    var contact = contactsArray[0];
                    if (contact.TryGetProperty("wa_id", out var waIdProp))
                    {
                        var waId = waIdProp.GetString();
                        bool isFirstMessage = false;
                        // Consultar e insertar en la base de datos
                        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                        {
                            connection.Open();
                            var selectCmd = connection.CreateCommand();
                            selectCmd.CommandText = "SELECT COUNT(*) FROM users WHERE wa_id = $wa_id;";
                            selectCmd.Parameters.AddWithValue("$wa_id", waId);
                            var result = selectCmd.ExecuteScalar();
                            var count = (result != null) ? (long)result : 0;
                            if (count == 0)
                            {
                                // Primer mensaje de este usuario
                                isFirstMessage = true;
                                var insertCmd = connection.CreateCommand();
                                insertCmd.CommandText = "INSERT INTO users (wa_id) VALUES ($wa_id);";
                                insertCmd.Parameters.AddWithValue("$wa_id", waId);
                                insertCmd.ExecuteNonQuery();
                            }
                        }
                        if (isFirstMessage)
                        {
                            Console.WriteLine($"Primer mensaje de este usuario: {waId}");
                        }
                    }
                }
            }
        }
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
