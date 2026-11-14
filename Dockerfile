# NobodyFalls: one image serving the API and (from step f) the built front end.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so that code changes reuse the cached layer.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/NobodyFalls.Domain/NobodyFalls.Domain.csproj src/NobodyFalls.Domain/
COPY src/NobodyFalls.Application/NobodyFalls.Application.csproj src/NobodyFalls.Application/
COPY src/NobodyFalls.Infrastructure/NobodyFalls.Infrastructure.csproj src/NobodyFalls.Infrastructure/
COPY src/NobodyFalls.Api/NobodyFalls.Api.csproj src/NobodyFalls.Api/
RUN dotnet restore src/NobodyFalls.Api/NobodyFalls.Api.csproj

COPY src/ src/
RUN dotnet publish src/NobodyFalls.Api/NobodyFalls.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "NobodyFalls.Api.dll"]
