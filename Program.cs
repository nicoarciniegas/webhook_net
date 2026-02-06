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

// Endpoint de prueba: intentar enviar PDF como base64 directo a Meta
app.MapGet("/test-base64/{telefono}", async (string telefono) =>
{
    var token = "EAAmNsGBlnEMBQjePsO5AgHXIJZCSoIbmRgUjMkmJYrVZCQ86Lpna6dyeKX67wxhCvkaptnGAHHqHHhtZBljhJjl2KuXpX0wo96cZAZBVMoV9QtGgNByfbZCYQmmJPKRykjRTUjmw4yyKZAk2x7E632bISp187jrlYxP6MSyarBE19rfYJnYNLxsPTAeLRpf1498mAZDZD";
    var url = "https://graph.facebook.com/v23.0/976270252240458/messages";
    var pdfPath = "Manual despliegue Whatsapp api cloud.pdf";
    
    if (!File.Exists(pdfPath))
    {
        return Results.Json(new { error = "PDF no encontrado", path = pdfPath });
    }
    
    // Leer y convertir a base64
    var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
    var pdfBase64 = Convert.ToBase64String(pdfBytes);
    
    Console.WriteLine($"PDF: {pdfBytes.Length} bytes → {pdfBase64.Length} caracteres base64");
    
    // Intento 1: Enviar base64 como "data URL" en link
    var httpClient = new HttpClient();
    var testBody1 = new
    {
        messaging_product = "whatsapp",
        recipient_type = "individual",
        to = telefono,
        type = "document",
        document = new
        {
            link = $"data:application/pdf;base64,{pdfBase64}",
            filename = "Manual_WhatsApp.pdf",
            caption = "Prueba base64 como data URL"
        }
    };
    
    var json1 = System.Text.Json.JsonSerializer.Serialize(testBody1);
    var req1 = new HttpRequestMessage(HttpMethod.Post, url);
    req1.Headers.Add("Authorization", $"Bearer {token}");
    req1.Content = new StringContent(json1, System.Text.Encoding.UTF8, "application/json");
    
    var resp1 = await httpClient.SendAsync(req1);
    var result1 = await resp1.Content.ReadAsStringAsync();
    
    // Intento 2: Enviar base64 como campo "data" inventado
    var testBody2 = new
    {
        messaging_product = "whatsapp",
        recipient_type = "individual",
        to = telefono,
        type = "document",
        document = new
        {
            data = pdfBase64,
            mime_type = "application/pdf",
            filename = "Manual_WhatsApp.pdf",
            caption = "Prueba base64 como campo data"
        }
    };
    
    var json2 = System.Text.Json.JsonSerializer.Serialize(testBody2);
    var req2 = new HttpRequestMessage(HttpMethod.Post, url);
    req2.Headers.Add("Authorization", $"Bearer {token}");
    req2.Content = new StringContent(json2, System.Text.Encoding.UTF8, "application/json");
    
    var resp2 = await httpClient.SendAsync(req2);
    var result2 = await resp2.Content.ReadAsStringAsync();
    
    return Results.Json(new
    {
        mensaje = "Pruebas de envío base64 directo a Meta API",
        intento1_dataUrl = new { status = (int)resp1.StatusCode, response = result1 },
        intento2_campoData = new { status = (int)resp2.StatusCode, response = result2 },
        conclusion = "Meta API no soporta base64 directo - requiere upload a /media primero"
    });
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
                            // Buscar el mensaje recibido (puede ser texto, imagen o documento)
                            if (valueObj.TryGetProperty("messages", out var messagesArray) && messagesArray.GetArrayLength() > 0)
                            {
                                var message = messagesArray[0];
                                
                                // Obtener tipo de mensaje
                                var messageType = message.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                                
                                // Procesar documento o imagen
                                if ((messageType == "document" || messageType == "image") && 
                                    message.TryGetProperty(messageType, out var mediaObj))
                                {
                                    if (mediaObj.TryGetProperty("id", out var mediaIdProp) && 
                                        mediaObj.TryGetProperty("mime_type", out var mimeTypeProp))
                                    {
                                        var mediaId = mediaIdProp.GetString();
                                        var mimeType = mimeTypeProp.GetString();
                                        var fileName = mediaObj.TryGetProperty("filename", out var fileNameProp) 
                                            ? fileNameProp.GetString() 
                                            : $"file_{DateTime.Now:yyyyMMddHHmmss}";
                                        
                                        Console.WriteLine($"Recibido {messageType}: {fileName} ({mimeType})");
                                        
                                        // 1. Obtener URL del archivo desde Meta API
                                        var mediaUrl = $"https://graph.facebook.com/v23.0/{mediaId}";
                                        var mediaRequest = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
                                        mediaRequest.Headers.Add("Authorization", $"Bearer {token}");
                                        
                                        try
                                        {
                                            var mediaResponse = await httpClient.SendAsync(mediaRequest);
                                            var mediaJson = await mediaResponse.Content.ReadAsStringAsync();
                                            var mediaDoc = System.Text.Json.JsonDocument.Parse(mediaJson);
                                            
                                            if (mediaDoc.RootElement.TryGetProperty("url", out var urlProp))
                                            {
                                                var fileUrl = urlProp.GetString();
                                                Console.WriteLine($"URL del archivo obtenida: {fileUrl}");
                                                
                                                // 2. Descargar el archivo usando el token
                                                var downloadRequest = new HttpRequestMessage(HttpMethod.Get, fileUrl);
                                                downloadRequest.Headers.Add("Authorization", $"Bearer {token}");
                                                
                                                var downloadResponse = await httpClient.SendAsync(downloadRequest);
                                                var fileBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
                                                
                                                // 3. Guardar archivo localmente
                                                var uploadsDir = "uploads";
                                                if (!Directory.Exists(uploadsDir))
                                                {
                                                    Directory.CreateDirectory(uploadsDir);
                                                }
                                                
                                                var localFilePath = Path.Combine(uploadsDir, $"{waId}_{fileName}");
                                                await File.WriteAllBytesAsync(localFilePath, fileBytes);
                                                Console.WriteLine($"Archivo guardado en: {localFilePath} ({fileBytes.Length} bytes)");
                                                
                                                // 4. Guardar URL en base de datos
                                                using (var connection2 = new SqliteConnection($"Data Source={dbPath}"))
                                                {
                                                    connection2.Open();
                                                    var updateDocCmd = connection2.CreateCommand();
                                                    updateDocCmd.CommandText = "UPDATE users SET DOCUMENT_URL = $docUrl WHERE wa_id = $wa_id;";
                                                    updateDocCmd.Parameters.AddWithValue("$docUrl", localFilePath);
                                                    updateDocCmd.Parameters.AddWithValue("$wa_id", waId);
                                                    updateDocCmd.ExecuteNonQuery();
                                                }
                                                
                                                // 5. Confirmar recepción al usuario
                                                var confirmDocBody = new
                                                {
                                                    messaging_product = "whatsapp",
                                                    recipient_type = "individual",
                                                    to = waId,
                                                    type = "text",
                                                    text = new { body = $"✅ Documento recibido: {fileName}\nTu solicitud está completa. Un agente te contactará pronto." }
                                                };
                                                var confirmDocJson = System.Text.Json.JsonSerializer.Serialize(confirmDocBody);
                                                var confirmDocRequest = new HttpRequestMessage(HttpMethod.Post, url);
                                                confirmDocRequest.Headers.Add("Authorization", $"Bearer {token}");
                                                confirmDocRequest.Content = new StringContent(confirmDocJson, System.Text.Encoding.UTF8, "application/json");
                                                await httpClient.SendAsync(confirmDocRequest);
                                                
                                                // 6. Enviar imagen de confirmación (IMG_9066.JPEG)
                                                try
                                                {
                                                    var imagePath = "IMG_9066.JPEG";
                                                    if (File.Exists(imagePath))
                                                    {
                                                        var imageBytes = await File.ReadAllBytesAsync(imagePath);
                                                        Console.WriteLine($"Enviando imagen de confirmación ({imageBytes.Length} bytes)...");
                                                        
                                                        // Subir imagen a Meta
                                                        var uploadUrl = $"https://graph.facebook.com/v23.0/976270252240458/media";
                                                        var uploadContent = new MultipartFormDataContent();
                                                        uploadContent.Add(new StringContent("whatsapp"), "messaging_product");
                                                        
                                                        var fileContent = new ByteArrayContent(imageBytes);
                                                        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                                                        uploadContent.Add(fileContent, "file", "confirmacion.jpeg");
                                                        
                                                        var uploadReq = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                                                        uploadReq.Headers.Add("Authorization", $"Bearer {token}");
                                                        uploadReq.Content = uploadContent;
                                                        
                                                        var uploadResp = await httpClient.SendAsync(uploadReq);
                                                        var uploadJsonStr = await uploadResp.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Respuesta de subida de imagen: {uploadJsonStr}");
                                                        
                                                        var mediaIdDoc = System.Text.Json.JsonDocument.Parse(uploadJsonStr);
                                                        var uploadedMediaId = mediaIdDoc.RootElement.GetProperty("id").GetString();
                                                        
                                                        // Enviar imagen al usuario
                                                        var sendImageBody = new
                                                        {
                                                            messaging_product = "whatsapp",
                                                            recipient_type = "individual",
                                                            to = waId,
                                                            type = "image",
                                                            image = new 
                                                            { 
                                                                id = uploadedMediaId,
                                                                caption = "Gracias por tu documento. Aquí está tu confirmación visual."
                                                            }
                                                        };
                                                        
                                                        var sendImageJson = System.Text.Json.JsonSerializer.Serialize(sendImageBody);
                                                        var sendImageReq = new HttpRequestMessage(HttpMethod.Post, url);
                                                        sendImageReq.Headers.Add("Authorization", $"Bearer {token}");
                                                        sendImageReq.Content = new StringContent(sendImageJson, System.Text.Encoding.UTF8, "application/json");
                                                        var sendImageResp = await httpClient.SendAsync(sendImageReq);
                                                        var sendImageRespContent = await sendImageResp.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Imagen enviada a {waId}. Respuesta: {sendImageRespContent}");
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine($"Advertencia: No se encontró la imagen {imagePath}");
                                                    }
                                                }
                                                catch (Exception imgEx)
                                                {
                                                    Console.WriteLine($"Error enviando imagen de confirmación: {imgEx.Message}");
                                                }
                                                
                                                // 7. PRUEBA: Intentar enviar PDF como base64 directo (fallará)
                                                try
                                                {
                                                    var pdfPath = "Manual despliegue Whatsapp api cloud.pdf";
                                                    if (File.Exists(pdfPath))
                                                    {
                                                        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
                                                        var pdfBase64 = Convert.ToBase64String(pdfBytes);
                                                        Console.WriteLine($"\n=== PRUEBA BASE64 DIRECTO ===");
                                                        Console.WriteLine($"PDF: {pdfBytes.Length} bytes → {pdfBase64.Length} caracteres base64");
                                                        
                                                        // Intento 1: data URL
                                                        var testBody1 = new
                                                        {
                                                            messaging_product = "whatsapp",
                                                            recipient_type = "individual",
                                                            to = waId,
                                                            type = "document",
                                                            document = new
                                                            {
                                                                link = $"data:application/pdf;base64,{pdfBase64}",
                                                                filename = "Manual_Base64.pdf",
                                                                caption = "Prueba base64 como data URL"
                                                            }
                                                        };
                                                        var json1 = System.Text.Json.JsonSerializer.Serialize(testBody1);
                                                        var req1 = new HttpRequestMessage(HttpMethod.Post, url);
                                                        req1.Headers.Add("Authorization", $"Bearer {token}");
                                                        req1.Content = new StringContent(json1, System.Text.Encoding.UTF8, "application/json");
                                                        var resp1 = await httpClient.SendAsync(req1);
                                                        var result1 = await resp1.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Intento 1 (data URL): {resp1.StatusCode} - {result1}");
                                                        
                                                        // Intento 2: campo "data" inventado
                                                        var testBody2 = new
                                                        {
                                                            messaging_product = "whatsapp",
                                                            recipient_type = "individual",
                                                            to = waId,
                                                            type = "document",
                                                            document = new
                                                            {
                                                                data = pdfBase64,
                                                                mime_type = "application/pdf",
                                                                filename = "Manual_Base64.pdf"
                                                            }
                                                        };
                                                        var json2 = System.Text.Json.JsonSerializer.Serialize(testBody2);
                                                        var req2 = new HttpRequestMessage(HttpMethod.Post, url);
                                                        req2.Headers.Add("Authorization", $"Bearer {token}");
                                                        req2.Content = new StringContent(json2, System.Text.Encoding.UTF8, "application/json");
                                                        var resp2 = await httpClient.SendAsync(req2);
                                                        var result2 = await resp2.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Intento 2 (campo data): {resp2.StatusCode} - {result2}");
                                                        Console.WriteLine($"=== FIN PRUEBA BASE64 (ambos fallan como esperado) ===\n");
                                                        
                                                        // 8. Ahora sí, enviar correctamente con upload a Meta
                                                        Console.WriteLine($"Enviando PDF del manual correctamente ({pdfBytes.Length} bytes)...");
                                                        
                                                        // Subir PDF a Meta
                                                        var uploadPdfUrl = $"https://graph.facebook.com/v23.0/976270252240458/media";
                                                        var uploadPdfContent = new MultipartFormDataContent();
                                                        uploadPdfContent.Add(new StringContent("whatsapp"), "messaging_product");
                                                        
                                                        var pdfContent = new ByteArrayContent(pdfBytes);
                                                        pdfContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                                                        uploadPdfContent.Add(pdfContent, "file", "Manual_WhatsApp.pdf");
                                                        
                                                        var uploadPdfReq = new HttpRequestMessage(HttpMethod.Post, uploadPdfUrl);
                                                        uploadPdfReq.Headers.Add("Authorization", $"Bearer {token}");
                                                        uploadPdfReq.Content = uploadPdfContent;
                                                        
                                                        var uploadPdfResp = await httpClient.SendAsync(uploadPdfReq);
                                                        var uploadPdfJsonStr = await uploadPdfResp.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Respuesta de subida de PDF: {uploadPdfJsonStr}");
                                                        
                                                        var pdfMediaDoc = System.Text.Json.JsonDocument.Parse(uploadPdfJsonStr);
                                                        if (pdfMediaDoc.RootElement.TryGetProperty("id", out var pdfMediaIdProp))
                                                        {
                                                            var pdfMediaId = pdfMediaIdProp.GetString();
                                                            
                                                            // Enviar documento al usuario
                                                            var sendPdfBody = new
                                                            {
                                                                messaging_product = "whatsapp",
                                                                recipient_type = "individual",
                                                                to = waId,
                                                                type = "document",
                                                                document = new 
                                                                { 
                                                                    id = pdfMediaId,
                                                                    caption = "📄 Aquí tienes el manual de referencia.",
                                                                    filename = "Manual_WhatsApp_API.pdf"
                                                                }
                                                            };
                                                            
                                                            var sendPdfJson = System.Text.Json.JsonSerializer.Serialize(sendPdfBody);
                                                            var sendPdfReq = new HttpRequestMessage(HttpMethod.Post, url);
                                                            sendPdfReq.Headers.Add("Authorization", $"Bearer {token}");
                                                            sendPdfReq.Content = new StringContent(sendPdfJson, System.Text.Encoding.UTF8, "application/json");
                                                            var sendPdfResp = await httpClient.SendAsync(sendPdfReq);
                                                            var sendPdfRespContent = await sendPdfResp.Content.ReadAsStringAsync();
                                                            Console.WriteLine($"PDF enviado a {waId}. Respuesta: {sendPdfRespContent}");
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine($"Advertencia: No se encontró el PDF {pdfPath}");
                                                    }
                                                }
                                                catch (Exception pdfEx)
                                                {
                                                    Console.WriteLine($"Error enviando PDF: {pdfEx.Message}");
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Error procesando archivo: {ex.Message}");
                                        }
                                    }
                                }
                                // Procesar mensaje de texto
                                else if (message.TryGetProperty("text", out var textObj) && textObj.TryGetProperty("body", out var bodyProp))
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
                                                            text = new { body = $"¡Tu solicitud ha sido registrada!\nPor favor, envía el documento necesario para tu solicitud (puedes adjuntar archivo PDF, imagen, etc.)." }
                                                        };
                                                        var caseMsgJson = System.Text.Json.JsonSerializer.Serialize(caseMsgBody);
                                                        var caseMsgRequest = new HttpRequestMessage(HttpMethod.Post, url);
                                                        caseMsgRequest.Headers.Add("Authorization", $"Bearer {token}");
                                                        caseMsgRequest.Content = new StringContent(caseMsgJson, System.Text.Encoding.UTF8, "application/json");
                                                        var caseMsgResponse = await httpClient.SendAsync(caseMsgRequest);
                                                        var caseMsgRespContent = await caseMsgResponse.Content.ReadAsStringAsync();
                                                        Console.WriteLine($"Mensaje de confirmación de registro enviado a {waId}. Respuesta: {caseMsgRespContent}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                } // Cierre del else if de mensaje de texto
                            } // Cierre del if de messagesArray
                        } // Cierre del else (no es primer mensaje)
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error procesando webhook: {ex.Message}");
    }

    return Results.Ok();
});

// Log startup
Console.WriteLine($"\nListening on port {port}\n");

// Start the server
app.Run();
