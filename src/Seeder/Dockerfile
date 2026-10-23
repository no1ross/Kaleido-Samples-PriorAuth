FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
# The Kaleido packages come from its GitHub Packages feed: the token is a build secret (compose:
# build.secrets nuget_token), given to this command only, never written to a layer.
RUN --mount=type=secret,id=nuget_token \
    NuGetPackageSourceCredentials_kaleido="Username=docker;Password=$(cat /run/secrets/nuget_token)" \
    dotnet restore "src/Seeder/Kaleido.Samples.PriorAuth.Seeder.csproj"
RUN dotnet publish "src/Seeder/Kaleido.Samples.PriorAuth.Seeder.csproj" --no-restore -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Kaleido.Samples.PriorAuth.Seeder.dll"]
