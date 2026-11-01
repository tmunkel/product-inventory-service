# Product Inventory Service

REST API for the product catalog used by the [grocery app](../grocery-app). Built with ASP.NET Core Minimal APIs (.NET 10), EF Core and PostgreSQL.

Features:

- Create, update and delete products (name, EAN, brand, category, size, description)
- Search by name/brand (case-insensitive "contains") or by EAN (exact match for 8/12/13/14-digit codes)
- EAN check-digit validation, unique EANs (duplicate → `409 Conflict`)
- Image upload (JPEG, PNG, WebP, GIF; max 5 MB by default), stored on disk with the path in the database
- Open Food Facts lookup to prefill new products, plus image import from Open Food Facts

## Run with Docker

```powershell
docker compose up -d --build
```

This starts PostgreSQL and the API at <http://localhost:5100>. Uploaded images are stored in the `inventory-images` volume and database data in the `inventory-db` volume.

Settings come from environment variables, which override `appsettings.json`. Use `__` in place of `:`, for example `ConnectionStrings__Inventory`. The compose file supports these variables, which you can also put in a `.env` file:

| Variable | Default | Description |
|---|---|---|
| `POSTGRES_PASSWORD` | `inventory` | Database password, used by PostgreSQL and in the API connection string |
| `API_PORT` | `5100` | Host port for the API |
| `DB_PORT` | `5433` | Host port for PostgreSQL (e.g. for pgAdmin) |
| `REGISTRY` | *(none)* | Registry host the image is pulled from / pushed to, e.g. `registry.example.com` |
| `TAG` | `latest` | Image tag, e.g. a Git commit for rollback |

Inside the compose network, the API reaches the database at `Host=postgres;Port=5432`.

### Deploy prebuilt images (no .NET SDK or source on the server)

The server needs only Docker with Compose. Build the image on a machine that has the source, then copy it to the server, either as a file or through a registry.

**As a file.** On the build machine:

```powershell
docker compose build
docker save -o product-inventory-service.tar product-inventory-service:latest
```

Copy `product-inventory-service.tar` and `docker-compose.yml`, plus an optional `.env`, to a folder on the server. Then run:

```bash
docker load -i product-inventory-service.tar
docker compose up -d --no-build
```

**Through a registry.** On the build machine:

```powershell
$env:REGISTRY = "registry.example.com"
docker compose build
docker compose push api
```

On the server, put `REGISTRY=registry.example.com` in `.env`, then run `docker compose pull` and `docker compose up -d --no-build`. The image is then `registry.example.com/product-inventory-service:latest`; set `TAG` to use another tag.

Images are built for the build machine's CPU architecture. For an ARM server, such as a Raspberry Pi, build on an ARM machine or use `docker buildx build --platform linux/arm64`. The database tables are created automatically when the API first starts.

### Behind Traefik (HTTPS)

`docker-compose.traefik.yml` publishes the API at `https://inventory.<DOMAIN>` through a Traefik reverse proxy instead of host ports, and gives it the name `inventory-api` on the shared `proxy` network so the grocery app can reach it directly. Enable it with these lines in `.env`:

```
COMPOSE_FILE=docker-compose.yml:docker-compose.traefik.yml
DOMAIN=example.com
```

The Traefik stack and a full walkthrough (certificates, registry, start order) live in the separate `deploy` folder; see its `README.md`.

## Run locally

Prerequisites: .NET 10 SDK and Docker.

```powershell
docker compose up -d postgres             # PostgreSQL only, on localhost:5433
dotnet run --project src/ProductInventory.Api
```

The API listens on <http://localhost:5100>. Interactive API docs: <http://localhost:5100/scalar>, OpenAPI document: `/openapi/v1.json`, health check: `/health`.
Database migrations are applied on startup (`Database:MigrateOnStartup`).

## API

| Method | Route | Description |
|---|---|---|
| GET | `/api/products?query=&ids=&page=1&pageSize=20` | Search (EAN exact or name/brand contains). `ids` (repeatable, e.g. `?ids=a&ids=b`) limits the results to those products. Without filters, all products are listed. |
| GET | `/api/products/{id}` | Get a product |
| GET | `/api/products/ean/{ean}` | Get a product by EAN |
| POST | `/api/products` | Create a product |
| PUT | `/api/products/{id}` | Update a product |
| DELETE | `/api/products/{id}` | Delete a product and its image |
| POST | `/api/products/{id}/image` | Upload an image (`multipart/form-data`, field `file`) |
| POST | `/api/products/{id}/image/from-url` | Import an image from an `https://*.openfoodfacts.org` URL (`{ "url": "..." }`) |
| GET | `/api/products/{id}/image` | Download the image |
| DELETE | `/api/products/{id}/image` | Remove the image |
| GET | `/api/lookup/{ean}` | Look up product data in Open Food Facts |

Create/update body:

```json
{ "ean": "3017620422003", "name": "Nutella", "brand": "Ferrero", "category": "Spreads", "quantity": "400 g", "description": null }
```

Errors are returned as RFC 7807 problem details (`400` validation errors, `404`, `409` duplicate EAN).

## Configuration (`appsettings.json`)

| Key | Default | Description |
|---|---|---|
| `ConnectionStrings:Inventory` | `Host=localhost;Port=5433;...` | PostgreSQL connection |
| `ImageStorage:Path` | `data/images` | Image directory (relative to the content root or absolute) |
| `ImageStorage:MaxBytes` | `5242880` | Maximum image size |
| `OpenFoodFacts:BaseUrl` / `UserAgent` | world.openfoodfacts.org | Open Food Facts API. Open Food Facts requires a descriptive User-Agent. |
| `Cors:AllowedOrigins` | `[]` | Origins allowed to call the API from a browser |
| `Database:MigrateOnStartup` | `true` | Apply EF Core migrations on startup |

There is no authentication. Run the service on a trusted network only.

## Tests

```powershell
dotnet test
```

The integration tests use Testcontainers, so Docker must be running.

## Migrations

```powershell
dotnet tool restore
dotnet ef migrations add <Name> -p src/ProductInventory.Api -o Data/Migrations
```
