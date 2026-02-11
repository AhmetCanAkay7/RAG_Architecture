FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5257

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY SK-UserGuide/SK-UserGuide.csproj ./SK-UserGuide/
RUN dotnet restore SK-UserGuide/SK-UserGuide.csproj
COPY SK-UserGuide/ ./SK-UserGuide/
RUN dotnet publish SK-UserGuide/SK-UserGuide.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5257
ENTRYPOINT ["dotnet", "SK-UserGuide.dll"]
