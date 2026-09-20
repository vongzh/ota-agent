# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/StayOta.Agent.slnx backend/
COPY backend/src backend/src
COPY backend/tests backend/tests
COPY contracts contracts
COPY mock mock
COPY eval eval
RUN dotnet restore backend/src/Hosts/StayOta.Agent.Host/StayOta.Agent.Host.csproj
RUN dotnet publish backend/src/Hosts/StayOta.Agent.Host/StayOta.Agent.Host.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV STAYOTA_AGENT_ROOT=/data
COPY --from=build /app/publish .
COPY contracts /data/contracts
COPY mock /data/mock
COPY eval /data/eval
EXPOSE 8080
ENTRYPOINT ["dotnet", "StayOta.Agent.Host.dll"]
