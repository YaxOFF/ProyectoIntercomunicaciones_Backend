FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Banco.Api/Banco.Api.csproj Banco.Api/
RUN dotnet restore Banco.Api/Banco.Api.csproj
COPY Banco.Api/ Banco.Api/
RUN dotnet publish Banco.Api/Banco.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
# Carpeta de llaves RSA (volumen); propiedad del usuario no-root 'app'
RUN mkdir -p /app/keys && chown app /app/keys
USER app
ENV ASPNETCORE_URLS=http://+:8080 Seguridad__KeysDir=/app/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "Banco.Api.dll"]
