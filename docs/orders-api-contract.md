# API Contract: Create Order

## `POST /orders`

Membuat order dengan status awal `Pending`. Semua nama properti JSON menggunakan `camelCase`.

### Request

| Header | Nilai | Aturan |
| --- | --- | --- |
| `Content-Type` | `application/json` | Body dikirim sebagai JSON. |
| `Idempotency-Key` | UUID atau string unik per operasi create | Wajib, tidak boleh kosong atau hanya spasi, maksimal 200 karakter. Gunakan key yang sama hanya untuk retry operasi yang sama. |

```http
POST /orders HTTP/1.1
Content-Type: application/json
Idempotency-Key: 7c16dc8e-2bb4-49e1-bd18-948bc1bdc643
```

```json
{
  "customerId": "C001",
  "shippingAddress": "Jl. Melati No. 10",
  "items": [
    {
      "productId": 1,
      "quantity": 2
    }
  ]
}
```

| Field | Tipe | Validasi |
| --- | --- | --- |
| `customerId` | string | Wajib; tidak boleh kosong atau hanya spasi. |
| `shippingAddress` | string | Wajib; tidak boleh kosong atau hanya spasi. |
| `items` | array | Wajib; minimal satu item. `productId` tidak boleh berulang dalam satu request. |
| `items[].productId` | integer | Wajib; lebih dari 0 dan harus merujuk produk yang ada. |
| `items[].quantity` | integer | Wajib; lebih dari 0. |

Harga, status, dan ID order tidak diterima dari klien. Server mengambil harga produk saat order dibuat, menetapkan status awal, dan membuat ID order.

### Respons sukses

**Order baru:** `201 Created`, dengan header `Location: /orders/{orderId}`.

```http
HTTP/1.1 201 Created
Location: /orders/6f77a956-0b74-47f7-a97f-4b67d7959082
Content-Type: application/json
X-Correlation-ID: 0HN8EXAMPLE:00000001
```

```json
{
  "orderId": "6f77a956-0b74-47f7-a97f-4b67d7959082",
  "status": "Pending"
}
```

**Retry dengan key dan payload yang sama:** `200 OK`, mengembalikan order yang sudah dibuat dengan bentuk body yang sama. Tidak membuat order baru dan tidak mengurangi stok lagi. Nilai `status` mencerminkan status order yang dikembalikan saat retry.

`Location` menunjuk resource `/orders/{orderId}`; endpoint `GET /orders/{orderId}` perlu tersedia ketika pembacaan order diimplementasikan.

### Aturan idempotency

- Key berlaku unik secara global untuk operasi create order ini.
- Payload yang sama berarti nilai `customerId`, `shippingAddress`, serta pasangan `productId` dan `quantity` sama. Urutan properti JSON dan urutan item tidak mengubah kesamaan payload; `productId` dalam satu request harus unik. Nilai string dibandingkan apa adanya setelah lolos validasi.
- Key yang sama dengan payload berbeda menghasilkan `409 Conflict`, tanpa membuat order atau mengurangi stok.
- Penyimpanan key, order, item, dan pengurangan stok harus berhasil atau gagal sebagai satu transaksi. Request bersamaan dengan key yang sama tidak boleh membuat dua order.

### Respons error

Semua error memakai `application/problem+json` dan memuat `correlationId`. Respons juga mengirim `X-Correlation-ID` dengan nilai yang sama agar dapat dicocokkan dengan log. Detail internal database tidak dikirim ke klien.

| Kondisi | HTTP | `code` |
| --- | --- | --- |
| Body atau header wajib tidak valid, termasuk item kosong/duplikat dan nilai numerik tidak valid | `400 Bad Request` | `INVALID_REQUEST` |
| `productId` valid secara bentuk tetapi produk tidak ditemukan | `422 Unprocessable Content` | `PRODUCT_NOT_FOUND` |
| Stok produk tidak cukup | `409 Conflict` | `INSUFFICIENT_STOCK` |
| Key sama digunakan dengan payload berbeda | `409 Conflict` | `IDEMPOTENCY_KEY_REUSED` |
| Kegagalan database yang tidak terduga | `500 Internal Server Error` | `INTERNAL_ERROR` |

Contoh error stok:

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json
X-Correlation-ID: 0HN8EXAMPLE:00000002
```

```json
{
  "type": "about:blank",
  "title": "Insufficient stock",
  "status": 409,
  "detail": "Stock for product 1 is insufficient.",
  "code": "INSUFFICIENT_STOCK",
  "correlationId": "0HN8EXAMPLE:00000002"
}
```

Untuk `400` karena validasi field, respons dapat menambahkan properti `errors` berisi nama field dan pesan validasinya.
