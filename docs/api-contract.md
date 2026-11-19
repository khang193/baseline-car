# API Contract Theo Code Hiện Tại

Đối chiếu ngày 26/10/2026 với code local, bao gồm thay đổi chưa commit. Đây là mô tả implementation, không phải danh sách API dự kiến. Nguồn: `src/CarRental.API/Controllers/`, DTO trong Application và `ai-service/main.py`.

## 1. Quy Ước

- .NET: `http://localhost:5001`; API nghiệp vụ có prefix `/api`.
- Controller .NET giữ tên thuộc tính DTO dạng PascalCase; object vô danh như `{ items, total }`, `{ code, message }` giữ tên chữ thường. Request JSON không phân biệt hoa/thường theo cấu hình web mặc định.
- Enum trong DTO xe được trả dạng số. `BookingDto.Status` là chuỗi vì service gọi `ToString()`.
- Tiền là VNĐ; giá định giá là VNĐ/ngày. Backend dùng `decimal`, FastAPI stub dùng `float`.
- JWT: `Authorization: Bearer <token>`. Role: `Customer`, `Staff`, `Admin`.
- Middleware nghiệp vụ trả `{ "code": "...", "message": "..." }`; `code` có thể null. Model validation trả 400 với `code = "validation_error"`. Không phải mọi lỗi framework/exception đều có định dạng này.
- .NET trả header `X-Correlation-Id`. Booking tạo ID riêng để lưu cùng đơn và truyền sang AI; ID này có thể khác header của request phía ngoài.

### Enum Đang Dùng

| Enum | Giá trị |
|---|---|
| UserRole | 1 Customer, 2 Staff, 3 Admin |
| CarType | 1 Sedan, 2 SUV, 3 Hatchback, 4 MPV, 5 Pickup, 6 Luxury |
| CarStatus | 1 SanSang, 2 DangThue, 3 BaoTri, 4 NgungHoatDong, 5 ChoDuyet |
| BlacklistLevel | 1 CanhBao, 2 Chan |
| BookingStatus | 1 ChoXacNhan, 2 DaXacNhan, 3 DangThue, 4 DaTraXe, 5 DaHuy |
| PaymentStatus | 1 ChuaThanhToan, 2 DaCoc, 3 DaThanhToan, 4 DaHoanTien |
| PaymentMethod | 1 TienMat, 2 ChuyenKhoan, 3 TheNoiDia, 4 ViDienTu, 6 TruCoc (chỉ hệ thống tạo khi quyết toán cọc, `POST .../payments` từ chối với `INVALID_METHOD`) |
| HandoverType | 1 Giao, 2 Nhan |
| DepositTxType | 1 Thu, 2 KhauTru, 3 Hoan |
| ExtensionStatus | 1 ChoDuyet, 2 DaDuyet, 3 TuChoi |

## 2. Health Và Auth

| Method | Route | Quyền | Kết quả thành công |
|---|---|---|---|
| GET | `/health`, `/api/health` | Public | 200 `{ "status": "ok", "service": "CarRental.API" }` |
| POST | `/api/auth/register` | Public | 200 `AuthResponse` |
| POST | `/api/auth/login` | Public | 200 `AuthResponse` |
| GET | `/api/auth/me` | Đăng nhập | 200 `ProfileDto` (`Id`, `FullName`, `Email`, `PhoneNumber`, `Role`, `IdentityNumber`, `Address`, `AvatarKey`, `LoyaltyPoints`, `LifetimePoints`, `Tier`, `DocumentsStatus`) |
| PUT | `/api/auth/me` | Đăng nhập | 204 |
| POST | `/api/auth/me/password` | Đăng nhập | 204; body `CurrentPassword`, `NewPassword` (>= 8) |
| POST | `/api/auth/me/avatar` | Đăng nhập | 200 `{ AvatarKey }`; multipart field `file` (WebP, tối đa 5 MB), ảnh công khai prefix `avatars/` |

Register request: `FullName`, `Email`, `PhoneNumber`, `Password`. Tên/email/điện thoại/mật khẩu bắt buộc; mật khẩu tối thiểu 8 ký tự. Email được trim/lowercase. Email hoặc điện thoại trùng trả 409 `EMAIL_EXISTS` / `PHONE_EXISTS`. Tài khoản mới luôn là Customer.

Login request: `Email`, `Password`; sai thông tin trả 401. `AuthResponse` có `Token`, `FullName`, `Role`, không có `CustomerId`.

Update profile request: `FullName`, `IdentityNumber`, `Address`.
Đổi `IdentityNumber` khác số trên CCCD đã duyệt thì CCCD đó quay lại `ChoDuyet` (phải duyệt lại).

## 3. Xe Và Ảnh

| Method | Route | Quyền | Kết quả thành công |
|---|---|---|---|
| GET | `/api/cars` | Public (xe `ChoDuyet` chỉ Admin/Staff thấy) | 200 `{ "items": [CarDto], "total": 0 }` |
| GET | `/api/cars/{id}` | Public (xe `ChoDuyet`: 404 với khách/Public) | 200 `CarDto`; không có xe: 404 |
| POST | `/api/cars` | Admin/Staff | 200 `CarDto`; Admin tạo -> `SanSang`, Staff tạo -> `ChoDuyet` |
| PUT | `/api/cars/{id}` | Admin/Staff (Staff không đổi giá/duyệt) | 204 |
| POST | `/api/cars/{id}/approve` | Admin | 200 `CarDto`; body tùy chọn `{ BasePricePerDay?, OverridePrice? }` |
| DELETE | `/api/cars/{id}` | Admin | 204 |
| POST | `/api/cars/{carId}/images` | Admin/Staff | 200 `CarImageDto` |
| PUT | `/api/cars/{carId}/images/{imageId}/primary` | Admin/Staff | 204 |
| DELETE | `/api/cars/{carId}/images/{imageId}` | Admin/Staff | 204 |

