FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY workers/TriSend.Worker/TriSend.Worker.csproj ./workers/TriSend.Worker/
COPY shared/TriSend.Contracts/TriSend.Contracts.csproj ./shared/TriSend.Contracts/

RUN dotnet restore workers/TriSend.Worker/TriSend.Worker.csproj

COPY workers ./workers
COPY shared ./shared

WORKDIR /src/workers/TriSend.Worker
RUN dotnet publish TriSend.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TriSend.Worker.dll"]
