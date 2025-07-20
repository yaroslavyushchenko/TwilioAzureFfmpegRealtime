using Microsoft.AspNetCore.Mvc;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using TwilioAzureFfmpegRealtime.Models.Options;
using TwilioAzureFfmpegRealtime.Services;
using Task = System.Threading.Tasks.Task;

namespace TwilioAzureFfmpegRealtime.Controllers;

[ApiController]
[Route("callback/[controller]")]
public class CallsController : ControllerBase
{
    private readonly AzureSpeechOptions _speechOptions;
    private readonly CallService _callService;
    private readonly RealtimeAudioConverter _audioConverter;
    private readonly ILogger<CallsController> _logger;

    public CallsController(
        IOptions<AzureSpeechOptions> options,
        CallService callService,
        RealtimeAudioConverter audioConverter,
        ILogger<CallsController> logger)
    {
        _speechOptions = options.Value;
        _callService = callService;
        _audioConverter = audioConverter;
        _logger = logger;
    }
    
    [HttpPost("voice")]
    public IActionResult VoiceCall()
    {
        var response = new VoiceResponse();
        var connect = new Connect();

        // Create WebSocket stream connection to our /stream endpoint
        var stream = new Twilio.TwiML.Voice.Stream(url: $"wss://{Request.Host}/stream/");
        connect.Append(stream);
        response.Append(connect);

        return Content(response.ToString(), "text/xml");
    }

    [HttpGet("/stream")]
    public async Task StreamHandler()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = 400;
            return;
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        using var audioStream = new PushAudioInputStream();
        using var recognizer = CreateSpeechRecognizer(audioStream);

        SetupRecognitionHandlers(recognizer);
        await recognizer.StartContinuousRecognitionAsync();

        try
        {
            // Process incoming audio from Twilio WebSocket
            await _callService.HandleTwilioMediaStream(webSocket, async (audioBytes) =>
            {
                // Convert audio format and write to Azure Speech stream
                var convertedAudio = await _audioConverter.WriteChunkAsync(audioBytes);
                if (convertedAudio.Length == 0) return;
                audioStream.Write(convertedAudio);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio stream");
        }
        finally
        {
            await recognizer.StopContinuousRecognitionAsync();
        }
    }

    private SpeechRecognizer CreateSpeechRecognizer(PushAudioInputStream audioStream)
    {
        var speechConfig = SpeechConfig.FromSubscription(_speechOptions.SubscriptionKey, _speechOptions.Region);

        speechConfig.SetProperty(PropertyId.SpeechServiceResponse_PostProcessingOption, "TrueText");
        speechConfig.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "3000");
        speechConfig.SetProfanity(ProfanityOption.Raw);

        var audioConfig = AudioConfig.FromStreamInput(audioStream);
        return new SpeechRecognizer(speechConfig, audioConfig);
    }

    private void SetupRecognitionHandlers(SpeechRecognizer recognizer)
    {
        recognizer.Recognizing += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Result.Text))
            {
                _logger.LogInformation("Recognizing: {Text}", e.Result.Text);
            }
        };

        recognizer.Recognized += (sender, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech)
            {
                _logger.LogInformation("Final result: {Text}", e.Result.Text);

                // TODO: Add your business logic here
                // Example: Save to database, trigger actions, etc.
            }
        };

        recognizer.Canceled += (sender, e) => { _logger.LogWarning("Recognition canceled: {Reason}", e.Reason); };
    }
}