FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Dev.Naamloos.Fennec.Notifications/Dev.Naamloos.Fennec.Notifications.csproj Dev.Naamloos.Fennec.Notifications/
RUN dotnet restore Dev.Naamloos.Fennec.Notifications/Dev.Naamloos.Fennec.Notifications.csproj
COPY Dev.Naamloos.Fennec.Notifications/ Dev.Naamloos.Fennec.Notifications/
RUN dotnet publish Dev.Naamloos.Fennec.Notifications/Dev.Naamloos.Fennec.Notifications.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Dev.Naamloos.Fennec.Notifications.dll"]
