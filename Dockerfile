FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY LearningLanguageBot.sln .
COPY src/LearningLanguageBot.Bot/LearningLanguageBot.Bot.csproj src/LearningLanguageBot.Bot/
COPY src/LearningLanguageBot.Core/LearningLanguageBot.Core.csproj src/LearningLanguageBot.Core/
COPY src/LearningLanguageBot.Infrastructure/LearningLanguageBot.Infrastructure.csproj src/LearningLanguageBot.Infrastructure/
COPY src/LearningLanguageBot.Shared/LearningLanguageBot.Shared.csproj src/LearningLanguageBot.Shared/

# Restore packages
RUN dotnet restore

# Copy source code
COPY src/ src/

# Build and publish
RUN dotnet publish src/LearningLanguageBot.Bot/LearningLanguageBot.Bot.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "LearningLanguageBot.Bot.dll"]
