# Image de l'outil d'exploitation (migrations, rappels, rapport du soir). Construire depuis la racine : docker build -f deploy/ops.Dockerfile -t soro-ops .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Soro.slnx ./
COPY src/backend ./src/backend
RUN dotnet publish src/backend/Soro.Ops/Soro.Ops.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS run
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["dotnet", "soro-ops.dll"]
