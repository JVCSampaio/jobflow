# syntax=docker/dockerfile:1

# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching)
COPY src/JobFlow.Api/JobFlow.Api.csproj src/JobFlow.Api/
COPY tests/JobFlow.Tests/JobFlow.Tests.csproj tests/JobFlow.Tests/
RUN dotnet restore src/JobFlow.Api/JobFlow.Api.csproj

COPY src/ src/
COPY tests/ tests/
RUN dotnet publish src/JobFlow.Api/JobFlow.Api.csproj -c Release -o /app/publish

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URL=http://+:8080
EXPOSE 8080

CMD ["dotnet", "JobFlow.Api.dll"]
