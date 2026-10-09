# Build from the repository root:  docker build -f src/Api/EduVibe.Api/Dockerfile -t eduvibe-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props EduVibe.slnx ./
COPY src ./src
RUN dotnet publish src/Api/EduVibe.Api/EduVibe.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "EduVibe.Api.dll"]
