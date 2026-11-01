# Image de l'API Sôrô. Construire depuis la racine du dépôt : docker build -f deploy/api.Dockerfile -t soro-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Soro.slnx ./
COPY src/backend ./src/backend
RUN dotnet publish src/backend/Soro.Api/Soro.Api.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS run
WORKDIR /app
COPY --from=build /out .
# Utilisateur sans droits (fourni par l'image) ; aucun secret dans l'image : tout vient des variables d'environnement.
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Soro.Api.dll"]