Filter xe: `CarType`, `MinPrice`, `MaxPrice`, `Status`, `Page=1`, `PageSize=10`, `AvailableFrom`, `AvailableTo`. Giá lọc là `BasePricePerDay`, không phải giá AI. Chưa có filter số chỗ, nhiên liệu hoặc hộp số.

Lọc xe còn trống: truyền cả `AvailableFrom` và `AvailableTo` (ISO date). Loại xe có booking `ChoXacNhan`/`DaXacNhan`/`DangThue` chồng khoảng `[Start, End)` theo cùng quy tắc chống trùng lịch khi đặt. Thiếu một đầu hoặc `AvailableTo <= AvailableFrom`: 400. Filter này không xét `Car.Status`; kết hợp thêm `Status=SanSang` nếu cần.

`CarDto`: `Id`, `CarType`, `Brand`, `Model`, `LicensePlate`, `Seats`, `CarAgeYears`, `BasePricePerDay`, `Status`, `OverridePrice`, `Description`, `Images`, `Transmission?`, `FuelType?`, `ManufactureYear?`.

Enum số: `Transmission` 1=Số sàn, 2=Số tự động; `FuelType` 1=Xăng, 2=Dầu, 3=Điện, 4=Hybrid. `ManufactureYear` 1990–2100 (chỉ hiển thị, giá AI dùng `CarAgeYears`). Ba trường này tùy chọn ở create/update request.

Create request: `CarType`, `Brand`, `Model`, `LicensePlate`, `Seats`, `CarAgeYears`, `BasePricePerDay`, `Description`. DTO create kiểm tra số chỗ 1–16, tuổi xe 0–50, giá > 0; không nên suy ra DTO update có cùng validation.

Update request thêm `Status`, `OverridePrice`. Biển số trùng trả 409 `PLATE_EXISTS`. Xóa xe đang có `Status = DangThue` trả 409 `CAR_IN_USE`; code hiện xóa cứng, chưa kiểm tra mọi booking tương lai của xe.

Upload dùng multipart field `file`, chỉ nhận WebP tối đa 5 MB, kiểm tra extension/MIME/header. `CarImageDto` có `Id`, `FilePath`, `IsPrimary`. `FilePath` hiện là object key dạng `cars/<guid>.webp`, không phải URL công khai đầy đủ. Ảnh đầu là ảnh chính; khi xóa ảnh chính, service chọn ảnh có ID nhỏ nhất còn lại. URL truy cập ảnh và casing phía UI còn cần đối chiếu khi tích hợp.

## 4. Đặt Xe Và Báo Giá

Toàn bộ `BookingsController` yêu cầu đăng nhập.

| Method | Route | Quyền | Kết quả thành công |
|---|---|---|---|
| POST | `/api/bookings` | Đăng nhập | 201 `BookingDto`, có Location |
| POST | `/api/bookings/estimate` | Đăng nhập | 200 `BookingEstimateDto` |
| GET | `/api/bookings/car/{carId}/booked-ranges` | Đăng nhập | 200 danh sách `{ StartDate, EndDate }` |
| GET | `/api/bookings` | Đăng nhập | 200 `{ "items": [BookingDto], "total": 0 }` |
| GET | `/api/bookings/{id}` | Chủ đơn hoặc Admin/Staff | 200 `BookingDto` |
| POST | `/api/bookings/{id}/confirm` | Admin/Staff | 200 `BookingDto` |
| POST | `/api/bookings/{id}/cancel` | Chủ đơn hoặc Admin/Staff | 200 `BookingDto` |

Create request: `CarId`, `StartDate`, `EndDate`, `Note` (tùy chọn). Estimate request: `CarId`, `StartDate`, `EndDate`. Từ wave 4 có thêm các trường tùy chọn về giờ, tài xế, mã giảm giá, giao tận nơi, chi nhánh: xem mục 4g.

```json
{
  "CarId": 1,
  "StartDate": "2026-11-07",
  "EndDate": "2026-11-10",
  "Note": "Nhận xe buổi sáng"
}
```

- Create kiểm tra start < end, ngày bắt đầu không ở quá khứ, xe tồn tại và `SanSang`.
- Số ngày là `(EndDate - StartDate).Days`; chưa có quy tắc làm tròn thuê theo giờ. Nên dùng ngày trọn vẹn khi tích hợp.
- Gọi AI trước transaction; sau đó khóa dòng xe bằng `UPDLOCK, ROWLOCK`, kiểm tra overlap và lưu đơn trong cùng transaction.
- Overlap dùng khoảng nửa mở `[StartDate, EndDate)`, chỉ xét booking chưa xóa thuộc `ChoXacNhan`, `DaXacNhan`, `DangThue`. Hai khoảng sát nhau được chấp nhận.
- Trùng lịch: 409 `BOOKING_OVERLAP`; xe không sẵn sàng: 409 `CAR_NOT_AVAILABLE`.
- Confirm chỉ chuyển `ChoXacNhan` → `DaXacNhan`. Cancel chặn `DangThue`, `DaTraXe`, `DaHuy`; lỗi trạng thái trả 409 `INVALID_STATUS`.
- Estimate hiện chỉ kiểm tra start < end và xe tồn tại; không kiểm tra đầy đủ ngày quá khứ, trạng thái xe hay overlap như create.
- Danh sách booking tự giới hạn Customer theo tài khoản; Admin/Staff xem được toàn bộ. Filter: `CustomerId`, `CarId`, `Status`, `StartDateFrom`, `StartDateTo`, `Page=1`, `PageSize=10`; DTO giới hạn Page >= 1 và PageSize 1–200.

