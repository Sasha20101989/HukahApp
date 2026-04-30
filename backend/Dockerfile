ARG PROJECT_PATH

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend ./backend
RUN dotnet restore "backend/${PROJECT_PATH}"
RUN dotnet publish "backend/${PROJECT_PATH}" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet"]
CMD ["HookahPlatform.ApiGateway.dll"]
