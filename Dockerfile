# One Dockerfile for every service. Choose the service with build args, e.g.:
#   docker build --build-arg PROJECT=src/Services/Orders/Orders.Api/Orders.Api.csproj --build-arg ASSEMBLY=Orders.Api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src

# Restore first, using only project files, so the layer is cached until a dependency changes.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/BuildingBlocks/LankaEats.Contracts/*.csproj src/BuildingBlocks/LankaEats.Contracts/
COPY src/BuildingBlocks/LankaEats.ServiceDefaults/*.csproj src/BuildingBlocks/LankaEats.ServiceDefaults/
COPY src/Services/Orders/Orders.Domain/*.csproj src/Services/Orders/Orders.Domain/
COPY src/Services/Orders/Orders.Application/*.csproj src/Services/Orders/Orders.Application/
COPY src/Services/Orders/Orders.Infrastructure/*.csproj src/Services/Orders/Orders.Infrastructure/
COPY src/Services/Orders/Orders.Api/*.csproj src/Services/Orders/Orders.Api/
COPY src/Services/Inventory/Inventory.Api/*.csproj src/Services/Inventory/Inventory.Api/
COPY src/Services/Payments/Payments.Api/*.csproj src/Services/Payments/Payments.Api/
COPY src/Services/Notifications/Notifications.Api/*.csproj src/Services/Notifications/Notifications.Api/
COPY src/Gateway/LankaEats.Gateway/*.csproj src/Gateway/LankaEats.Gateway/
RUN dotnet restore "$PROJECT"

COPY src/ src/
RUN dotnet publish "$PROJECT" -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG ASSEMBLY
ENV APP_ASSEMBLY=${ASSEMBLY} \
    ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
COPY --from=build /app .
# Run as the image's built-in non-root user.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_ASSEMBLY.dll\""]
