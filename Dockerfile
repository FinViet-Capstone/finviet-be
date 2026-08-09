FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY FinViet.sln ./
COPY src/FinViet.Domain/FinViet.Domain.csproj src/FinViet.Domain/
COPY src/FinViet.Application/FinViet.Application.csproj src/FinViet.Application/
COPY src/FinViet.Infrastructure/FinViet.Infrastructure.csproj src/FinViet.Infrastructure/
COPY src/FinViet.Api/FinViet.Api.csproj src/FinViet.Api/

RUN dotnet restore src/FinViet.Api/FinViet.Api.csproj

COPY src ./src

RUN dotnet publish src/FinViet.Api/FinViet.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:10000

EXPOSE 10000

ENTRYPOINT ["dotnet", "FinViet.Api.dll"]
