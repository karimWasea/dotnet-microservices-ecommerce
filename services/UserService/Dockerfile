# Stage 1: Build and Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/UserService.Domain/UserService.Domain.csproj src/UserService.Domain/
COPY src/UserService.Application/UserService.Application.csproj src/UserService.Application/
COPY src/UserService.Infrastructure/UserService.Infrastructure.csproj src/UserService.Infrastructure/
COPY src/UserService.Api/UserService.Api.csproj src/UserService.Api/

RUN dotnet restore src/UserService.Api/UserService.Api.csproj

COPY src/ src/
RUN dotnet publish src/UserService.Api/UserService.Api.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish ./
RUN mkdir -p /app/.keys && chown -R $APP_UID:$APP_UID /app/.keys

USER $APP_UID
ENTRYPOINT ["dotnet", "UserService.Api.dll"]
