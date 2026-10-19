# Stage 1: Build and Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/OrderService.Domain/OrderService.Domain.csproj src/OrderService.Domain/
COPY src/OrderService.Application/OrderService.Application.csproj src/OrderService.Application/
COPY src/OrderService.Infrastructure/OrderService.Infrastructure.csproj src/OrderService.Infrastructure/
COPY src/OrderService.Api/OrderService.Api.csproj src/OrderService.Api/

RUN dotnet restore src/OrderService.Api/OrderService.Api.csproj

COPY src/ src/
RUN dotnet publish src/OrderService.Api/OrderService.Api.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish ./

USER $APP_UID
ENTRYPOINT ["dotnet", "OrderService.Api.dll"]
