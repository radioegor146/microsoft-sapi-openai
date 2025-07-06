FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY SpeechAPITTS/ .
RUN dotnet publish -c Release --self-contained true --runtime win-x86 -o out

FROM scottyhardy/docker-wine:latest
WORKDIR /app
COPY --from=build /app/out/ SpeechAPITTS/
RUN groupadd --gid 1010 wineuser && useradd --shell /bin/bash --uid 1010 --gid 1010 wineuser && mkdir /home/wineuser
USER wineuser
RUN wine reg delete "HKLM\\Software\\Microsoft\\Speech\\Voices\\Tokens\\Wine Default Voice" /f
RUN mkdir /tmp/voices
RUN wget https://microsoft-sapi-openai-voices.re146.dev/s2g-tatyana-x86.exe -O /tmp/voices/s2g-tatyana-x86.exe && wine /tmp/voices/s2g-tatyana-x86.exe /S /NCRC
RUN wget https://microsoft-sapi-openai-voices.re146.dev/s2g-maxim-x86.exe -O /tmp/voices/s2g-maxim-x86.exe && wine /tmp/voices/s2g-maxim-x86.exe /S /NCRC
RUN rm -rf /tmp/voices/
EXPOSE 8000
CMD ["wine", "SpeechAPITTS/SpeechAPITTS.exe"]