`BookingDto`: `Id`, `CustomerId`, `CustomerName`, `CarId`, `CarName`, `LicensePlate`, `StartDate`, `EndDate`, `RentalDays`, `PredictedPricePerDay`, `FinalPricePerDay`, `TotalPrice`, `IsFallback`, `Status`, `CorrelationId`, `Note`, `CreatedAt`.

`BookingEstimateDto`: `RentalDays`, `BasePricePerDay`, `PredictedPricePerDay`, `FinalPricePerDay`, `TotalPrice`, `IsFallback`.

```text
FinalPricePerDay = clamp(OverridePrice ?? PredictedPrice ?? BasePricePerDay, 400000, 1500000)
TotalPrice = FinalPricePerDay * RentalDays
```

Fallback client trả giá gốc trong `PredictedPrice` cùng `IsFallback = true`; trường giá dự đoán lưu trên booking vì vậy có thể chứa giá gốc, không nhất thiết null.

## 4b. Thanh Toán, Nhận Xe Và Trả Xe

Nguồn: `RentalController.cs`, `RentalService.cs`. Tất cả yêu cầu đăng nhập; Customer chỉ thao tác booking của mình (sai chủ đơn: 403).

| Method | Route | Quyền | Kết quả thành công |
|---|---|---|---|
| POST | `/api/bookings/{bookingId}/payments` | Chủ đơn hoặc Admin/Staff | 201 `PaymentDto` (trạng thái `ChuaThanhToan`) |
| GET | `/api/bookings/{bookingId}/payments` | Chủ đơn hoặc Admin/Staff | 200 `BookingPaymentsDto` |
| POST | `/api/payments/{paymentId}/mark-paid` | Admin/Staff | 200 `PaymentDto` |
| POST | `/api/bookings/{bookingId}/pickup` | Admin/Staff | 200 `BookingDto` (`DaXacNhan` → `DangThue`) |
| POST | `/api/bookings/{bookingId}/return` | Admin/Staff | 200 `ReturnRecordDto` (`DangThue` → `DaTraXe`) |
| GET | `/api/bookings/{bookingId}/return` | Chủ đơn hoặc Admin/Staff | 200 `ReturnRecordDto`, 404 nếu chưa trả |

- Create payment: `Amount` (> 0), `Method` (1–4), `TransactionCode` (tùy chọn). Chỉ cho booking đã xác nhận trở đi (không phải `ChoXacNhan`/`DaHuy`), lỗi: 409 `INVALID_STATUS`. Tổng các khoản chưa hoàn tiền không được vượt số phải trả: 409 `PAYMENT_EXCEEDS_DUE`. Một booking có nhiều payment.
- Số phải trả = `TotalPrice`, hoặc `ReturnRecord.FinalAmount` sau khi đã trả xe.
- Mark-paid: `Status` chỉ nhận 2 (`DaCoc`) hoặc 3 (`DaThanhToan`), chỉ áp dụng cho payment đang `ChuaThanhToan`, ghi `PaidAt`.
- `BookingPaymentsDto`: `Items`, `TotalPrice`, `AmountToPay`, `PaidAmount` (chỉ tính `DaCoc`/`DaThanhToan`), `AmountDue`.
- Return request: `ReturnedAt` (tùy chọn, mặc định bây giờ), `DamageFee` (>= 0), `DamageNote`. Không cho trước `StartDate`. Chỉ nhận khi booking `DangThue`; đã có biên bản: 409 `RETURN_EXISTS`.
- Công thức: `LateDays = max(0, ngày trả − ngày EndDate)` (theo ngày lịch); `LateFee = LateDays × FinalPricePerDay`; `FinalAmount = TotalPrice + LateFee + DamageFee`. `StaffId` lấy từ JWT.
- `ReturnRecordDto`: `BookingId`, `ReturnedAt`, `EndDate`, `LateDays`, `LateFee`, `DamageFee`, `DamageNote`, `TotalPrice`, `FinalAmount`, `StaffId`.
- Chưa tự đổi `Car.Status` theo vòng đời thuê, chưa có refund/`DaHoanTien` qua API, chưa có hợp đồng PDF.

## 4c. Báo Cáo (chỉ Admin)

| Method | Route | Trả về |
|---|---|---|
| GET | `/api/reports/revenue?from=&to=` | 200 `{ totalRevenue, totalBookings, byMonth: [{ month: "2026-11", revenue, bookings }] }` |
| GET | `/api/reports/occupancy?from=&to=` | 200 `{ totalCars, avgOccupancyRate, byCar: [{ carId, licensePlate, occupancyRate }] }` |

