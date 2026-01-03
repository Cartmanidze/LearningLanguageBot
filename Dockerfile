FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY LearningLanguageBot.sln .
COPY src/LearningLanguageBot/LearningLanguageBot.csproj src/LearningLanguageBot/

# Restore packages
RUN dotnet restore

# Copy source code
COPY src/ src/

# Build and publish
RUN dotnet publish src/LearningLanguageBot/LearningLanguageBot.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "LearningLanguageBot.dll"]
