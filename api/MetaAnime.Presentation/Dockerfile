# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["MetaAnime.Presentation/MetaAnime.Presentation.csproj", "MetaAnime.Presentation/"]
COPY ["MetaAnime.Application/MetaAnime.Application.csproj", "MetaAnime.Application/"]
COPY ["MetaAnime.Core/MetaAnime.Core.csproj", "MetaAnime.Core/"]
COPY ["MetaAnime.Infrastructure.Kometa/MetaAnime.Infrastructure.Kometa.csproj", "MetaAnime.Infrastructure.Kometa/"]
COPY ["MetaAnime.Infrastructure.MyAnimeList/MetaAnime.Infrastructure.MyAnimeList.csproj", "MetaAnime.Infrastructure.MyAnimeList/"]
COPY ["MetaAnime.Infrastructure.Plex/MetaAnime.Infrastructure.Plex.csproj", "MetaAnime.Infrastructure.Plex/"]
COPY ["MetaAnime.Infrastructure.Sonarr/MetaAnime.Infrastructure.Sonarr.csproj", "MetaAnime.Infrastructure.Sonarr/"]
COPY ["MetaAnime.Infrastructure/MetaAnime.Infrastructure.csproj", "MetaAnime.Infrastructure/"]
RUN dotnet restore "./MetaAnime.Presentation/MetaAnime.Presentation.csproj"
COPY . .
WORKDIR "/src/MetaAnime.Presentation"
RUN dotnet build "./MetaAnime.Presentation.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MetaAnime.Presentation.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MetaAnime.Presentation.dll"]