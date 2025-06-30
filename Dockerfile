# Etap 1: Budowanie aplikacji
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Kopiuj pliki .csproj i przywróć zależności
COPY ["HearthstoneScraper/HearthstoneScraper.csproj", "HearthstoneScraper/"]
COPY ["HearthstoneScraper.Core/HearthstoneScraper.Core.csproj", "HearthstoneScraper.Core/"]
COPY ["HearthstoneScraper.Data/HearthstoneScraper.Data.csproj", "HearthstoneScraper.Data/"]
RUN dotnet restore "HearthstoneScraper/HearthstoneScraper.csproj"

# Kopiuj resztę kodu źródłowego
COPY . .
WORKDIR "/src/HearthstoneScraper"
RUN dotnet build "HearthstoneScraper.csproj" -c Release -o /app/build

# Etap 2: Publikowanie aplikacji
FROM build AS publish
RUN dotnet publish "HearthstoneScraper.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etap 3: Finalny, lekki obraz
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HearthstoneScraper.dll"]