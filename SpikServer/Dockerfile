# Панель собирается первой: её dist попадает в wwwroot сервера при сборке проекта.
FROM node:22-alpine AS panel
WORKDIR /panel
COPY admin-panel/package*.json ./
RUN npm ci
COPY admin-panel/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SpikServer/SpikServer/SpikServer.csproj SpikServer/SpikServer/
RUN dotnet restore SpikServer/SpikServer/SpikServer.csproj
COPY SpikServer/ SpikServer/
COPY AetherMain/ AetherMain/
COPY --from=panel /panel/dist/ admin-panel/dist/
RUN dotnet publish SpikServer/SpikServer/SpikServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./

# Данные лежат отдельно от кода, чтобы том не перекрывал сборку.
RUN mkdir -p /data
ENV SPIK_URLS=http://0.0.0.0:5232
EXPOSE 5232

ENTRYPOINT ["dotnet", "AetherShell.Server.dll"]