- JSON trả về dạng PascalCase như các API khác (`TotalRevenue`, `ByMonth`, `Month`...); bảng trên viết camelCase cho dễ đọc.
- `from`/`to` là ngày, gồm cả hai đầu; mặc định 30 ngày gần nhất; `from > to` hoặc quá 366 ngày: 400.
- Doanh thu = tổng `Payment` có `Status` là `DaCoc`/`DaThanhToan` và `PaidAt` trong khoảng, nhóm theo tháng của `PaidAt`. `bookings` đếm booking khác nhau có thu tiền. Không trừ hoàn tiền; payment `ChuaThanhToan` không tính.
- Lấp đầy = số ngày thuê (booking `DaXacNhan`/`DangThue`/`DaTraXe`, cắt theo khoảng) / số ngày trong khoảng, giá trị 0..1, tính cho xe chưa xóa và khác `NgungHoatDong`.

## 4d. Phân Quyền, Nhật Ký, Danh Sách Đen (Wave 1)

Ma trận quyền (3 vai trò, không có kế toán):

| Chức năng | Customer | Staff | Admin |
|---|---|---|---|
| Xem/đặt xe, đơn của mình | có | có | có |
| Quản lý đơn, trả xe, ảnh xe | không | có | có |
| Thêm xe | không | có (xe vào `ChoDuyet`) | có (vào `SanSang`) |
| Sửa xe | không | có, không đổi `BasePricePerDay`/`OverridePrice`, không đổi trạng thái vào/ra `ChoDuyet` | có |
| Duyệt xe, xóa xe | không | không | có |
| Danh sách đen: xem, `check` | không | có | có |
| Danh sách đen: thêm/gỡ | không | không | có |
| Người dùng, nhật ký, báo cáo | không | không | có |

Xe `ChoDuyet` bị ẩn khỏi danh sách/chi tiết công khai và không đặt được. Giá `BasePricePerDay` Staff nhập khi tạo chỉ là đề xuất. `POST /api/cars/{id}/approve` đặt giá nếu có (phải > 0) rồi chuyển xe sang `SanSang`.

Mỗi request có JWT đều được kiểm tra lại với DB: tài khoản bị khóa, bị xóa hoặc đổi vai trò so với claim token thì 401 ngay (không cần chờ token hết hạn). Đăng nhập tài khoản bị khóa: 403 `ACCOUNT_LOCKED`.

| Method | Route | Quyền | Kết quả |
|---|---|---|---|
| GET | `/api/admin/users?Role=&Search=&IsLocked=&Page=&PageSize=` | Admin | 200 `{ items: [UserSummaryDto], total }` |
| POST | `/api/admin/users` | Admin | 201 `UserSummaryDto`; tạo tài khoản Staff (`FullName`, `Email`, `PhoneNumber`, `Password` >= 8) |
| PUT | `/api/admin/users/{id}/role` | Admin | 204; body `{ Role }` (1/2/3) |
| POST | `/api/admin/users/{id}/lock` | Admin | 204; body `{ Reason }` bắt buộc |
| POST | `/api/admin/users/{id}/unlock` | Admin | 204 |
| GET | `/api/admin/audit-logs?EntityType=&EntityId=&ActorId=&Action=&From=&To=&Page=&PageSize=` | Admin | 200 `{ items: [AuditLogDto], total }` |
| GET | `/api/admin/blacklist?ActiveOnly=&Page=&PageSize=` | Admin/Staff | 200 `{ items: [BlacklistEntryDto], total }` |
| GET | `/api/admin/blacklist/check?customerId=` | Admin/Staff | 200 `{ Level: 1/2/null, Reason }` |
| POST | `/api/admin/blacklist` | Admin | 201 `BlacklistEntryDto`; body `CustomerId?`, `IdentityNumber?`, `PhoneNumber?`, `Level`, `Reason` |
| POST | `/api/admin/blacklist/{id}/remove` | Admin | 204; body `{ Reason }` bắt buộc |

- `AuditLogDto`: `Id`, `ActorId`, `ActorName`, `ActorRole`, `Action` (vd `Car.Created`, `Car.Updated.Status`, `Booking.Deleted`), `EntityType`, `EntityId`, `Changes` (JSON cũ/mới; `PasswordHash` che thành `***`, cắt 8000 ký tự), `IpAddress`, `CreatedAt`. Ghi tự động cho `Car`, `Booking`, `Payment`, `ReturnRecord`, `Customer`, `BlacklistEntry`, `CustomerDocument`, `LoyaltyTransaction`.
- Danh sách đen khớp theo `CustomerId`, CCCD hoặc số điện thoại; mức cao nhất thắng. `Chan`: tạo đơn trả 403 `CUSTOMER_BLOCKED`, duyệt đơn trả 409 `CUSTOMER_BLOCKED` (kèm lý do). `CanhBao`: chỉ hiển thị cảnh báo.

Mã lỗi mới (`{ code, message }`; `ValidationAppException` và `ForbiddenException` nay cũng trả `code`):

