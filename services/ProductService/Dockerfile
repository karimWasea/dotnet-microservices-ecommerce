# Stage 1: Build and Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/ProductService.Domain/ProductService.Domain.csproj src/ProductService.Domain/
COPY src/ProductService.Application/ProductService.Application.csproj src/ProductService.Application/
COPY src/ProductService.Infrastructure/ProductService.Infrastructure.csproj src/ProductService.Infrastructure/
COPY src/ProductService.Api/ProductService.Api.csproj src/ProductService.Api/

RUN dotnet restore src/ProductService.Api/ProductService.Api.csproj

COPY src/ src/
RUN dotnet publish src/ProductService.Api/ProductService.Api.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish ./

USER $APP_UID
ENTRYPOINT ["dotnet", "ProductService.Api.dll"]
