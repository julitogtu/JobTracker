# Applies the EF Core migrations, then exits. `docker compose up` runs it once before the API and
# the background host start, because neither of them migrates on boot.
#
# The migrations are baked into an `efbundle` executable at image build time, so the runtime image
# needs neither the SDK nor the source tree.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN dotnet tool install --global dotnet-ef --version 10.0.*
ENV PATH="$PATH:/root/.dotnet/tools"

COPY ["Directory.Packages.props", "."]
COPY ["src/JobTracker.Application/JobTracker.Application.csproj", "src/JobTracker.Application/"]
COPY ["src/JobTracker.Domain/JobTracker.Domain.csproj", "src/JobTracker.Domain/"]
COPY ["src/JobTracker.Jobs.IntegrationEvents/JobTracker.Jobs.IntegrationEvents.csproj", "src/JobTracker.Jobs.IntegrationEvents/"]
COPY ["src/JobTracker.Persistence/JobTracker.Persistence.csproj", "src/JobTracker.Persistence/"]
RUN dotnet restore "./src/JobTracker.Persistence/JobTracker.Persistence.csproj"
COPY . .

# JobsDbContextFactory is the design-time context, so Persistence is its own startup project here
# and no connection string is needed at build time -- efbundle takes one with --connection.
RUN dotnet ef migrations bundle \
    --project src/JobTracker.Persistence \
    --startup-project src/JobTracker.Persistence \
    --configuration Release \
    --output /bundle/efbundle

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
# Npgsql probes for Kerberos GSSAPI on connect; the runtime image does not ship it, and the
# missing-library error it prints looks like a failure even though the migration succeeds.
RUN apt-get update     && apt-get install -y --no-install-recommends libgssapi-krb5-2     && rm -rf /var/lib/apt/lists/*
WORKDIR /bundle
COPY --from=build /bundle .
ENTRYPOINT ["./efbundle"]
