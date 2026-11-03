FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Tienda.Api/Tienda.Api.csproj Tienda.Api/
RUN dotnet restore Tienda.Api/Tienda.Api.csproj
COPY Tienda.Api/ Tienda.Api/
RUN dotnet publish Tienda.Api/Tienda.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
# Carpeta de llaves RSA (volumen); propiedad del usuario no-root 'app'
RUN mkdir -p /app/keys && chown app /app/keys
USER app
ENV ASPNETCORE_URLS=http://+:8080 Seguridad__KeysDir=/app/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tienda.Api.dll"]
