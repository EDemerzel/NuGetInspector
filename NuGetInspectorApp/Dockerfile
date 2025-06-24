# Optimized Dockerfile for containers
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project file and restore dependencies
COPY ["NuGetInspectorApp/NuGetInspectorApp.csproj", "NuGetInspectorApp/"]
# ✅ Restore with the target runtime identifier
RUN dotnet restore "NuGetInspectorApp/NuGetInspectorApp.csproj" --runtime linux-x64

# Copy source code and build
COPY . .
WORKDIR "/src/NuGetInspectorApp"

# ✅ Publish as self-contained for smaller runtime image
RUN dotnet publish "NuGetInspectorApp.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    --runtime linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=false

# ✅ Use minimal runtime-deps image for self-contained apps
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-alpine AS final
WORKDIR /app

# Create non-root user for security
RUN addgroup -g 1001 appuser && \
    adduser -u 1001 -G appuser -s /bin/sh -D appuser

# Copy the published application
COPY --from=build /app/publish .

# Set ownership and make executable
RUN chown -R appuser:appuser /app && \
    chmod +x ./NuGetInspectorApp

# Switch to non-root user
USER appuser

# ✅ Direct executable entry point for self-contained deployment
ENTRYPOINT ["./NuGetInspectorApp"]