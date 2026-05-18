FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["hosts/Turbo.Host.Modulith/Turbo.Host.Modulith.csproj", "hosts/Turbo.Host.Modulith/"]
COPY ["Turboapi-auth/Turboapi-auth.csproj", "Turboapi-auth/"]
COPY ["Turboapi-activity/Turboapi-activity.csproj", "Turboapi-activity/"]
COPY ["Turboapi-geo/Turboapi-geo.csproj", "Turboapi-geo/"]
COPY ["TurboAuthentication/TurboAuthentication.csproj", "TurboAuthentication/"]
COPY ["Turbo-pg-data/Turbo-pg-data.csproj", "Turbo-pg-data/"]
COPY ["src/Shared/Turbo.Messaging.Abstractions/Turbo.Messaging.Abstractions.csproj", "src/Shared/Turbo.Messaging.Abstractions/"]
COPY ["src/Shared/Turbo.Messaging.InProcess/Turbo.Messaging.InProcess.csproj", "src/Shared/Turbo.Messaging.InProcess/"]
COPY ["src/Shared/Turbo.Messaging.Nats/Turbo.Messaging.Nats.csproj", "src/Shared/Turbo.Messaging.Nats/"]
COPY ["src/Shared/Turbo.Outbox.Abstractions/Turbo.Outbox.Abstractions.csproj", "src/Shared/Turbo.Outbox.Abstractions/"]
COPY ["src/Shared/Turbo.Outbox.Postgres/Turbo.Outbox.Postgres.csproj", "src/Shared/Turbo.Outbox.Postgres/"]
RUN dotnet restore "hosts/Turbo.Host.Modulith/Turbo.Host.Modulith.csproj"

COPY . .
WORKDIR /src/hosts/Turbo.Host.Modulith
RUN dotnet publish "Turbo.Host.Modulith.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Turbo.Host.Modulith.dll"]
