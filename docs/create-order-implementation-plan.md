# Implementation Plan: `POST /orders`

1. Validate the request and `Idempotency-Key`, then hash a canonical payload. Sort items by `productId` so item order does not affect the hash.
2. Change `IdempotencyRequest.PayloadHash` to a 64-character SHA-256 hex string. Add a foreign key from the request to the created order and generate an EF Core migration.
3. Start a database transaction and insert the idempotency key first. The primary key arbitrates concurrent requests. On a duplicate-key error, roll back, load the committed request, compare hashes, and return the existing order or a conflict.
4. For a new key, load products and capture prices. Deduct each product's stock with a conditional SQL update (`StockQuantity >= quantity`). Roll back if any product is missing or its stock is insufficient.
5. Save the order and price-snapshot items, link the idempotency request, and commit. Return `201 Created`; a matching retry returns `200 OK`.
6. Wire the endpoint, structured errors, and correlation IDs. Test validation and hash behavior, build the project, and check the migration model.

Apply the migration to a configured SQL Server with:

```powershell
dotnet ef database update --project src/AdayaOrderManagement.API/AdayaOrderManagement.API.csproj --startup-project src/AdayaOrderManagement.API/AdayaOrderManagement.API.csproj
```
