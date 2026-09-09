FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "."]
COPY ["src/JobTracker.Application/JobTracker.Application.csproj", "src/JobTracker.Application/"]
COPY ["src/JobTracker.BackgroundJob/JobTracker.BackgroundJob.csproj", "src/JobTracker.BackgroundJob/"]
COPY ["src/JobTracker.Domain/JobTracker.Domain.csproj", "src/JobTracker.Domain/"]
COPY ["src/JobTracker.Jobs.IntegrationEvents/JobTracker.Jobs.IntegrationEvents.csproj", "src/JobTracker.Jobs.IntegrationEvents/"]
COPY ["src/JobTracker.Persistence/JobTracker.Persistence.csproj", "src/JobTracker.Persistence/"]
RUN dotnet restore "./src/JobTracker.BackgroundJob/JobTracker.BackgroundJob.csproj"
COPY . .
WORKDIR "/src/src/JobTracker.BackgroundJob"
RUN dotnet build "./JobTracker.BackgroundJob.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./JobTracker.BackgroundJob.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "JobTracker.BackgroundJob.dll"]
