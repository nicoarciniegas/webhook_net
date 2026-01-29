using Microsoft.Data.Sqlite;
var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Inicializar base de datos SQLite y tabla de usuarios
var dbPath = "users.db";
// Crear tablas 'cases' y 'users' en una sola conexión
using (var connection = new SqliteConnection($"Data Source={dbPath}"))
{
    connection.Open();
    var usersCmd = connection.CreateCommand();
    usersCmd.CommandText = @"CREATE TABLE IF NOT EXISTS users (
        wa_id TEXT PRIMARY KEY, 
        TIPO_SOLICITUD TEXT, 
        NOMBRE TEXT, 
        EMAIL TEXT,
        DESCRIPCION TEXT,
        DOCUMENT_URL TEXT
    );";
    usersCmd.ExecuteNonQuery();
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
                        var httpClient = new HttpClient();
                        var url = "https://graph.facebook.com/v23.0/976270252240458/messages";
                        var token = "EAAmNsGBlnEMBQjePsO5AgHXIJZCSoIbmRgUjMkmJYrVZCQ86Lpna6dyeKX67wxhCvkaptnGAHHqHHhtZBljhJjl2KuXpX0wo96cZAZBVMoV9QtGgNByfbZCYQmmJPKRykjRTUjmw4yyKZAk2x7E632bISp187jrlYxP6MSyarBE19rfYJnYNLxsPTAeLRpf1498mAZDZD";
                        if (isFirstMessage)
                        {
                            Console.WriteLine($"Primer mensaje de este usuario: {waId}");
                            // Enviar mensaje de bienvenida por WhatsApp
                            var welcomeBody = new
                            {
                                messaging_product = "whatsapp",
                                recipient_type = "individual",
                                to = waId,
                                type = "text",
                                text = new { body = "Hola, bienvenid@ al canal de Prodygytek. \nEscribe 1️⃣ si necesitas soporte tecnico ó escribe 2️⃣ para radicar una solicitud. " }
                            };
                            var jsonBody = System.Text.Json.JsonSerializer.Serialize(welcomeBody);
                            var request = new HttpRequestMessage(HttpMethod.Post, url);
                            request.Headers.Add("Authorization", $"Bearer {token}");
                            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                            try
                            {
                                var response = await httpClient.SendAsync(request);
                                var respContent = await response.Content.ReadAsStringAsync();
                                Console.WriteLine($"Mensaje de bienvenida enviado a {waId}. Respuesta: {respContent}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error enviando mensaje de bienvenida: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Usuario no es la primera vez que escribe: {waId}");
                            // Buscar el texto del mensaje recibido
                            if (valueObj.TryGetProperty("messages", out var messagesArray) && messagesArray.GetArrayLength() > 0)
                            {
                                var message = messagesArray[0];
                                if (message.TryGetProperty("text", out var textObj) && textObj.TryGetProperty("body", out var bodyProp))
                                {
                                    var userText = bodyProp.GetString()?.Trim();
                                    using (var connection2 = new SqliteConnection($"Data Source={dbPath}"))
                                    {
                                        connection2.Open();
                                        var checkCmd = connection2.CreateCommand();
                                        checkCmd.CommandText = "SELECT TIPO_SOLICITUD, NOMBRE, EMAIL, DESCRIPCION, DOCUMENT_URL FROM users WHERE wa_id = $wa_id;";
                                        checkCmd.Parameters.AddWithValue("$wa_id", waId);
                                        using (var reader2 = checkCmd.ExecuteReader())
                                        {
                                            if (reader2.Read())
                                            {
                                                var tipoSolicitud = reader2["TIPO_SOLICITUD"] as string;
                                                var nombre = reader2["NOMBRE"] as string;
                                                var email = reader2["EMAIL"] as string;
                                                var descripcion = reader2["DESCRIPCION"] as string;
                                                var documentUrl = reader2["DOCUMENT_URL"] as string;
                                                // Si el usuario responde 1 o 2 y aún no tiene tipo de solicitud
                                                if ((userText == "1" || userText == "2") && string.IsNullOrEmpty(tipoSolicitud))
                                                {
                                                    string tipo = userText == "1" ? "SOPORTE_TECNICO" : "RADICAR_SOLICITUD";
                                                    var updateCmd = connection2.CreateCommand();
                                                    updateCmd.CommandText = "UPDATE users SET TIPO_SOLICITUD = $tipo WHERE wa_id = $wa_id;";
                                                    updateCmd.Parameters.AddWithValue("$tipo", tipo);
                                                    updateCmd.Parameters.AddWithValue("$wa_id", waId);
                                                    updateCmd.ExecuteNonQuery();

                                                    string confirmMsg = userText == "1"
                                                        ? $"Hola {waId}, has elegido soporte técnico.\nPor favor, indícanos tu nombre completo."
                                                        : $"Hola {waId}, has elegido radicar una solicitud.\nPor favor, indícanos tu nombre completo.";
                                                    // Enviar mensaje de confirmación
                                                    var confirmBody = new
                                                    {
                                                        messaging_product = "whatsapp",
                                                        recipient_type = "individual",
                                                        to = waId,
                                                        type = "text",
                                                        text = new { body = confirmMsg }
                                                    };
                                                    var confirmJson = System.Text.Json.JsonSerializer.Serialize(confirmBody);
                                                    var confirmRequest = new HttpRequestMessage(HttpMethod.Post, url);
                                                    confirmRequest.Headers.Add("Authorization", $"Bearer {token}");
                                                    confirmRequest.Content = new StringContent(confirmJson, System.Text.Encoding.UTF8, "application/json");
                                                    try
                                                    {
                                                        var confirmResponse = await httpClient.SendAsync(confirmRequest);
                                                        var confirmRespContent = await confirmResponse.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Mensaje de confirmación enviado a {waId}. Respuesta: {confirmRespContent}");
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine($"Error enviando mensaje de confirmación: {ex.Message}");
                                                    }
                                                }
                                                // Si ya tiene tipo de solicitud pero no nombre, guardar el primer mensaje como nombre y pedir correo
                                                else if (!string.IsNullOrEmpty(tipoSolicitud) && string.IsNullOrEmpty(nombre) && userText != "1" && userText != "2")
                                                {
                                                    var updateCmd = connection2.CreateCommand();
                                                    updateCmd.CommandText = "UPDATE users SET NOMBRE = $nombre WHERE wa_id = $wa_id;";
                                                    updateCmd.Parameters.AddWithValue("$nombre", userText);
                                                    updateCmd.Parameters.AddWithValue("$wa_id", waId);
                                                    updateCmd.ExecuteNonQuery();

                                                    var askEmailBody = new
                                                    {
                                                        messaging_product = "whatsapp",
                                                        recipient_type = "individual",
                                                        to = waId,
                                                        type = "text",
                                                        text = new { body = $"¡Gracias {userText}! Ahora, por favor indícanos tu correo electrónico." }
                                                    };
                                                    var askEmailJson = System.Text.Json.JsonSerializer.Serialize(askEmailBody);
                                                    var askEmailRequest = new HttpRequestMessage(HttpMethod.Post, url);
                                                    askEmailRequest.Headers.Add("Authorization", $"Bearer {token}");
                                                    askEmailRequest.Content = new StringContent(askEmailJson, System.Text.Encoding.UTF8, "application/json");
                                                    try
                                                    {
                                                        var askEmailResponse = await httpClient.SendAsync(askEmailRequest);
                                                        var askEmailRespContent = await askEmailResponse.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Mensaje de solicitud de correo enviado a {waId}. Respuesta: {askEmailRespContent}");
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        Console.WriteLine($"Error enviando mensaje de solicitud de correo: {ex.Message}");
                                                    }
                                                }
                                                // Si ya tiene tipo de solicitud y nombre pero no email, y el mensaje parece un correo, guardar el correo
                                                else if (!string.IsNullOrEmpty(tipoSolicitud) && !string.IsNullOrEmpty(nombre))
                                                {
                                                    // Validar si el mensaje es un correo electrónico simple
                                                    if (System.Text.RegularExpressions.Regex.IsMatch(userText ?? "", @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                                                    {
                                                        var updateCmd = connection2.CreateCommand();
                                                        updateCmd.CommandText = "UPDATE users SET EMAIL = $email WHERE wa_id = $wa_id;";
                                                        updateCmd.Parameters.AddWithValue("$email", userText);
                                                        updateCmd.Parameters.AddWithValue("$wa_id", waId);
                                                        updateCmd.ExecuteNonQuery();

                                                        // Mensaje de agradecimiento por el correo
                                                        var thanksBody = new
                                                        {
                                                            messaging_product = "whatsapp",
                                                            recipient_type = "individual",
                                                            to = waId,
                                                            type = "text",
                                                            text = new { body = $"¡Gracias! Hemos registrado tu correo electrónico." }
                                                        };
                                                        var thanksJson = System.Text.Json.JsonSerializer.Serialize(thanksBody);
                                                        var thanksRequest = new HttpRequestMessage(HttpMethod.Post, url);
                                                        thanksRequest.Headers.Add("Authorization", $"Bearer {token}");
                                                        thanksRequest.Content = new StringContent(thanksJson, System.Text.Encoding.UTF8, "application/json");
                                                        var thanksResponse = await httpClient.SendAsync(thanksRequest);
                                                        var thanksRespContent = await thanksResponse.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Mensaje de agradecimiento de correo enviado a {waId}. Respuesta: {thanksRespContent}");

                                                        // Mensaje para describir la solicitud
                                                        var describeBody = new
                                                        {
                                                            messaging_product = "whatsapp",
                                                            recipient_type = "individual",
                                                            to = waId,
                                                            type = "text",
                                                            text = new { body = "Describe tu solicitud en un mensaje" }
                                                        };
                                                        var describeJson = System.Text.Json.JsonSerializer.Serialize(describeBody);
                                                        var describeRequest = new HttpRequestMessage(HttpMethod.Post, url);
                                                        describeRequest.Headers.Add("Authorization", $"Bearer {token}");
                                                        describeRequest.Content = new StringContent(describeJson, System.Text.Encoding.UTF8, "application/json");
                                                        var describeResponse = await httpClient.SendAsync(describeRequest);
                                                        var describeRespContent = await describeResponse.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Mensaje de solicitud de descripción enviado a {waId}. Respuesta: {describeRespContent}");
                                                        return Results.Ok();
                                                    }
                                                    // Si no es un correo, se asume que es la descripción de la solicitud
                                                    else if (!string.IsNullOrEmpty(userText))
                                                    {
                                                        // Guardar la descripción directamente en la tabla users
                                                        var updateDescCmd = connection2.CreateCommand();
                                                        updateDescCmd.CommandText = "UPDATE users SET DESCRIPCION = $desc WHERE wa_id = $wa_id;";
                                                        updateDescCmd.Parameters.AddWithValue("$wa_id", waId);
                                                        updateDescCmd.Parameters.AddWithValue("$desc", userText);
                                                        updateDescCmd.ExecuteNonQuery();

                                                        // Responder al usuario con confirmación
                                                        var caseMsgBody = new
                                                        {
                                                            messaging_product = "whatsapp",
                                                            recipient_type = "individual",
                                                            to = waId,
                                                            type = "text",
                                                            text = new { body = $"¡Tu solicitud ha sido registrada!\nPor favor, envía el documento necesario para tu solicitud (puedes adjuntar archivo PDF)." }
                                                        };
                                                        var caseMsgJson = System.Text.Json.JsonSerializer.Serialize(caseMsgBody);
                                                        var caseMsgRequest = new HttpRequestMessage(HttpMethod.Post, url);
                                                        caseMsgRequest.Headers.Add("Authorization", $"Bearer {token}");
                                                        caseMsgRequest.Content = new StringContent(caseMsgJson, System.Text.Encoding.UTF8, "application/json");
                                                        var caseMsgResponse = await httpClient.SendAsync(caseMsgRequest);
                                                        var caseMsgRespContent = await caseMsgResponse.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Mensaje de confirmación de registro enviado a {waId}. Respuesta: {caseMsgRespContent}");

                                                        // Esperar siguiente mensaje tipo documento
                                                        // ...existing code...
                                                        return Results.Ok();
                                                    }
                                                    // Si el mensaje recibido es un documento
                                                    else if (message.TryGetProperty("type", out var msgTypeProp) && msgTypeProp.GetString() == "document" && message.TryGetProperty("document", out var docObj))
                                                    {
                                                        var docUrl = docObj.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                                                        var fileName = docObj.TryGetProperty("filename", out var fileNameProp) ? fileNameProp.GetString() : null;
                                                        if (!string.IsNullOrEmpty(docUrl))
                                                        {
                                                            // Guardar el link en la base de datos
                                                            var updateDocCmd = connection2.CreateCommand();
                                                            updateDocCmd.CommandText = "UPDATE users SET DOCUMENT_URL = $docUrl WHERE wa_id = $wa_id;";
                                                            updateDocCmd.Parameters.AddWithValue("$docUrl", docUrl);
                                                            updateDocCmd.Parameters.AddWithValue("$wa_id", waId);
                                                            updateDocCmd.ExecuteNonQuery();

                                                            // Generar número de radicado (ejemplo: RAD-2026-00123)
                                                            string year = DateTime.Now.Year.ToString();
                                                            string radNum = "00123"; // Aquí podrías generar un consecutivo real
                                                            string radicado = $"RAD-{year}-{radNum}";

                                                            // Obtener datos del usuario para el resumen
                                                            var summaryCmd = connection2.CreateCommand();
                                                            summaryCmd.CommandText = "SELECT TIPO_SOLICITUD, NOMBRE, EMAIL, DESCRIPCION FROM users WHERE wa_id = $wa_id;";
                                                            summaryCmd.Parameters.AddWithValue("$wa_id", waId);
                                                            string tipoSolicitudSummary = "";
                                                            string nombreSummary = "";
                                                            string emailSummary = "";
                                                            string descripcionSummary = "";
                                                            using (var summaryReader = summaryCmd.ExecuteReader())
                                                            {
                                                                if (summaryReader.Read())
                                                                {
                                                                    tipoSolicitudSummary = summaryReader["TIPO_SOLICITUD"] as string ?? "";
                                                                    nombreSummary = summaryReader["NOMBRE"] as string ?? "";
                                                                    emailSummary = summaryReader["EMAIL"] as string ?? "";
                                                                    descripcionSummary = summaryReader["DESCRIPCION"] as string ?? "";
                                                                }
                                                            }

                                                            string resumen = $"\n\nResumen de tu solicitud:\n" +
                                                                $"• Nombre: {nombreSummary}\n" +
                                                                $"• Email: {emailSummary}\n" +
                                                                $"• Tipo: {tipoSolicitudSummary}\n" +
                                                                $"• Descripción: {descripcionSummary}";

                                                            // Responder con mensaje de radicado y resumen
                                                            var radicadoMsgBody = new
                                                            {
                                                                messaging_product = "whatsapp",
                                                                recipient_type = "individual",
                                                                to = waId,
                                                                type = "text",
                                                                text = new { body = $"✅ Tu solicitud fue registrada exitosamente.\n\nNúmero de radicado: {radicado}{resumen}\n\nUn funcionario continuará la atención por este mismo chat." }
                                                            };
                                                            var radicadoMsgJson = System.Text.Json.JsonSerializer.Serialize(radicadoMsgBody);
                                                            var radicadoMsgRequest = new HttpRequestMessage(HttpMethod.Post, url);
                                                            radicadoMsgRequest.Headers.Add("Authorization", $"Bearer {token}");
                                                            radicadoMsgRequest.Content = new StringContent(radicadoMsgJson, System.Text.Encoding.UTF8, "application/json");
                                                            var radicadoMsgResponse = await httpClient.SendAsync(radicadoMsgRequest);
                                                            var radicadoMsgRespContent = await radicadoMsgResponse.Content.ReadAsStringAsync();
                                                            Console.WriteLine($"Mensaje de radicado enviado a {waId}. Respuesta: {radicadoMsgRespContent}");
                                                            return Results.Ok();
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
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
