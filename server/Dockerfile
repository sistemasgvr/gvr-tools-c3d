# docs/LICENSING_PLAN.md, Pieza 6 "Deploy en EasyPanel" -- un solo contenedor sirve
# /v1/* y /admin/* (GvrLicense.Api). EasyPanel construye esta imagen a partir de este Dockerfile
# apuntado al repo; el connection string y las claves llegan por variables de entorno, nunca
# horneadas en la imagen.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY GvrLicense.slnx .
COPY Directory.Build.props .
COPY src/GvrLicense.Domain/GvrLicense.Domain.csproj src/GvrLicense.Domain/
COPY src/GvrLicense.Contracts/GvrLicense.Contracts.csproj src/GvrLicense.Contracts/
COPY src/GvrLicense.Infrastructure/GvrLicense.Infrastructure.csproj src/GvrLicense.Infrastructure/
COPY src/GvrLicense.Api/GvrLicense.Api.csproj src/GvrLicense.Api/
RUN dotnet restore src/GvrLicense.Api/GvrLicense.Api.csproj

COPY src/ src/
RUN dotnet publish src/GvrLicense.Api/GvrLicense.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080

ENTRYPOINT ["dotnet", "GvrLicense.Api.dll"]
