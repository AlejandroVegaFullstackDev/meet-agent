# meetagent-stt en contenedor: STT compatible con OpenAI sobre Whisper local, para el bot de Vexa.
# Se construye desde la salida de `dotnet publish` (ver up.sh); el modelo se monta en /meetagent/models.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# ggml (whisper.cpp) usa OpenMP; la imagen base no trae libgomp.
RUN apt-get update && apt-get install -y --no-install-recommends libgomp1 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY . .
ENV MEETAGENT_HOME=/meetagent \
    ASPNETCORE_URLS=http://0.0.0.0:8083 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8083
USER app
ENTRYPOINT ["dotnet", "meetagent-stt.dll"]
