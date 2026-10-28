# Order Management API

REST API berbasis ASP.NET Core buat bikin order sesuai stok produk, mengubah status order, dan melihat detail order beserta harga item yang tersimpan saat order dibuat. Repo ini masih berupa prototipe untuk technical assessment. Data produk disimpan di SQL Server, tapi belum ada endpoint buat mengelola atau mengisi produk.

## Teknologi yang dipakai

| Bagian | Versi atau implementasi |
| --- | --- |
| Aplikasi dan tes | .NET 10 (`net10.0`), ASP.NET Core controllers |
| Database | EF Core SQL Server 10.0.0, EF Core Design 10.0, dan migration SQL Server |
| Deskripsi API | `Microsoft.AspNetCore.OpenApi` 10.0.12; endpoint OpenAPI aktif di lingkungan Development |
| Tes | xUnit 2.9.3, `Microsoft.AspNetCore.Mvc.Testing` 10.0.0, `Microsoft.NET.Test.Sdk` 17.14.1 |

## Struktur aplikasi

- `src/AdayaOrderManagement.API/Controllers/OrdersController.cs` mendefinisikan route HTTP dan mengubah hasil service jadi status HTTP.
- `src/AdayaOrderManagement.API/Application/Orders/` berisi tipe request/response, validasi, hash payload, proses pembuatan order, perubahan status, dan query detail order.
- `src/AdayaOrderManagement.API/Models/` berisi model yang disimpan: produk, order, item, dan data idempotency. `Infrastructure/OrderManagementDbCtx.cs` mengatur constraint, index, relasi, dan `rowversion` order.
- `src/AdayaOrderManagement.API/Migrations/` berisi perubahan skema SQL Server. `tests/AdayaOrderManagement.Tests/` berisi unit test dan integration test dengan SQL Server.

`Program.cs` mendaftarkan DbContext dan service dengan scope per request, memasang controller, serta mengatur error handling. Saat membuat order, controller memvalidasi request; service lalu mengklaim idempotency key, membaca harga produk, mengurangi stok lewat update bersyarat, dan menyimpan order beserta item dalam satu transaksi database. Saat status diubah, service membaca order dan item dengan tracking, memeriksa transisinya, menyimpan perubahan dengan pengecekan `rowversion`, lalu mengembalikan stok dalam transaksi yang sama kalau order dibatalkan. Query detail mengambil data tanpa tracking. Tidak ada repository abstraction atau background worker.

## Cara menjalankan

1. Siapkan .NET 10 SDK, SQL Server, dan tool `dotnet-ef`. Akun SQL Server yang dipakai harus bisa membuat atau memperbarui database saat migration dijalankan. Integration test juga perlu izin membuat dan menghapus database sementara.
2. Atur `ConnectionStrings:DefaultConnection` sesuai SQL Server yang dipakai. Di Development, `appsettings.Development.json` memakai server `localhost`, database `OrderManagementDb`, Windows authentication, dan `TrustServerCertificate=True`. Untuk lingkungan lain, nilai ini bisa diubah lewat user secret atau environment variable `ConnectionStrings__DefaultConnection`. `appsettings.json` tidak punya connection string.
3. Dari root repo, build aplikasi, jalankan migration, lalu nyalakan API:

   ```powershell
   dotnet build AdayaOrderManagement.slnx
   dotnet ef database update --project src/AdayaOrderManagement.API --startup-project src/AdayaOrderManagement.API
   dotnet run --project src/AdayaOrderManagement.API --launch-profile http
   ```

   Profil `http` memakai lingkungan Development dan alamat `http://localhost:5206`. Profil `https` juga membuka `https://localhost:7020`. Migration **tidak jalan otomatis** saat aplikasi menyala. Isi dulu minimal satu produk di database sebelum membuat order karena belum ada seed data atau route untuk membuat produk. Di Development, dokumen OpenAPI JSON tersedia di `/openapi/v1.json`.

## Endpoint API

Nama properti JSON memakai camel case bawaan ASP.NET Core. Respons `POST` dan `PATCH` berbentuk `{ "orderId": "...", "status": "PENDING" }` dengan status huruf besar. Respons `GET` punya bentuk lebih lengkap dan status dengan huruf awal kapital, misalnya `Pending`.

