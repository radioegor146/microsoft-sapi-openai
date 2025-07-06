FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY SpeechAPITTS/ .
RUN dotnet publish -c Release --self-contained true --runtime win-x86 -o out

FROM scottyhardy/docker-wine:latest
WORKDIR /app
COPY --from=build /app/out/ SpeechAPITTS/
RUN wine reg delete "HKLM\\Software\\Microsoft\\Speech\\Voices\\Tokens\\Wine Default Voice" /f
RUN mkdir voices/
# RUN wget https://microsoft-sapi-openai-voices.re146.dev/maxim-lite-x86.exe -O voices/maxim-lite-x86.exe && wine voices/maxim-lite-x86.exe /VERYSILENT /SP-
# RUN wget https://microsoft-sapi-openai-voices.re146.dev/tatyana-lite-x86.exe -O voices/tatyana-lite-x86.exe && wine voices/tatyana-lite-x86.exe /VERYSILENT /SP-
RUN rm -rf voices/
EXPOSE 8000
CMD ["wine", "SpeechAPITTS/SpeechAPITTS.exe"]
