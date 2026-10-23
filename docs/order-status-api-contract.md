# API Contract: Update dan Cancel Order Status

Semua nama properti JSON menggunakan `camelCase`. Nilai status yang dikenal API adalah `PENDING`, `CONFIRMED`, `SHIPPED`, `DELIVERED`, dan `CANCELLED`. Nilai status pada request dan respons selalu UPPERCASE.

## `PATCH /orders/{id}/status`

Mengubah status order. Target `Cancelled` diterima di endpoint ini, tetapi diproses melalui workflow cancellation yang sama dengan `POST /orders/{id}/cancel`.

### Request

| Bagian | Aturan |
| --- | --- |
| `id` pada URL | Wajib berupa GUID yang valid. |
| `Content-Type` | `application/json`. |
| `status` pada body | Wajib berupa string dari daftar status di atas; tidak boleh kosong. |

```http
PATCH /orders/6f77a956-0b74-47f7-a97f-4b67d7959082/status HTTP/1.1
Content-Type: application/json
```

```json
{
  "status": "SHIPPED"
}
```

### Respons sukses

`200 OK`. `updatedAtUtc` adalah waktu perubahan yang ditetapkan server dalam format UTC ISO 8601; nilai berikut hanya contoh.

```http
HTTP/1.1 200 OK
Content-Type: application/json
X-Correlation-ID: 0HN8EXAMPLE:00000003
```

```json
{
  "id": "6f77a956-0b74-47f7-a97f-4b67d7959082",
  "status": "SHIPPED",
  "updatedAtUtc": "2026-10-23T03:00:00Z"
}
```

## `POST /orders/{id}/cancel`

Membatalkan order melalui workflow cancellation. Request tidak memerlukan body.

### Request

| Bagian | Aturan |
| --- | --- |
| `id` pada URL | Wajib berupa GUID yang valid. |
| Body | Tidak diperlukan. |

```http
POST /orders/6f77a956-0b74-47f7-a97f-4b67d7959082/cancel HTTP/1.1
```

### Respons sukses

`200 OK`.

```http
HTTP/1.1 200 OK
Content-Type: application/json
X-Correlation-ID: 0HN8EXAMPLE:00000004
```

```json
{
  "id": "6f77a956-0b74-47f7-a97f-4b67d7959082",
  "status": "CANCELLED"
}
```

## Respons error

Kedua endpoint menggunakan `application/problem+json`. Body error memuat `correlationId`, dan header `X-Correlation-ID` membawa nilai yang sama. Detail internal database tidak dikirim ke klien.

| Kondisi | HTTP | `code` |
| --- | --- | --- |
| `id` bukan GUID valid, atau payload `PATCH` tidak valid | `400 Bad Request` | `INVALID_REQUEST` |
| Order tidak ditemukan | `404 Not Found` | `ORDER_NOT_FOUND` |
| Perubahan status atau cancellation tidak diperbolehkan oleh workflow | `409 Conflict` | `INVALID_STATUS_TRANSITION` |
| Perubahan status bersamaan menyebabkan konflik versi data | `409 Conflict` | `CONCURRENT_STATUS_UPDATE` |
| Kegagalan database yang tidak terduga | `500 Internal Server Error` | `INTERNAL_ERROR` |

Contoh respons ketika transisi tidak diperbolehkan:

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json
X-Correlation-ID: 0HN8EXAMPLE:00000005
```

```json
{
  "type": "about:blank",
  "title": "Invalid status transition",
  "status": 409,
  "detail": "This order cannot transition to SHIPPED.",
  "code": "INVALID_STATUS_TRANSITION",
  "correlationId": "0HN8EXAMPLE:00000005"
}
```

Aturan transisi status dan efek samping cancellation perlu ditetapkan pada desain workflow sebelum endpoint diimplementasikan. Kedua jalur menuju `Cancelled` harus memakai aturan dan efek samping yang sama.
