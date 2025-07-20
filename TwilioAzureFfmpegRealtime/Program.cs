using TwilioAzureFfmpegRealtime.Models.Options;
using TwilioAzureFfmpegRealtime.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.Configure<AzureSpeechOptions>(
    builder.Configuration.GetSection("AzureSpeechOptions"));

// TODO: TO Singleton and stateless
builder.Services.AddScoped<RealtimeAudioConverter>();
builder.Services.AddSingleton<CallService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseWebSockets();
app.MapControllers();

app.Run();