| HTTP | code | Khi nào |
|---|---|---|
| 403 | `PRICE_ADMIN_ONLY` | Staff đổi `BasePricePerDay`/`OverridePrice` |
| 403 | `CAR_APPROVAL_ADMIN_ONLY` | Staff chuyển trạng thái vào/ra `ChoDuyet` |
| 409 | `CAR_NOT_PENDING` | Duyệt xe không ở `ChoDuyet` |
| 400 | `INVALID_PRICE` | Giá duyệt <= 0 |
| 403 | `ACCOUNT_LOCKED` | Đăng nhập tài khoản bị khóa |
| 403/409 | `CUSTOMER_BLOCKED` | Khách bị chặn đặt/duyệt đơn |
| 409 | `LAST_ADMIN` | Hạ quyền/khóa Admin cuối cùng |
| 409 | `SELF_CHANGE` / `SELF_LOCK` | Tự đổi vai trò / tự khóa mình |
| 400 | `REASON_REQUIRED` | Thiếu lý do khóa/gỡ danh sách đen/thêm danh sách đen/từ chối giấy tờ/điều chỉnh điểm |
| 400 | `INVALID_ROLE` | Vai trò không hợp lệ |
| 409 | `EMAIL_EXISTS` / `PHONE_EXISTS` | Trùng khi tạo Staff |
| 400 | `BLACKLIST_KEY_REQUIRED` / `INVALID_LEVEL` | Thiếu khóa nhận diện / mức độ sai |
| 409 | `ALREADY_REMOVED` | Bản ghi danh sách đen đã gỡ |

## 4e. Khách Hàng, Giấy Tờ, Điểm Thưởng (Wave 2)

Enum: `DocumentType` 1 Cccd, 2 BangLai; `DocumentStatus` 1 ChoDuyet, 2 DaDuyet, 3 TuChoi; `DocumentsOverallStatus` 1 ChuaNop, 2 ChoDuyet, 3 TuChoi, 4 DaDuyet (đã duyệt đủ CCCD và bằng lái); `MemberTier` 1 ThanhVien, 2 Bac, 3 Vang, 4 KimCuong.

| Method | Route | Quyền | Kết quả |
|---|---|---|---|
| GET | `/api/me/documents` | Đăng nhập | 200 `[DocumentDto]` |
| POST | `/api/me/documents` | Đăng nhập | 201 `DocumentDto`; multipart `Type`, `Number` (12 số), `ExpiryDate` (bằng lái, phải ở tương lai), `LicenseClass?`, `Front`, `Back?` (WebP). Nộp lại thay hồ sơ cũ cùng loại (file cũ vẫn giữ) |
| GET | `/api/admin/documents?Status=&Page=&PageSize=` | Admin/Staff | 200 `{ items: [DocumentDto], total }` |
| POST | `/api/admin/documents/{id}/approve` | Admin/Staff | 200 `DocumentDto`; duyệt CCCD đồng bộ `Customer.IdentityNumber` |
| POST | `/api/admin/documents/{id}/reject` | Admin/Staff | 200 `DocumentDto`; body `{ Reason }` bắt buộc |
| GET | `/api/documents/{id}/file?side=front\|back` | Chủ hồ sơ, Admin/Staff | 200 `image/webp`, `Cache-Control: private, no-store` |
| GET | `/api/me/loyalty` | Đăng nhập | 200 `LoyaltyDto` (`Points`, `LifetimePoints`, `Tier`, `NextTier`, `PointsToNextTier`, `History`) |
| POST | `/api/admin/customers/{id}/loyalty/adjust` | Admin | 200 `LoyaltyDto`; body `Points` (khác 0), `Note` bắt buộc |
| GET | `/api/admin/customers?Search=&Tier=&DocStatus=&Blacklisted=&Page=&PageSize=` | Admin/Staff | 200 `{ items: [CustomerListItemDto], total }` |
| GET | `/api/admin/customers/{id}` | Admin/Staff | 200 `CustomerDetailDto`; Staff nhận bản rút gọn (`IdentityNumber`, `Address`, `LockReason` null, `Loyalty.History` rỗng) |

- `DocumentDto` không bao giờ trả object key; ảnh chỉ lấy qua endpoint `file`. Ảnh giấy tờ nằm ở prefix `private/documents/{customerId}` của MinIO, không có URL công khai. Chỉ `cars/` và `avatars/` được mở đọc công khai.
- Nhận xe (`POST /api/bookings/{id}/pickup`) bị chặn 409 `DOCUMENTS_NOT_VERIFIED` nếu khách chưa có CCCD và bằng lái đã duyệt (bằng lái còn hạn đến hết ngày trả xe).
- Điểm = 1% giá trị đơn (`ReturnRecord.FinalAmount`, làm tròn xuống), cộng một lần khi trả xe. Hạng theo điểm tích lũy: Bạc từ 100.000, Vàng từ 300.000, Kim cương từ 800.000. Điều chỉnh chỉ làm đổi số dư; điểm tích lũy chỉ tăng.
- Khóa/mở khóa và danh sách đen khách dùng lại `/api/admin/users/{id}/lock|unlock` và `/api/admin/blacklist`.
- `POST /api/bookings/estimate` và `GET /api/bookings/car/{carId}/booked-ranges` trả 404 với khách khi xe đang `ChoDuyet`.
- `POST /api/cars/{id}/approve` trả 200 `CarDto`.

| HTTP | code | Khi nào |
|---|---|---|
| 409 | `DOCUMENTS_NOT_VERIFIED` | Nhận xe khi giấy tờ chưa xác minh |
| 400 | `WRONG_PASSWORD` / `SAME_PASSWORD` | Mật khẩu hiện tại sai / mật khẩu mới trùng cũ |
| 400 | `INVALID_DOCUMENT_TYPE` / `INVALID_DOCUMENT_NUMBER` / `INVALID_EXPIRY` | Loại giấy tờ, số 12 chữ số, hạn bằng lái |
| 400 | `INVALID_SIDE` | `side` khác front/back |
| 403 | `SELF_REVIEW` | Staff/Admin duyệt giấy tờ của chính mình |
| 409 | `INVALID_STATUS` | Duyệt/từ chối hồ sơ không ở `ChoDuyet` |
| 400 | `INVALID_POINTS` | Điều chỉnh 0 điểm |
| 409 | `INSUFFICIENT_POINTS` | Trừ quá số dư |

