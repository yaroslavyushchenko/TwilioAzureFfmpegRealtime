# TwilioAzureFfmpegRealtime

**Realtime Twilio Audio Transformation for Azure Speech Recognition using FFmpeg in .NET**

---

## Overview

This project demonstrates a complete real-time pipeline that:

- Receives live Twilio call callbacks (webhooks)
- Establishes a WebSocket connection for streaming raw audio from Twilio
- Converts Twilio's raw mu-law audio to 16-bit PCM WAV on-the-fly using FFmpeg as an external process
- Streams the converted audio directly into Azure Speech-to-Text for real-time transcription

No heavy .NET audio libraries are used—FFmpeg handles high-quality audio conversion with minimal latency.

---

## How It Works

1. Twilio sends a webhook POST request to `/callback/calls/voice` on incoming calls.
2. The app responds with TwiML directing Twilio to open a WebSocket connection to `/stream`.
3. Twilio streams 8 kHz, 8-bit mu-law audio to the WebSocket.
4. The app converts the audio using FFmpeg subprocess to 16-bit PCM WAV.
5. Converted audio chunks are fed into Azure Speech SDK's `PushAudioInputStream` for continuous recognition.
6. Recognition results are logged and can be extended for custom business logic.

---

## Setup

1. **Azure Speech Configuration**

Configure your Azure Speech subscription key and region in `appsettings.json` or environment variables:

```json
{
  "AzureSpeechOptions": {
    "SubscriptionKey": "your-azure-subscription-key",
    "Region": "your-azure-region"
  }
}