| Method dan route | Request | Kalau berhasil | Respons lain yang ditangani |
| --- | --- | --- | --- |
| `POST /orders` | Header `Idempotency-Key` wajib diisi, tidak boleh cuma spasi, maksimal 200 karakter; body JSON berisi `customerId`, `shippingAddress`, dan `items` yang tidak kosong dengan isi `{productId, quantity}` | `201 Created` dengan header `Location: /orders/{orderId}` dan ringkasan order; retry dengan key dan payload yang sama menghasilkan `200 OK` berisi ringkasan order saat ini | `400` request tidak valid; `409` key dipakai untuk payload berbeda atau stok kurang; `422` produk tidak ditemukan |
| `GET /orders/{id}` | GUID order di URL | `200 OK` berisi detail order | `400` GUID tidak valid; `404` order tidak ada |
| `PATCH /orders/{id}/status` | GUID order di URL; body JSON `{ "status": "CONFIRMED" }` dengan nama status yang persis huruf besar | `200 OK` berisi ringkasan order | `400` body/status tidak valid; `404` order tidak ada; `409` transisi tidak sah, konflik perubahan order, atau stok gagal dikembalikan |
| `GET /WeatherForecast` | Tidak ada | `200 OK` berisi contoh data cuaca yang dibuat saat request | Endpoint bawaan template, tidak terkait order |

Contoh request jika produk dengan ID `1` sudah ada:

```http
POST /orders HTTP/1.1
Content-Type: application/json
Idempotency-Key: example-order-1

{"customerId":"C001","shippingAddress":"Jl. Melati No. 10","items":[{"productId":1,"quantity":2}]}
```

Respons create dan update status cuma berisi `orderId` dan `status`. Respons `GET /orders/{id}` berisi `id`, `customerId`, `shippingAddress`, `status` dalam Title Case, `createdAtUtc`, `updatedAtUtc`, `items` (`productId`, `quantity`, `unitPrice`, `subtotal`), dan `totalAmount`. `subtotal` dan `totalAmount` dihitung dari jumlah serta harga item yang tersimpan. `GET /orders` untuk daftar order dan route pembatalan terpisah belum diimplementasikan.

## Aturan bisnis

- Order baru selalu berstatus `PENDING`. Transisi yang boleh: `PENDING` ke `CONFIRMED` atau `CANCELLED`; `CONFIRMED` ke `SHIPPED` atau `CANCELLED`; `SHIPPED` ke `DELIVERED`. `DELIVERED` dan `CANCELLED` adalah status akhir. Mengirim status yang sama lagi dianggap transisi tidak sah.
- Saat membuat order, `customerId` dan `shippingAddress` wajib diisi dan tidak boleh cuma spasi. Harus ada minimal satu item; `productId` dan `quantity` harus positif; satu produk tidak boleh muncul dua kali dalam order yang sama. Harga item diambil dari produk saat order dibuat dan disimpan sebagai `UnitPrice`, bukan diterima dari client. Produk yang tidak ada menghasilkan `422`; stok kurang menghasilkan `409`.
- Pembuatan order mengurangi stok sebanyak jumlah tiap item. Pembatalan mengembalikan stok tiap item, dan kode pengembalian stok hanya dijalankan kalau transisi ke `CANCELLED` sah. Kalau penambahan stok bakal melewati batas integer, perubahan status dan stok dibatalkan lewat rollback.
- `Idempotency-Key` wajib untuk membuat order dan unik untuk semua order. Hash SHA-256 payload mencakup customer ID, alamat, serta ID dan jumlah item yang diurutkan berdasarkan product ID. Retry dengan key dan payload yang sama mengembalikan status order **saat retry**, tanpa mengurangi stok lagi. Key sama dengan payload berbeda menghasilkan `409`. Nilai string di-hash sesuai isinya, jadi perubahan spasi atau huruf besar/kecil dianggap payload berbeda.
- Constraint SQL menjaga stok tidak negatif, jumlah item lebih dari nol, status tersimpan sebagai nilai huruf besar yang diizinkan, dan pasangan `(OrderId, ProductId)` unik. Foreign key menghubungkan item dengan order/produk dan data idempotency dengan order.

## Concurrency dan konsistensi data

