FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY SpeechAPITTS/ .
RUN dotnet publish -c Release --self-contained true --runtime win-x86 -o out

FROM scottyhardy/docker-wine:latest
WORKDIR /app
COPY out/ SpeechAPITTS/ --from=build
CMD ["wine", "SpeechAPITTS.exe"]
