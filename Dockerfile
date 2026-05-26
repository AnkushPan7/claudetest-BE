# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ClaudeCertPractice.Api/ClaudeCertPractice.Api.csproj ./ClaudeCertPractice.Api/
RUN dotnet restore ./ClaudeCertPractice.Api/ClaudeCertPractice.Api.csproj

COPY ClaudeCertPractice.Api/ ./ClaudeCertPractice.Api/
RUN dotnet publish ./ClaudeCertPractice.Api/ClaudeCertPractice.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "ClaudeCertPractice.Api.dll"]
