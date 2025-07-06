#!/bin/bash
if [ ! -f /home/wineuser/voices-ready ]; then
    wine reg delete "HKLM\\Software\\Microsoft\\Speech\\Voices\\Tokens\\Wine Default Voice" /f
    mkdir /tmp/voices
    wget https://microsoft-sapi-openai-voices.re146.dev/s2g-tatyana-x86.exe -O /tmp/voices/s2g-tatyana-x86.exe && wine /tmp/voices/s2g-tatyana-x86.exe /S /NCRC
    wget https://microsoft-sapi-openai-voices.re146.dev/s2g-maxim-x86.exe -O /tmp/voices/s2g-maxim-x86.exe && wine /tmp/voices/s2g-maxim-x86.exe /S /NCRC
    rm -rf /tmp/voices/
    touch /home/wineuser/voices-ready
fi
wine /app/SpeechAPITTS/SpeechAPITTS.exe