| Skenario | Cara kerjanya dan hasil yang diharapkan | Batasan dan bukti tes |
| --- | --- | --- |
| **A. Dua request mengurangi stok yang sama** | `CreateOrderService` menjalankan `UPDATE` di database dengan syarat product ID cocok dan `StockQuantity >= quantity`, lalu menetapkan stok menjadi `StockQuantity - quantity`. Kalau stok terakhir cuma cukup untuk satu request, update lain mengubah nol baris dan menghasilkan `409 INSUFFICIENT_STOCK`. Check constraint SQL juga menolak stok negatif. | Klaim key, seluruh pengurangan stok, dan penyimpanan order/item ada dalam satu transaksi. Kalau stok kurang, transaksi di-rollback. Integration test mengirim dua request dengan key berbeda terhadap stok 15, masing-masing meminta 10, lalu memeriksa hanya satu order tersimpan dan sisa stok 5. Deadlock atau error SQL lain belum dipetakan jadi konflik stok. |
| **B. Dua request mengubah status order yang sama** | `rowversion` order dipakai EF Core sebagai concurrency token. Dua penulis yang membaca versi sama tidak bisa sama-sama menyimpan perubahan. `DbUpdateConcurrencyException` dipetakan jadi `409 ORDER_CONCURRENCY_CONFLICT`. Aturan transisi diperiksa sebelum penyimpanan. | Penyimpanan status dan pengembalian stok saat pembatalan ada dalam satu transaksi; kegagalan pengembalian stok membatalkan keduanya. Tes mencakup penulisan dari DbContext yang sudah stale serta pembatalan/rollback, tapi belum ada tes dua request HTTP status yang dikirim bersamaan. Deadlock atau error SQL di luar exception tadi masuk ke handler umum `500`. |
| **C. Dua request memakai idempotency key yang sama** | `IdempotencyRequests.Key` adalah primary key. Setiap request mencoba menyimpan klaim key di dalam transaksinya; request lain tidak bisa ikut menyimpan key yang sama. Saat SQL memberi error duplikat key (2601/2627), request kedua melakukan rollback, membaca data yang sudah tersimpan, lalu mengembalikan `200` untuk hash yang sama atau `409 IDEMPOTENCY_KEY_REUSED` untuk hash berbeda. | Klaim key, pengurangan stok, order, item, dan tautan key ke order di-commit bersama. Ini mencegah duplikasi untuk **key yang sama**, bukan perebutan stok antara key berbeda. Belum ada tes request bersamaan dengan key yang sama. Lock timeout dan error SQL lain tidak diubah menjadi respons idempotency. Key tidak punya masa berlaku atau scope per customer. |

### Race condition lain

- **Harga produk berubah saat order dibuat:** service membaca produk tanpa tracking sebelum update stok bersyarat. Kalau harga diubah setelah pembacaan itu, order bisa menyimpan harga sebelumnya. Tidak ada pemeriksaan versi harga atau kebijakan snapshot harga untuk seluruh transaksi. Setelah disimpan, harga item itulah yang dipakai untuk menghitung total di endpoint detail.
- **Produk dihapus saat order dibuat:** produk bisa dihapus setelah lookup awal tetapi sebelum update stok bersyarat. Update tersebut akan mengubah nol baris, sehingga service melakukan rollback dan mengembalikan `409 INSUFFICIENT_STOCK` meskipun penyebabnya produk sudah hilang. Foreign key juga mencegah item tanpa produk, sementara produk yang sudah dipakai item order tidak bisa dihapus. Skenario timing ini belum punya tes.

## Penanganan error, logging, dan observabilitas

### Penanganan error

Controller memakai `CreateOrderValidator` untuk request pembuatan order serta model binding ASP.NET Core dan `[ApiController]` untuk input yang tidak bisa dibaca. Model state yang tidak valid dikirim sebagai `ValidationProblemDetails` dengan `400 INVALID_REQUEST`. Kegagalan service yang sudah dipetakan memakai `ProblemDetails` bertipe `application/problem+json`, lengkap dengan status HTTP, `code`, dan `correlationId`. Exception tak terduga dicatat oleh handler global dan dibalas dengan `500 INTERNAL_ERROR` tanpa detail internal database. Tidak semua error SQL terkait concurrency atau constraint diubah menjadi `409` khusus.

### Strategi logging

Aplikasi memakai abstraksi bawaan ASP.NET Core `ILogger<T>` untuk mencatat log terstruktur melalui templat pesan dan properti bernama. Provider console dikonfigurasi untuk menghasilkan JSON, sementara provider debug juga diaktifkan.

- **Konfigurasi per lingkungan:** tingkat log diatur melalui `appsettings.json` dan `appsettings.Development.json`. Konfigurasi utama memakai tingkat `Information` untuk aplikasi, sedangkan Development mengubah kategori `AdayaOrderManagement` menjadi `Debug`.
- **Format keluaran:** log console berupa JSON tanpa indentasi, menyertakan scope, dan memakai waktu UTC dengan format `yyyy-MM-ddTHH:mm:ss.fffZ`.
- **Scope per request:** middleware memakai `ILogger.BeginScope()` untuk menyertakan properti `CorrelationId` selama request diproses.
- **Pencatatan penyelesaian request:** middleware mencatat metode HTTP, path request, kode status respons, dan durasi pemrosesan dalam milidetik. Respons dengan status di bawah `400` dicatat pada tingkat `Information`, `400`–`499` pada `Warning`, dan `500` ke atas pada `Error`.
- **Pencatatan exception terpusat:** handler global mencatat exception yang tidak tertangani, sementara klien menerima respons standar `500 INTERNAL_ERROR` tanpa detail exception internal.