## 4f. Bàn Giao, Tiền Cọc, Gia Hạn, Lịch Sử Thuê (Wave 3)

`Car` có thêm `SecurityDeposit` (tiền cọc bảo đảm, 0 = không cọc). Chỉ Admin đặt/đổi được (`PRICE_ADMIN_ONLY`); `Create/UpdateCarRequest.SecurityDeposit` là tùy chọn, bỏ trống khi sửa là giữ nguyên. Tiền cọc không phải doanh thu và không nằm trong `Payments`, trừ khoản khấu trừ (`PaymentMethod = TruCoc`).

### Bàn giao (biên bản giao/nhận xe)

| Method | Path | Quyền | Ghi chú |
|---|---|---|---|
| GET | `/api/bookings/{id}/handovers` | Chủ đơn, Staff, Admin | Danh sách biên bản kèm `Photos[{Id}]` |
| POST | `/api/bookings/{id}/handovers` | Staff, Admin | Body `{Type: Giao/Nhan, Odometer, FuelLevel 0-100, ConditionNote}`; 201 |
| POST | `/api/handovers/{id}/photos` | Staff, Admin | multipart `file` (WebP, tối đa 5 MB, tối đa 10 ảnh); lưu `private/handovers/{bookingId}` |
| GET | `/api/handover-photos/{id}` | Chủ đơn, Staff, Admin | Ảnh, `Cache-Control: no-store` |

Lỗi: `HANDOVER_REQUIRED` (409, nhận xe khi chưa có biên bản Giao hoặc trả xe khi chưa có biên bản Nhan), `HANDOVER_EXISTS` (409, mỗi đơn một biên bản mỗi loại), `ODOMETER_DECREASED` (400), `HANDOVER_PHOTO_LIMIT`, `INVALID_STATUS`.

### Tiền cọc

| Method | Path | Quyền | Ghi chú |
|---|---|---|---|
| GET | `/api/bookings/{id}/deposit` | Chủ đơn, Staff, Admin | `{Required, Collected, Held, Deducted, Refunded, Status (ChuaThu/DangGiu/DaQuyetToan), Items}` |
| POST | `/api/bookings/{id}/deposit/collect` | Staff, Admin | `{Method}`, đơn `DaXacNhan` |
| POST | `/api/bookings/{id}/deposit/settle` | Staff, Admin | `{RefundMethod, Note}`, đơn `DaTraXe` hoặc `DaHuy`; khấu trừ `min(đang giữ, còn nợ)` bằng Payment `TruCoc`, hoàn phần còn lại; đơn `DaHuy` hoàn toàn bộ |

Lỗi: `DEPOSIT_REQUIRED` (409, nhận xe khi xe có cọc mà chưa thu), `DEPOSIT_NOT_REQUIRED`, `DEPOSIT_COLLECTED`, `DEPOSIT_NOT_COLLECTED`, `DEPOSIT_SETTLED` (409), `INVALID_METHOD` (400).

### Gia hạn

| Method | Path | Quyền | Ghi chú |
|---|---|---|---|
| GET | `/api/bookings/{id}/extend/check?newEndDate=` | Chủ đơn, Staff, Admin | `{OldEndDate, NewEndDate, ExtraDays, ExtraAmount, NewTotalPrice, Available}` |
| POST | `/api/bookings/{id}/extend` | Chủ đơn, Staff, Admin | `{NewEndDate}`; tạo yêu cầu `ChoDuyet` |
| GET | `/api/bookings/{id}/extensions` | Chủ đơn, Staff, Admin | Lịch sử yêu cầu |
| GET | `/api/booking-extensions/pending` | Staff, Admin | Yêu cầu chờ duyệt |
| POST | `/api/booking-extensions/{id}/approve` | Staff, Admin | Kiểm tra lại trùng lịch, cập nhật `EndDate`, `TotalPrice += ExtraAmount` |
| POST | `/api/booking-extensions/{id}/reject` | Staff, Admin | `{Reason}` bắt buộc |

Phụ thu = số ngày thêm x `FinalPricePerDay` đã chốt của đơn (`PricingCalculator.ExtensionCharge`, không gọi lại AI). Lỗi: `EXTENSION_UNAVAILABLE`, `EXTENSION_PENDING`, `EXTENSION_STALE` (409), `EXTENSION_TOO_LONG` (tối đa 30 ngày/lần), `INVALID_NEW_END_DATE`, `REASON_REQUIRED` (400), `INVALID_STATUS`.

Lịch sử thuê và "Đặt lại" là phía frontend: `GET /api/bookings` có sẵn `CarId`; nút "Đặt lại" mở `/booking/{carId}?start=&end=` với cùng số ngày.

## 4g. Mã Giảm Giá, Phụ Phí, Thuê Theo Giờ, Giao Tận Nơi, Chi Nhánh (Wave 4)

Enum số trong request và response: `RentalMode` (1 TheoNgay, 2 TheoGio), `CouponType` (1 PhanTram, 2 SoTien), `SurchargeType` (1 CoDinh, 2 TheoNgay, 3 PhanTram). Dòng phí trả về là chuỗi `Kind`: `PhuPhi`, `GiamGia`, `TaiXe`, `GiaoNhan`, `MotChieu`.

