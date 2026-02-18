# Use Microsoft's official Alpine-based .NET runtime image (most lightweight)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Use SDK Alpine image for build
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files
COPY ["MetaAnime.Presentation/MetaAnime.Presentation.csproj", "MetaAnime.Presentation/"]
COPY ["MetaAnime.Application/MetaAnime.Application.csproj", "MetaAnime.Application/"]
COPY ["MetaAnime.Core/MetaAnime.Core.csproj", "MetaAnime.Core/"]
COPY ["MetaAnime.Infrastructure/MetaAnime.Infrastructure.csproj", "MetaAnime.Infrastructure/"]
COPY ["MetaAnime.Infrastructure.Kometa/MetaAnime.Infrastructure.Kometa.csproj", "MetaAnime.Infrastructure.Kometa/"]
COPY ["MetaAnime.Infrastructure.MyAnimeList/MetaAnime.Infrastructure.MyAnimeList.csproj", "MetaAnime.Infrastructure.MyAnimeList/"]
COPY ["MetaAnime.Infrastructure.Plex/MetaAnime.Infrastructure.Plex.csproj", "MetaAnime.Infrastructure.Plex/"]
COPY ["MetaAnime.Infrastructure.Sonarr/MetaAnime.Infrastructure.Sonarr.csproj", "MetaAnime.Infrastructure.Sonarr/"]

# Restore dependencies
RUN dotnet restore "MetaAnime.Presentation/MetaAnime.Presentation.csproj"

# Copy the rest of the source code
COPY . .

# Build the application
WORKDIR "/src/MetaAnime.Presentation"
RUN dotnet build "MetaAnime.Presentation.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish the application
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "MetaAnime.Presentation.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage - runtime
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create a non-root user for security
RUN addgroup -g 1000 appuser && adduser -u 1000 -G appuser -s /bin/sh -D appuser
USER appuser

ENTRYPOINT ["dotnet", "MetaAnime.Presentation.dll"]