### Pencatatan kejadian bisnis

| Tingkat log | Kejadian |
| --- | --- |
| `Information` | Pembuatan order, perubahan status, pembatalan, dan pengembalian hasil request idempoten yang diulang |
| `Warning` | Stok tidak cukup, konflik idempotency key, transisi status tidak sah, dan konflik perubahan data yang terdeteksi |
| `Error` | Exception tak terduga dan kegagalan request yang tidak tertangani |
| `Debug` | Diagnostik tambahan jika tersedia; tingkat ini diaktifkan untuk kategori aplikasi di Development |

Log keberhasilan pembuatan order dan perubahan status ditempatkan setelah transaksi database berhasil di-commit. Request idempoten yang diulang membaca order yang sudah tersimpan tanpa membuat order baru.

### ID korelasi

Middleware memakai `HttpContext.TraceIdentifier` sebagai ID korelasi dan mengirimkannya melalui header `X-Correlation-ID` di setiap respons. Body error memakai ID yang sama. API tidak menerima ID korelasi dari klien dan tidak menambahkannya ke body respons sukses.

ID tersebut juga disertakan dalam scope log terstruktur agar log penyelesaian request, kejadian bisnis, dan exception dari request yang sama bisa dicocokkan. Misalnya, ID dari respons yang gagal dapat dipakai untuk mencari log request dan alasan penolakan bisnis yang terkait.

### Keamanan data dalam log

Pencatatan aplikasi menghindari penyimpanan payload request yang sensitif, kredensial, connection string, dan alamat pengiriman. Log pembuatan order menyertakan `CustomerId` dan `OrderId`. Detail exception dicatat di sisi server, bukan dikembalikan dalam respons error API.

### Cakupan dan batasan observabilitas

Implementasi saat ini memakai log console terstruktur untuk menelusuri request dalam satu layanan API. Penyimpanan log terpusat, penelusuran lintas layanan (*distributed tracing*), pengumpulan metrik, dan dasbor pemantauan belum termasuk dalam cakupan prototipe.

Pencatatan perubahan status masih perlu dirapikan: beberapa templat pesan di `OrderStatusService` memiliki jumlah argumen yang tidak sesuai, dan pesan untuk pembatalan serta perubahan status lainnya tertukar. Karena itu, rincian log perubahan status belum bisa sepenuhnya diandalkan untuk pemantauan.

## Tes

Jalankan dari root repo:

```powershell
dotnet test AdayaOrderManagement.slnx
```

Unit test mencakup hash payload, validasi create, dan tabel transisi status. Integration test memakai `WebApplicationFactory` dan SQL Server: tes membuat database sementara dengan nama unik, menjalankan migration, mengisi produk, lalu menghapus database tersebut. Skenario yang diuji meliputi perebutan stok, pembatalan dan kegagalan pengembalian stok, penulisan order dengan `rowversion` stale, aturan status huruf besar di database, serta respons detail/error order. `Integration/IdempotencyTests.cs` masih kosong; belum ada tes request bersamaan dengan key yang sama. Kalau SQL Server lokal dengan Windows authentication tidak tersedia, atur `ADAYA_TEST_SQLSERVER_CONNECTION` ke connection string server tes. Akun tes perlu izin membuat dan menghapus database.

## Keputusan desain dan batasan

- Update stok yang atomik dan bersyarat mencegah dua request sama-sama memakai stok terakhir berdasarkan pembacaan lama. Item diproses menurut product ID agar urutan penguncian lebih konsisten. Transaksi membuat perubahan banyak item berhasil atau gagal bersama, tetapi transaksi saja tidak menghilangkan semua konflik atau deadlock.
- Pengecekan `rowversion` melindungi perubahan status tanpa perlu menahan lock sepanjang pemrosesan request. Request yang kalah mendapat respons konflik; aplikasi tidak melakukan retry otomatis.
- Primary key database menjadi penentu akhir untuk idempotency, termasuk saat request diproses di proses aplikasi berbeda. Hash payload menyamakan urutan item, sedangkan key yang unik secara global dan disimpan permanen harus dikelola oleh pemanggil API.
- Data produk perlu diisi lewat jalur di luar API ini. Belum ada aturan authentication atau authorization, dan route bawaan WeatherForecast masih terbuka. Daftar order serta route cancel terpisah yang tertulis di beberapa file `docs/` belum diimplementasikan.
- Ringkasan dari create/update status memakai status huruf besar, sedangkan endpoint detail memakai Title Case. Total detail dihitung saat dibaca, bukan disimpan sebagai total order di database.
