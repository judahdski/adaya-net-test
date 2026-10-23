# API Contract: List Orders dengan Filter

## `GET /orders`

Mengambil daftar ringkasan order dengan filter opsional dan paginasi. Semua nama properti JSON menggunakan `camelCase`. Request tidak memerlukan body.

### Request

```http
GET /orders?status=Confirmed&customerId=CUST-001&page=1&pageSize=20 HTTP/1.1
```

| Query parameter | Tipe | Default | Aturan |
| --- | --- | --- | --- |
| `status` | string, opsional | Semua status | Salah satu `Pending`, `Confirmed`, `Shipped`, `Delivered`, atau `Cancelled`; pencocokan nilai tidak peka huruf besar/kecil. |
| `customerId` | string, opsional | Semua customer | Cocokkan seluruh ID customer, bukan sebagian teks. Jika dikirim, tidak boleh kosong atau hanya spasi. |
| `fromUtc` | string (ISO 8601 UTC), opsional | Tidak dibatasi | Batas awal `createdAtUtc`, **inklusif** (`createdAtUtc >= fromUtc`). |
| `toUtc` | string (ISO 8601 UTC), opsional | Tidak dibatasi | Batas akhir `createdAtUtc`, **eksklusif** (`createdAtUtc < toUtc`). |
| `page` | integer | `1` | Nomor halaman, minimal `1`. |
| `pageSize` | integer | `20` | Jumlah order per halaman, minimal `1` dan maksimal `100`. |

`fromUtc` dan `toUtc` harus memakai format ISO 8601 dengan penanda UTC `Z`, misalnya `2026-10-23T00:00:00Z`. Jika keduanya dikirim, `fromUtc` harus lebih kecil dari `toUtc`.

Filter yang dikirim bersama diterapkan dengan logika AND. `status` dibandingkan dengan status order saat dibaca; `customerId` dibandingkan dengan ID customer order. Filter tanggal hanya memakai `createdAtUtc`, bukan `updatedAtUtc`.

### Respons sukses

`200 OK` dengan `Content-Type: application/json`. Order diurutkan menurut `createdAtUtc` terbaru lebih dahulu, lalu `id` menurun sebagai pemecah nilai waktu yang sama. Paginasi diterapkan setelah seluruh filter dan pengurutan.

```http
HTTP/1.1 200 OK
Content-Type: application/json
X-Correlation-ID: 0HN8EXAMPLE:00000008
```

```json
{
  "orders": [
    {
      "id": "a75d4ed2-3e99-4cb3-861d-b5b7539f86d2",
      "customerId": "CUST-001",
      "shippingAddress": "Jakarta Selatan",
      "status": "Confirmed",
      "createdAtUtc": "2026-10-23T02:00:00Z",
      "updatedAtUtc": "2026-10-23T02:05:00Z",
      "totalAmount": 300000
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

| Field | Tipe | Makna |
| --- | --- | --- |
| `orders` | array | Ringkasan order pada halaman yang diminta; item produk tidak disertakan. |
| `orders[].id` | string (GUID) | ID order. |
| `orders[].customerId` | string | ID customer. |
| `orders[].shippingAddress` | string | Alamat pengiriman. |
| `orders[].status` | string | Status saat dibaca, dalam *Title Case* seperti `GET /orders/{id}`. |
| `orders[].createdAtUtc` | string (ISO 8601 UTC) | Waktu pembuatan order. |
| `orders[].updatedAtUtc` | string (ISO 8601 UTC) | Waktu perubahan terakhir order. |
| `orders[].totalAmount` | number (decimal) | Jumlah `quantity * unitPrice` dari seluruh item order. |
| `page` | integer | Halaman yang diminta. |
| `pageSize` | integer | Ukuran halaman yang diminta. |
| `totalCount` | integer | Jumlah seluruh order yang cocok dengan filter, sebelum paginasi. |
| `totalPages` | integer | `ceil(totalCount / pageSize)`; `0` jika tidak ada hasil. |

`totalAmount` dihitung dari harga yang tercatat pada item order, tanpa harus disimpan sebagai kolom database. Halaman tanpa hasil tetap mengembalikan `200 OK` dengan `orders: []`, `totalCount: 0`, dan `totalPages: 0` jika tidak ada order yang cocok. Jika `page` melampaui halaman terakhir, `orders` kosong tetapi `totalCount` dan `totalPages` tetap menggambarkan seluruh hasil filter.

### Respons error

Respons error memakai `application/problem+json`, memuat `correlationId`, dan mengirim header `X-Correlation-ID` dengan nilai yang sama.

| Kondisi | HTTP | `code` |
| --- | --- | --- |
| `status` tidak dikenal, `customerId` kosong, tanggal tidak valid atau bukan UTC, `fromUtc >= toUtc`, `page < 1`, atau `pageSize` di luar `1`–`100` | `400 Bad Request` | `INVALID_REQUEST` |
| Kegagalan server yang tidak terduga | `500 Internal Server Error` | `INTERNAL_ERROR` |

Contoh `pageSize` melebihi batas:

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
X-Correlation-ID: 0HN8EXAMPLE:00000009
```

```json
{
  "type": "about:blank",
  "title": "Invalid request",
  "status": 400,
  "detail": "pageSize must be between 1 and 100.",
  "code": "INVALID_REQUEST",
  "correlationId": "0HN8EXAMPLE:00000009"
}
```
