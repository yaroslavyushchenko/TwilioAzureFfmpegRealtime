using System.Diagnostics;

namespace TwilioAzureFfmpegRealtime.Services;

public class RealtimeAudioConverter : IDisposable
{
    private readonly Process _ffmpegProcess;
    private readonly MemoryStream _buffer = new();
    private const int BufferThreshold = 512;
    private readonly ILogger<RealtimeAudioConverter> _logger;

    public RealtimeAudioConverter(ILogger<RealtimeAudioConverter> logger)
    {
        _logger = logger;
        _ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    "-loglevel error -fflags nobuffer -avioflags direct -fflags discardcorrupt -probesize 32 -analyzeduration 0 -f mulaw -ar 8000 -ac 1 -i pipe:0 -ar 16000 -acodec pcm_s16le -f s16le pipe:1",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _ffmpegProcess.Start();

        Task.Run(() =>
        {
            using var reader = _ffmpegProcess.StandardError;
            while (reader.ReadLine() is { } line)
            {
                Console.Error.WriteLine(line);
            }
        });
    }

    public async Task<byte[]> WriteChunkAsync(byte[] chunk)
    {
        if (_ffmpegProcess == null)
            throw new InvalidOperationException("FFmpeg process is not started.");

        _buffer.Write(chunk, 0, chunk.Length);
        if (_buffer.Length < BufferThreshold) return [];
        var dataToSend = _buffer.ToArray();
        _buffer.SetLength(0);

        await _ffmpegProcess.StandardInput.BaseStream.WriteAsync(dataToSend, 0, dataToSend.Length);
        await _ffmpegProcess.StandardInput.BaseStream.FlushAsync();

        var buffer = new byte[4096];
        using var memoryStream = new MemoryStream();
        var bytesRead = await _ffmpegProcess.StandardOutput.BaseStream.ReadAsync(buffer, 0, buffer.Length);

        if (bytesRead > 0)
        {
            memoryStream.Write(buffer, 0, bytesRead);
        }

        return memoryStream.ToArray();
    }

    public void Dispose()
    {
        try
        {
            _buffer.Close();
            _ffmpegProcess.StandardInput.Close();

            while (!_ffmpegProcess.StandardOutput.EndOfStream)
            {
                _ffmpegProcess.StandardOutput.ReadLine();
            }

            _ffmpegProcess.WaitForExit();
        }
        finally
        {
            _ffmpegProcess.Dispose();
        }
    }
}