### Đặt xe và báo giá (mở rộng mục 4, các trường mới đều tùy chọn)

`CreateBookingRequest` và `EstimateBookingRequest` thêm: `Mode`, `WithDriver`, `CouponCode`, `DeliveryAddress`, `ReturnAddress`, `PickupBranchId`, `ReturnBranchId`.

- `BookingDto` thêm: `Mode`, `WithDriver`, `DeliveryAddress`, `ReturnAddress`, `PickupBranchId`, `ReturnBranchId`, `Charges [{Kind, Name, Amount}]`. `BookingEstimateDto` thêm `RentalAmount`, `Charges`.
- **`TotalPrice` là tổng cuối** = tiền thuê + phụ phí tự động + tài xế + phí giao/nhận + phí trả khác chi nhánh - giảm giá (`RentalAmount + tổng Charges.Amount`, giảm giá là số âm). `FinalPricePerDay` vẫn là giá chốt một ngày.
- Thuê theo giờ (`Mode = 2`): 4 đến 12 giờ, tiền thuê = số giờ làm tròn lên x `Car.PricePerHour` (mặc định giá ngày chia 8). Sai khoảng: 400 `HOURLY_RANGE`. Đơn theo giờ không gia hạn được (409 `HOURLY_NOT_EXTENDABLE`); trễ tính theo giờ, `LateDays = 0`.
- Phí cố định (section `Pricing` của `appsettings.json`): `DriverFeePerDay`, `DriverFeePerHour`, `DeliveryFee` (mỗi chiều giao hoặc nhận tận nơi), `OneWayFee` (trả khác chi nhánh). Không có bản đồ nên không tính theo km.
- Mã giảm giá áp sau cùng, trên (tiền thuê + các khoản phí khác). Lỗi 400: `COUPON_INVALID`, `COUPON_NOT_STARTED`, `COUPON_EXPIRED`, `COUPON_MIN_ORDER`; 409: `COUPON_EXHAUSTED` (hết lượt), `COUPON_ALREADY_USED` (vượt số lần mỗi khách). Báo giá (estimate) không kiểm số lần mỗi khách.
- Lượt dùng mã trừ bằng `UPDATE` có điều kiện trong transaction tạo đơn (chặn đua); hủy đơn trả lại lượt.
- Xe thuộc chi nhánh (`Car.BranchId`); `PickupBranchId` khác chi nhánh xe: 400 `BRANCH_MISMATCH`; chi nhánh không tồn tại hoặc ngừng: 400 `BRANCH_INVALID`; địa chỉ quá 300 ký tự: 400 `ADDRESS_TOO_LONG`. Trả xe sang chi nhánh khác thì khi nhận xe xe chuyển về chi nhánh trả.
- `CarDto` thêm `BranchId`, `PricePerHour`; `Create/UpdateCarRequest` thêm `BranchId` và `PricePerHour` (chỉ Admin đặt giá giờ: `PRICE_ADMIN_ONLY`; bỏ trống khi sửa là giữ nguyên); `GET /api/cars?BranchId=` lọc theo chi nhánh.

### Endpoint mới

| Method | Path | Quyền | Ghi chú |
|---|---|---|---|
| GET | `/api/coupons/check?code=&orderAmount=` | Đăng nhập | `{Code, Type, Value, MaxDiscount, MinOrderAmount, ValidTo}` hoặc lỗi `COUPON_*` |
| GET/POST | `/api/admin/coupons` | Admin | Body tạo: `{Code, Type, Value, MaxDiscount, MinOrderAmount, ValidFrom, ValidTo, UsageLimit, PerCustomerLimit, IsActive}`; mã duy nhất (409 `COUPON_CODE_EXISTS`) |
| PUT | `/api/admin/coupons/{id}` | Admin | Không đổi `Code` (400 `COUPON_CODE_IMMUTABLE`); `UsageLimit` không nhỏ hơn đã dùng |
| GET/POST | `/api/admin/surcharges` | Admin | `{Name, Type, Amount, AutoApply, IsActive}`; `AutoApply` tự cộng vào mọi đơn mới |
| PUT | `/api/admin/surcharges/{id}` | Admin | |
| GET | `/api/branches` | Công khai | Chi nhánh đang hoạt động |
| GET/POST | `/api/admin/branches` | Admin | `{Name, Address, Phone, IsActive}` |
| PUT | `/api/admin/branches/{id}` | Admin | Ngừng chi nhánh còn xe: 409 `BRANCH_HAS_CARS` |

## 5. .NET → FastAPI

Nguồn: `IAiPricingClient.cs`, `AiPricingClient.cs`, `ai-service/main.py`. Trong Compose, .NET gọi `http://ai:5002/predict-price`.

### POST /predict-price

Payload thực tế do .NET gửi:

```json
{
  "car_type": 1,
  "car_age_years": 3,
  "seats": 5,
  "rental_days": 3,
  "base_price_per_day": 500000,
  "start_date": "2026-11-07"
}
```

FastAPI hiện chỉ khai báo `car_type`, `seats`, `car_age_years`, `base_price_per_day`. Hai trường `rental_days` và `start_date` đang bị bỏ qua theo cấu hình Pydantic mặc định, chưa tham gia dự đoán.

Response stub hiện tại:

```json
{
  "predicted_price_per_day": 500000,
  "confidence": 0.0,
  "model_used": "stub_fallback"
}
```

