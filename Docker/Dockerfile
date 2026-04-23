# Build stage
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["OrderProcessingSystem.csproj", "./"]
RUN dotnet restore "OrderProcessingSystem.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "OrderProcessingSystem.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "OrderProcessingSystem.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final
WORKDIR /app

# Install EF Core tools for migrations
RUN dotnet tool install --global dotnet-ef --version 7.0.14
ENV PATH="$PATH:/root/.dotnet/tools"

EXPOSE 80
EXPOSE 443

# Copy published app
COPY --from=publish /app/publish .

# Set entry point
ENTRYPOINT ["dotnet", "OrderProcessingSystem.dll"]
