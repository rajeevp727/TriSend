FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/TriSend.Auth.Domain/TriSend.Auth.Domain.csproj src/TriSend.Auth.Domain/
COPY src/TriSend.Auth.Application/TriSend.Auth.Application.csproj src/TriSend.Auth.Application/
COPY src/TriSend.Auth.Infrastructure/TriSend.Auth.Infrastructure.csproj src/TriSend.Auth.Infrastructure/
COPY src/TriSend.Auth.Api/TriSend.Auth.Api.csproj src/TriSend.Auth.Api/

RUN dotnet restore src/TriSend.Auth.Api/TriSend.Auth.Api.csproj

COPY src ./src

RUN dotnet publish src/TriSend.Auth.Api/TriSend.Auth.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "TriSend.Auth.Api.dll"]