- Chưa có model ML; giá trả về bằng `base_price_per_day`.
- .NET chỉ đọc `predicted_price_per_day`; chưa dùng `confidence` hoặc `model_used`. Stub trả 200 nên .NET hiện coi là thành công, `IsFallback = false`.
- Timeout mặc định 2 giây **mỗi lần gọi**, retry 2 lần: tối đa 3 lần gọi. Backoff 200 ms rồi 400 ms; không đợi sau lần cuối. Đây không phải deadline tổng 2 giây.
- Client retry khi HTTP không thành công, lỗi mạng hoặc timeout. JSON sai cú pháp thì dừng retry; thiếu trường giá thì tiếp tục retry. Hết lượt thì trả giá gốc và `IsFallback = true`.
- Header `X-Correlation-Id` ưu tiên ID trong request application, sau đó mới lấy header HTTP hiện tại. FastAPI stub chưa đọc/log header này.

### GET /health

200 `{ "status": "ok", "service": "ai-service" }`. Health hiện không kiểm tra model hay Gemini.

### POST /chat

FastAPI request: `message`, `correlation_id` (tùy chọn). Response: `reply`, `correlation_id`. Thiếu key hoặc key bằng placeholder hiện tại: 503 với `detail`. Có key: echo `[Stub] Bạn hỏi: ...`; chưa gọi Gemini, chưa có lịch sử hội thoại hoặc ngữ cảnh xe.

## 4i. Đợt 5: lịch xe, bảo trì, dashboard, xuất file, nhập xe

JSON PascalCase, lỗi `{ code, message }`. Enum dạng số.

| Route | Quyền | Ghi chú |
|---|---|---|
| `GET /api/maintenance?carId=&status=` | Staff, Admin | `Cost` chỉ Admin thấy (Staff nhận null). |
| `GET /api/maintenance/due?withinDays=30` | Staff, Admin | `{ Overdue, Upcoming, ByOdometer }`, tính lúc đọc. Km lấy từ `HandoverRecord.Odometer` lớn nhất của xe, nhắc khi còn dưới 500 km. |
| `POST /api/maintenance` | Staff, Admin | `{ CarId, Type(1 BaoDuong,2 DangKiem,3 BaoHiem), DueDate, DueOdometer?, Note? }` |
| `POST /api/maintenance/{id}/start` | Staff, Admin | `{ Force }`. 409 `CAR_IN_USE` (xe đang thuê), 409 `MAINTENANCE_BOOKING_OVERLAP` (có đơn chồng, cần `Force=true`), 409 `MAINTENANCE_STATE`. Đặt xe `BaoTri`. |
| `POST /api/maintenance/{id}/complete` | Staff, Admin | `{ Cost?, Note?, NextDueDate? }`. Xe về `SanSang` chỉ khi đang `BaoTri` (không ghi đè `NgungHoatDong`); tạo lịch kế tiếp nếu có `NextDueDate`. |
| `POST /api/maintenance/{id}/cancel` | Staff, Admin | Chỉ lịch KeHoach. |
| `GET /api/cars/calendar?from=&to=&branchId=` | Staff, Admin | Tối đa 62 ngày. `[{ CarId, Name, LicensePlate, Status, Items:[{ Kind: Booking/Maintenance, Id, Start, End, Label, Status }] }]`. |
| `GET /api/dashboard/summary` | Staff, Admin | `CarsByStatus, RentingCount, DueTodayOrTomorrow, Overdue, PendingConfirm, PendingDocuments, MaintenanceDue, TodayRevenue` (null với Staff). |
| `GET /api/reports/revenue/export?format=xlsx\|pdf&from=&to=` | Admin | File tải về. `format` khác thì 400 `INVALID_FORMAT`. |
| `GET /api/reports/occupancy/export?format=&from=&to=` | Admin | Như trên. |
| `GET /api/bookings/export?...filter` | Staff, Admin | Xlsx, tối đa 5.000 dòng. |
| `GET /api/cars/import/template` | Staff, Admin | File xlsx mẫu. |
| `POST /api/cars/import?dryRun=true\|false` | Staff, Admin | multipart `file` .xlsx, tối đa 2 MB và 500 dòng. Trả `{ Total, Valid, Errors:[{ Row, Column, Message }], Committed }`. Có lỗi thì không ghi dòng nào. Staff nhập thì xe `ChoDuyet`, Admin thì `SanSang`; Staff không được nhập `SecurityDeposit`/`PricePerHour`. Lỗi: 400 `IMPORT_INVALID_FILE`, `IMPORT_FILE_TOO_LARGE`, `IMPORT_TOO_MANY_ROWS`. |

`GET /api/reports/revenue` thêm `ByMethod: [{ Method, Revenue, Payments }]`. Doanh thu không gồm tiền cọc.

## 6. Chức Năng Chưa Có Backend

- Chưa có `/api/ai/quote`: báo giá đang đi qua `/api/bookings/estimate` có JWT.
- Chưa có `/api/ai/chat`: `ChatWidget.jsx` đã gọi route này nhưng chưa có controller/client tương ứng ở .NET.
- Chưa có khôi phục mật khẩu.
- Payment, nhận/trả xe đã có API (mục 4b); báo cáo đã có API (mục 4c).

Khi triển khai AI thật cần thống nhất bổ sung feature thuê, validation, hành vi model chưa sẵn sàng, ý nghĩa confidence, nguồn ngữ cảnh chatbot và schema session trước khi nối xuyên suốt.
