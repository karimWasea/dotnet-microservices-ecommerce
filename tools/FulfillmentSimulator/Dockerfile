# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY FulfillmentSimulator.csproj ./
RUN dotnet restore FulfillmentSimulator.csproj

COPY . ./
RUN dotnet publish FulfillmentSimulator.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "FulfillmentSimulator.dll"]
