FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY api/BloodBank.Api.csproj api/
RUN dotnet restore api/BloodBank.Api.csproj
COPY api/ api/
RUN dotnet publish api/BloodBank.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "BloodBank.Api.dll"]
