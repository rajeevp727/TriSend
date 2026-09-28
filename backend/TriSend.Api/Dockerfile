FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY backend/Directory.Build.props ./backend/
COPY backend/TriSend.Api/TriSend.Api.csproj ./backend/TriSend.Api/
COPY shared/TriSend.Contracts/TriSend.Contracts.csproj ./shared/TriSend.Contracts/

RUN dotnet restore backend/TriSend.Api/TriSend.Api.csproj

COPY backend ./backend
COPY shared ./shared

WORKDIR /src/backend/TriSend.Api
RUN dotnet publish TriSend.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "TriSend.Api.dll"]
