FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ProductInventory.Api/ProductInventory.Api.csproj src/ProductInventory.Api/
RUN dotnet restore src/ProductInventory.Api/ProductInventory.Api.csproj
COPY src/ src/
RUN dotnet publish src/ProductInventory.Api/ProductInventory.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
# Pre-create the image directory so a mounted volume inherits the non-root owner.
RUN mkdir -p /data/images && chown -R app:app /data
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ImageStorage__Path=/data/images
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "ProductInventory.Api.dll"]
