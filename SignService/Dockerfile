FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY SignService/src/SignService.Core/SignService.Core.csproj SignService/src/SignService.Core/
COPY SignService/src/SignService.Infrastructure/SignService.Infrastructure.csproj SignService/src/SignService.Infrastructure/
COPY SignService/src/SignService.API/SignService.API.csproj SignService/src/SignService.API/
RUN dotnet restore SignService/src/SignService.API/SignService.API.csproj

COPY SignService/src/ SignService/src/
RUN dotnet publish SignService/src/SignService.API/SignService.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "SignService.API.dll"]

