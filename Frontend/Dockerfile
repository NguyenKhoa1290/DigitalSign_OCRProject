FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && ln -sf /usr/bin/python3 /usr/bin/python \
    && rm -rf /var/lib/apt/lists/*

RUN dotnet workload install wasm-tools

COPY Frontend/HauDocumentApp.csproj Frontend/
RUN dotnet restore Frontend/HauDocumentApp.csproj

COPY Frontend/ Frontend/
RUN dotnet publish Frontend/HauDocumentApp.csproj -c Release -o /app/publish --no-restore

FROM nginx:1.27-alpine AS runtime
COPY Frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

EXPOSE 80
