FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Turboapi-auth/Turboapi-auth.csproj", "Turboapi-auth/"]
RUN dotnet restore "Turboapi-auth/Turboapi-auth.csproj"
COPY . .
WORKDIR "/src/Turboapi-auth"
RUN dotnet build "Turboapi-auth.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Turboapi-auth.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Turboapi-auth.dll"]
