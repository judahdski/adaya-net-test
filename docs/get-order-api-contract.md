# API Contract: Get Order by ID

## `GET /orders/{id}`

Mengambil satu order beserta item dan totalnya. Semua nama properti JSON menggunakan `camelCase`.

### Request

| Bagian | Aturan |
| --- | --- |
| `id` pada URL | Wajib berupa GUID order yang valid. |
| Body | Tidak diperlukan. |

```http
GET /orders/a75d4ed2-3e99-4cb3-861d-b5b7539f86d2 HTTP/1.1
```

### Respons sukses

`200 OK` dengan `Content-Type: application/json`.

```http
HTTP/1.1 200 OK
Content-Type: application/json
X-Correlation-ID: 0HN8EXAMPLE:00000006
```

```json
{
  "id": "a75d4ed2-3e99-4cb3-861d-b5b7539f86d2",
  "customerId": "CUST-001",
  "shippingAddress": "Jakarta Selatan",
  "status": "Confirmed",
  "createdAtUtc": "2026-10-23T02:00:00Z",
  "updatedAtUtc": "2026-10-23T02:05:00Z",
  "items": [
    {
      "productId": 1,
      "quantity": 2,
      "unitPrice": 150000,
      "subtotal": 300000
    }
  ],
  "totalAmount": 300000
}
```

| Field | Tipe | Makna |
| --- | --- | --- |
| `id` | string (GUID) | ID order yang diminta. |
| `customerId` | string | ID pelanggan pada saat order dibuat. |
| `shippingAddress` | string | Alamat pengiriman order. |
| `status` | string | Status order saat dibaca: `Pending`, `Confirmed`, `Shipped`, `Delivered`, atau `Cancelled`. |
| `createdAtUtc` | string (ISO 8601 UTC) | Waktu pembuatan order. |
| `updatedAtUtc` | string (ISO 8601 UTC) | Waktu perubahan terakhir order. |
| `items` | array | Item order; setiap item memuat harga yang tercatat saat order dibuat. |
| `items[].productId` | integer | ID produk. |
| `items[].quantity` | integer | Jumlah produk dalam order. |
| `items[].unitPrice` | number (decimal) | Harga satuan yang tercatat saat order dibuat. |
| `items[].subtotal` | number (decimal) | `quantity * unitPrice`. |
| `totalAmount` | number (decimal) | Jumlah seluruh `items[].subtotal`; `0` jika `items` kosong. |

`subtotal` dan `totalAmount` dihitung dari data item saat respons dibuat; keduanya tidak harus disimpan sebagai kolom database. Urutan item tidak dijamin. Kontrak GET ini mengikuti contoh dengan status *Title Case*; status pada kontrak `POST /orders` dan `PATCH /orders/{id}/status` menggunakan UPPERCASE.

### Respons error

Respons error memakai `application/problem+json`, memuat `correlationId`, dan mengirim header `X-Correlation-ID` dengan nilai yang sama.

| Kondisi | HTTP | `code` |
| --- | --- | --- |
| `id` bukan GUID valid | `400 Bad Request` | `INVALID_REQUEST` |
| Order dengan `id` tersebut tidak ditemukan | `404 Not Found` | `ORDER_NOT_FOUND` |
| Kegagalan server yang tidak terduga | `500 Internal Server Error` | `INTERNAL_ERROR` |

Contoh order tidak ditemukan:

```http
HTTP/1.1 404 Not Found
Content-Type: application/problem+json
X-Correlation-ID: 0HN8EXAMPLE:00000007
```

```json
{
  "type": "about:blank",
  "title": "Order not found",
  "status": 404,
  "detail": "Order a75d4ed2-3e99-4cb3-861d-b5b7539f86d2 was not found.",
  "code": "ORDER_NOT_FOUND",
  "correlationId": "0HN8EXAMPLE:00000007"
}
```
