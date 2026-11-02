# Uygulama imajı: docker compose --profile app up -d --build   ya da   docker build -t canapptemplate .
# 1) derleme
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Önce yalnızca proje dosyaları: kod değişince paketler yeniden indirilmez (katman önbelleği).
COPY nuget.config Directory.Build.props Directory.Packages.props CanAppTemplate.slnx ./
COPY src/CanAppTemplate.Domain/CanAppTemplate.Domain.csproj src/CanAppTemplate.Domain/
COPY src/CanAppTemplate.Application/CanAppTemplate.Application.csproj src/CanAppTemplate.Application/
COPY src/CanAppTemplate.Persistence/CanAppTemplate.Persistence.csproj src/CanAppTemplate.Persistence/
COPY src/CanAppTemplate.Infrastructure/CanAppTemplate.Infrastructure.csproj src/CanAppTemplate.Infrastructure/
COPY src/CanAppTemplate.WebAPI/CanAppTemplate.WebAPI.csproj src/CanAppTemplate.WebAPI/
RUN dotnet restore src/CanAppTemplate.WebAPI/CanAppTemplate.WebAPI.csproj

COPY src/ src/
RUN dotnet publish src/CanAppTemplate.WebAPI/CanAppTemplate.WebAPI.csproj -c Release --no-restore -o /app

# 2) çalışma
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
# Yetkisiz kullanıcı (imajda hazır); uygulama 8080'i dinler.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CanAppTemplate.WebAPI.dll"]
