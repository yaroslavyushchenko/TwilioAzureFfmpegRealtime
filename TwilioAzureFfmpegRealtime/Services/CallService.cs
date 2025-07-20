using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace TwilioAzureFfmpegRealtime.Services;

public class CallService
{
    private readonly ILogger<CallService> _logger;

    public CallService(ILogger<CallService> logger)
    {
        _logger = logger;
    }

    public async Task HandleTwilioMediaStream(WebSocket webSocket, Func<byte[], Task> handleAudio)
    {
        var buffer = new byte[1024 * 4];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessTwilioMessage(webSocket, message, handleAudio);
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed by client",
                        CancellationToken.None);
                    break;
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex, "WebSocket error");
        }
    }

    private async Task ProcessTwilioMessage(WebSocket webSocket, string message, Func<byte[], Task> handleAudio)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            if (!root.TryGetProperty("event", out var eventElement))
                return;

            var eventType = eventElement.GetString();

            switch (eventType)
            {
                case "connected":
                    await HandleConnected(webSocket, root);
                    break;
                case "start":
                    await HandleStart(webSocket, root);
                    break;
                case "media":
                    await HandleMedia(webSocket, root, handleAudio);
                    break;
                case "stop":
                    await HandleStop(webSocket, root);
                    break;
                default:
                    _logger.LogWarning("Unknown event: {EventType}", eventType);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error");
        }
    }

    private async Task HandleConnected(WebSocket webSocket, JsonElement root)
    {
        _logger.LogInformation("WebSocket connected to Twilio");

        if (root.TryGetProperty("protocol", out var protocol))
        {
            _logger.LogInformation("Protocol: {Protocol}", protocol.GetString());
        }

        if (root.TryGetProperty("version", out var version))
        {
            _logger.LogInformation("Version: {Version}", version.GetString());
        }
    }

    private async Task HandleStart(WebSocket webSocket, JsonElement root)
    {
        _logger.LogInformation("Media stream started");

        if (root.TryGetProperty("streamSid", out var streamSid))
        {
            _logger.LogInformation("Stream SID: {StreamSid}", streamSid.GetString());
        }

        if (root.TryGetProperty("start", out var start))
        {
            if (start.TryGetProperty("accountSid", out var accountSid))
                _logger.LogInformation("Account SID: {AccountSid}", accountSid.GetString());

            if (start.TryGetProperty("callSid", out var callSid))
                _logger.LogInformation("Call SID: {CallSid}", callSid.GetString());

            if (start.TryGetProperty("mediaFormat", out var mediaFormat))
            {
                if (mediaFormat.TryGetProperty("encoding", out var encoding))
                    _logger.LogInformation("Encoding: {Encoding}", encoding.GetString());

                if (mediaFormat.TryGetProperty("sampleRate", out var sampleRate))
                    _logger.LogInformation("Sample Rate: {SampleRate}", sampleRate.GetInt32());

                if (mediaFormat.TryGetProperty("channels", out var channels))
                    _logger.LogInformation("Channels: {Channels}", channels.GetInt32());
            }
        }
    }

    private async Task HandleMedia(WebSocket webSocket, JsonElement root, Func<byte[], Task> handleAudio)
    {
        if (root.TryGetProperty("media", out var media))
        {
            if (media.TryGetProperty("track", out var track))
                _logger.LogDebug("Track: {Track}", track.GetString());

            if (media.TryGetProperty("chunk", out var chunk))
                _logger.LogDebug("Chunk: {Chunk}", chunk.GetString());

            if (media.TryGetProperty("timestamp", out var timestamp))
                _logger.LogDebug("Timestamp: {Timestamp}", timestamp.GetString());

            if (media.TryGetProperty("payload", out var payload))
            {
                var payloadValue = payload.GetString();
                await ProcessAudioData(webSocket, payloadValue, media, handleAudio);
            }
        }
    }

    private async Task ProcessAudioData(WebSocket webSocket, string base64Audio, JsonElement media,
        Func<byte[], Task> handleAudio)
    {
        try
        {
            byte[] audioBytes = Convert.FromBase64String(base64Audio);
            _logger.LogDebug("Received audio data: {Length} bytes", audioBytes.Length);

            await handleAudio(audioBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio");
        }
    }

    private async Task HandleStop(WebSocket webSocket, JsonElement root)
    {
        _logger.LogInformation("Media stream stopped");

        if (root.TryGetProperty("streamSid", out var streamSid))
        {
            _logger.LogInformation("Stopped Stream SID: {StreamSid}", streamSid.GetString());
        }
    }

    private string GetStreamSid(JsonElement media)
    {
        if (media.TryGetProperty("streamSid", out var streamSid))
            return streamSid.GetString() ?? "";

        return "";
    }

    private string GetChunk(JsonElement media)
    {
        if (media.TryGetProperty("chunk", out var chunk))
            return chunk.GetString() ?? "";
        return "";
    }

    private string GetTimestamp(JsonElement media)
    {
        if (media.TryGetProperty("timestamp", out var timestamp))
            return timestamp.GetString() ?? "";

        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
    }

    private async Task SendMark(WebSocket webSocket, string streamSid, string markName)
    {
        var markMessage = new
        {
            @event = "mark",
            streamSid = streamSid,
            mark = new { name = markName }
        };

        var json = JsonSerializer.Serialize(markMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    private async Task ClearBuffer(WebSocket webSocket, string streamSid)
    {
        var clearMessage = new
        {
            @event = "clear",
            streamSid = streamSid
        };

        var json = JsonSerializer.Serialize(clearMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }
}