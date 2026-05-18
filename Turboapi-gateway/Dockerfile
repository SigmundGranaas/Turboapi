FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Packages.props", "./"]
COPY ["Turboapi-gateway/Turboapi-gateway.csproj", "Turboapi-gateway/"]
RUN dotnet restore "Turboapi-gateway/Turboapi-gateway.csproj"

COPY . .
WORKDIR /src/Turboapi-gateway
RUN dotnet publish "Turboapi-gateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Turboapi-gateway.dll"]
