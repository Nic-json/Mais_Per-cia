FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Sistemas para pericias.csproj", "./"]
RUN dotnet restore "Sistemas para pericias.csproj"
COPY . .
RUN dotnet publish "Sistemas para pericias.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV APP_UID=1654
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Sistemas para pericias.dll"]
