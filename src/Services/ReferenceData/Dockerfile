FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends wget && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
# The Kaleido packages come from its GitHub Packages feed: the token is a build secret (compose:
# build.secrets nuget_token), given to this command only, never written to a layer.
RUN --mount=type=secret,id=nuget_token \
    NuGetPackageSourceCredentials_kaleido="Username=docker;Password=$(cat /run/secrets/nuget_token)" \
    dotnet restore "src/Services/ReferenceData/Kaleido.Samples.PriorAuth.ReferenceData.csproj"
RUN dotnet publish "src/Services/ReferenceData/Kaleido.Samples.PriorAuth.ReferenceData.csproj" --no-restore -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/data
ENTRYPOINT ["dotnet", "Kaleido.Samples.PriorAuth.ReferenceData.dll"]
