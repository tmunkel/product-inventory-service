# Product Inventory Service

REST API for the product catalog used by the [grocery app](../grocery-app). Built with ASP.NET Core Minimal APIs (.NET 10), EF Core and PostgreSQL.

Features:

- Create, update and delete products (name, EAN, brand, category, size, description)
- Search by name/brand (case-insensitive "contains") or by EAN (exact match for 8/12/13/14-digit codes)
- EAN check-digit validation, unique EANs (duplicate → `409 Conflict`)
- Image upload (JPEG, PNG, WebP, GIF; max 5 MB by default), stored on disk with the path in the database
- Open Food Facts lookup to prefill new products, plus image import from Open Food Facts

## Run locally

Prerequisites: .NET 10 SDK and Docker.

```powershell
docker compose up -d                      # PostgreSQL on localhost:5433
dotnet run --project src/ProductInventory.Api
```

The API listens on <http://localhost:5100>. Interactive API docs: <http://localhost:5100/scalar>, OpenAPI document: `/openapi/v1.json`, health check: `/health`.
Database migrations are applied on startup (`Database:MigrateOnStartup`).

## API

| Method | Route | Description |
|---|---|---|
| GET | `/api/products?query=&page=1&pageSize=20` | Search (EAN exact or name/brand contains). No query lists all products. |
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
