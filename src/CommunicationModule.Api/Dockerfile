FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY CommunicationModule.sln ./
COPY src/CommunicationModule.Core/CommunicationModule.Core.csproj src/CommunicationModule.Core/
COPY src/CommunicationModule.Infrastructure/CommunicationModule.Infrastructure.csproj src/CommunicationModule.Infrastructure/
COPY src/CommunicationModule.Api/CommunicationModule.Api.csproj src/CommunicationModule.Api/

RUN dotnet restore src/CommunicationModule.Api/CommunicationModule.Api.csproj

COPY . .
RUN dotnet publish src/CommunicationModule.Api/CommunicationModule.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CommunicationModule.Api.dll"]