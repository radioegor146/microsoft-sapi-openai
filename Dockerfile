FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY SpeechAPITTS/ .
RUN dotnet publish -c Release --self-contained true --runtime win-x86 -o out

FROM scottyhardy/docker-wine:latest
WORKDIR /app
COPY --from=build /app/out/ SpeechAPITTS/
COPY main.sh main.sh
EXPOSE 8000
CMD ["/bin/bash", "main.sh"]
