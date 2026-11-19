// Nạp dữ liệu mẫu (DEMO) cho Car Rental. Chạy tay, idempotent. KHÔNG dùng cho production.
// Dùng: dotnet run --project tools/CarRental.DemoSeed -- "<connection string>"
//   hoặc đặt biến môi trường ConnectionStrings__Default.
using CarRental.Domain.Common;
using CarRental.Domain.Modules.Bookings;
using CarRental.Domain.Modules.Branches;
using CarRental.Domain.Modules.Pricing;
using CarRental.Domain.Modules.Cars;
using CarRental.Application.Modules.Customers;
using CarRental.Domain.Modules.Customers;
using CarRental.Infrastructure.Persistence;
using CarRental.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

const string DemoPassword = "Demo@12345";

var conn = args.FirstOrDefault() ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default");
if (string.IsNullOrWhiteSpace(conn))
{
    Console.Error.WriteLine("Thiếu connection string. Truyền làm tham số đầu tiên hoặc đặt ConnectionStrings__Default.");
    return 1;
}

var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(conn).Options;
using var db = new ApplicationDbContext(options);
db.Database.Migrate();

var hasher = new BCryptPasswordHasher(); // cùng thuật toán với AuthService (BCrypt, work factor 12)
var today = DateTime.UtcNow.Date;
int addedUsers = 0, addedCars = 0, addedBookings = 0;

// ---------- Tài khoản ----------
Customer EnsureUser(string name, string email, string phone, UserRole role, string? cccd, string? addr)
{
    var u = db.Customers.FirstOrDefault(c => c.Email == email || c.PhoneNumber == phone);
    if (u != null) return u;
    u = new Customer
    {
        FullName = name, Email = email, PhoneNumber = phone, Role = role,
        PasswordHash = hasher.Hash(DemoPassword), IdentityNumber = cccd, Address = addr
    };
    db.Customers.Add(u);
    db.SaveChanges();
    addedUsers++;
    return u;
}

EnsureUser("Quản trị viên Demo", "admin@demo.local", "0900000099", UserRole.Admin, null, null);
var staff = EnsureUser("Nhân viên Demo", "staff@demo.local", "0900000011", UserRole.Staff, null, null);
var kh1 = EnsureUser("Nguyễn Văn An", "an.nguyen@demo.local", "0912000001", UserRole.Customer, "079200000001", "12 Lê Lợi, Quận 1, TP.HCM");
var kh2 = EnsureUser("Trần Thị Bình", "binh.tran@demo.local", "0912000002", UserRole.Customer, "001200000002", "25 Hai Bà Trưng, Hoàn Kiếm, Hà Nội");
var kh3 = EnsureUser("Lê Hoàng Cường", "cuong.le@demo.local", "0912000003", UserRole.Customer, "048200000003", "88 Bạch Đằng, Hải Châu, Đà Nẵng");

// ---------- Xe ----------
Car EnsureCar(CarType type, string brand, string model, string plate, int seats, int age, decimal price, CarStatus status, string desc)
{
    var c = db.Cars.FirstOrDefault(x => x.LicensePlate == plate);
    if (c != null) return c;
    c = new Car
    {
        CarType = type, Brand = brand, Model = model, LicensePlate = plate, Seats = seats,
        CarAgeYears = age, BasePricePerDay = price, Status = status, Description = desc
    };
    db.Cars.Add(c);
    db.SaveChanges();
    addedCars++;
    return c;
}

var vios     = EnsureCar(CarType.Sedan,     "Toyota",     "Vios",      "51A-100.01", 5, 2, 500_000,   CarStatus.SanSang, "Sedan phổ thông, tiết kiệm nhiên liệu.");
EnsureCar(CarType.Sedan,                    "Hyundai",    "Accent",    "51A-100.02", 5, 3, 480_000,   CarStatus.SanSang, "Sedan gọn gàng, phù hợp đi phố.");
var mazda3   = EnsureCar(CarType.Sedan,     "Mazda",      "Mazda3",    "30A-100.03", 5, 1, 700_000,   CarStatus.SanSang, "Sedan thiết kế đẹp, lái thú vị.");
var fortuner = EnsureCar(CarType.SUV,       "Toyota",     "Fortuner",  "51K-100.04", 7, 2, 1_100_000, CarStatus.SanSang, "SUV 7 chỗ máy dầu, đi đường dài.");
var crv      = EnsureCar(CarType.SUV,       "Honda",      "CR-V",      "30G-100.05", 7, 2, 950_000,   CarStatus.SanSang, "SUV rộng rãi, vận hành êm.");
EnsureCar(CarType.SUV,                      "Kia",        "Seltos",    "43A-100.06", 5, 1, 750_000,   CarStatus.SanSang, "SUV cỡ nhỏ, nhiều tiện nghi.");
EnsureCar(CarType.Hatchback,                "Hyundai",    "Grand i10", "51A-100.07", 5, 3, 400_000,   CarStatus.SanSang, "Hatchback nhỏ gọn, giá rẻ.");
EnsureCar(CarType.Hatchback,                "Kia",        "Morning",   "29A-100.08", 5, 4, 400_000,   CarStatus.BaoTri,  "Hatchback đô thị (đang bảo trì).");
var xpander  = EnsureCar(CarType.MPV,       "Mitsubishi", "Xpander",   "51A-100.09", 7, 1, 650_000,   CarStatus.SanSang, "MPV 7 chỗ cho gia đình.");
EnsureCar(CarType.MPV,                      "Toyota",     "Innova",    "51A-100.10", 7, 4, 600_000,   CarStatus.BaoTri,  "MPV 7 chỗ bền bỉ (đang bảo trì).");
var ranger   = EnsureCar(CarType.Pickup,    "Ford",       "Ranger",    "51C-100.11", 5, 2, 900_000,   CarStatus.SanSang, "Bán tải mạnh mẽ, chở hàng và đi off-road.");
var merc     = EnsureCar(CarType.Luxury,    "Mercedes",   "C300 AMG",  "30K-100.12", 5, 1, 1_500_000, CarStatus.SanSang, "Sedan hạng sang, dịp quan trọng.");

// ---------- Booking (khoá idempotent: CorrelationId) ----------
// Mỗi xe chỉ có các khoảng ngày không giao nhau.
Booking? EnsureBooking(string key, Customer cust, Car car, int startOff, int endOff, BookingStatus status, string? note)
{
    if (db.Bookings.Any(b => b.CorrelationId == key)) return null;
    var start = today.AddDays(startOff);
    var end = today.AddDays(endOff);
    var price = Math.Clamp(car.OverridePrice ?? car.BasePricePerDay, 400_000m, 1_500_000m);
    var b = new Booking
    {
        CustomerId = cust.Id, CarId = car.Id, StartDate = start, EndDate = end,
        PredictedPricePerDay = null, FinalPricePerDay = price, IsFallback = true,
        TotalPrice = price * (end - start).Days, Status = status, CorrelationId = key, Note = note
    };
    db.Bookings.Add(b);
    db.SaveChanges();
    addedBookings++;
    return b;
}

void AddPayment(Booking b, decimal amount, PaymentMethod m, PaymentStatus s, string code, int paidDaysAgo)
{
    db.Payments.Add(new Payment
    {
        BookingId = b.Id, Amount = amount, Method = m, Status = s, TransactionCode = code,
        PaidAt = s is PaymentStatus.DaCoc or PaymentStatus.DaThanhToan ? DateTime.UtcNow.AddDays(-paidDaysAgo) : null
    });
    db.SaveChanges();
}

void AddReturn(Booking b, DateTime returnedAt, decimal damage, string? damageNote)
{
    var (lateDays, lateFee, final) = CarRental.Application.Modules.Bookings.RentalService.CalculateReturn(
        b.EndDate, returnedAt, b.FinalPricePerDay, b.TotalPrice, damage);
    db.ReturnRecords.Add(new ReturnRecord
    {
        BookingId = b.Id, ReturnedAt = returnedAt, LateDays = lateDays, LateFee = lateFee,
        DamageFee = damage, DamageNote = damageNote, FinalAmount = final, StaffId = staff.Id
    });
    db.SaveChanges();
}

// 1) Chờ xác nhận (chưa có thanh toán)
EnsureBooking("demo-seed-01", kh1, vios, 5, 8, BookingStatus.ChoXacNhan, "Đặt đi Vũng Tàu cuối tuần.");

// 2) Đã xác nhận + đã cọc 30%
var b2 = EnsureBooking("demo-seed-02", kh2, fortuner, 2, 5, BookingStatus.DaXacNhan, "Cần xe 7 chỗ đi Đà Lạt.");
if (b2 != null) AddPayment(b2, b2.TotalPrice * 0.3m, PaymentMethod.ChuyenKhoan, PaymentStatus.DaCoc, "DEMO-DEP-002", 1);

// 3) Đã xác nhận, có khoản cọc chờ ghi nhận (demo mark-paid)
var b3 = EnsureBooking("demo-seed-03", kh3, mazda3, 3, 6, BookingStatus.DaXacNhan, null);
if (b3 != null) AddPayment(b3, b3.TotalPrice * 0.3m, PaymentMethod.ViDienTu, PaymentStatus.ChuaThanhToan, "DEMO-PEND-003", 0);

// 4) Đang thuê (xe chuyển DangThue), đã thanh toán đủ
var b4 = EnsureBooking("demo-seed-04", kh1, xpander, -2, 2, BookingStatus.DangThue, "Gia đình đi du lịch.");
if (b4 != null)
{
    db.Cars.First(x => x.Id == b4.CarId).Status = CarStatus.DangThue;
    db.SaveChanges();
    AddPayment(b4, b4.TotalPrice, PaymentMethod.TheNoiDia, PaymentStatus.DaThanhToan, "DEMO-PAY-004", 3);
}

// 5) Đã trả xe trễ 1 ngày + phí hư hỏng nhẹ
var b5 = EnsureBooking("demo-seed-05", kh2, crv, -10, -7, BookingStatus.DaTraXe, "Công tác Hải Phòng.");
if (b5 != null)
{
    AddReturn(b5, today.AddDays(-6), 200_000m, "Trầy xước nhẹ cản sau.");
    var rr = db.ReturnRecords.First(r => r.BookingId == b5.Id);
    AddPayment(b5, b5.TotalPrice, PaymentMethod.ChuyenKhoan, PaymentStatus.DaThanhToan, "DEMO-PAY-005A", 10);
    AddPayment(b5, rr.LateFee + rr.DamageFee, PaymentMethod.TienMat, PaymentStatus.DaThanhToan, "DEMO-PAY-005B", 6);
}

// 6) Đã trả xe đúng hạn, thanh toán đủ
var b6 = EnsureBooking("demo-seed-06", kh3, ranger, -20, -17, BookingStatus.DaTraXe, null);
if (b6 != null)
{
    AddReturn(b6, b6.EndDate, 0, null);
    AddPayment(b6, b6.TotalPrice, PaymentMethod.TienMat, PaymentStatus.DaThanhToan, "DEMO-PAY-006", 17);
}

// 7) Đã hủy (không thanh toán)
EnsureBooking("demo-seed-07", kh1, merc, 10, 12, BookingStatus.DaHuy, "Khách đổi lịch.");

// 8) Đã trả xe đúng hạn, cùng xe Vios với booking 1 nhưng ở quá khứ (không trùng lịch)
var b8 = EnsureBooking("demo-seed-08", kh2, vios, -15, -13, BookingStatus.DaTraXe, null);
if (b8 != null)
{
    AddReturn(b8, b8.EndDate, 0, null);
    AddPayment(b8, b8.TotalPrice, PaymentMethod.ViDienTu, PaymentStatus.DaThanhToan, "DEMO-PAY-008", 13);
}

// ---------- Dữ liệu số lượng lớn (idempotent: bỏ qua nếu đã có booking demo-bulk-*) ----------
// Mỗi xe chỉ có booking không giao nhau; xe mới tạo riêng nên không đụng 8 booking ở trên.
if (!db.Bookings.Any(b => b.CorrelationId!.StartsWith("demo-bulk-")))
{
    var rnd = new Random(42); // cố định để chạy lại ra cùng dữ liệu
    var passHash = hasher.Hash(DemoPassword); // hash 1 lần dùng chung (BCrypt chậm)
    string[] ho = { "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương" };
    string[] dem = { "Văn", "Thị", "Minh", "Quốc", "Thanh", "Hoàng", "Ngọc", "Gia", "Anh", "Đức" };
    string[] ten = { "An", "Bình", "Châu", "Dũng", "Hà", "Hùng", "Khánh", "Lan", "Linh", "Long", "Mai", "Nam", "Phúc", "Quân", "Sơn", "Tâm", "Thảo", "Trang", "Tuấn", "Việt" };
    string[] tinh = { "Quận 1, TP.HCM", "Quận 7, TP.HCM", "Cầu Giấy, Hà Nội", "Hải Châu, Đà Nẵng", "Ninh Kiều, Cần Thơ", "Nha Trang, Khánh Hòa", "Đà Lạt, Lâm Đồng" };

    var users = new List<Customer>();
    for (var i = 1; i <= 60; i++)
        users.Add(new Customer
        {
            FullName = $"{ho[rnd.Next(ho.Length)]} {dem[rnd.Next(dem.Length)]} {ten[rnd.Next(ten.Length)]}",
            Email = $"bulk{i:000}@demo.local", PhoneNumber = $"0913{100000 + i}", Role = UserRole.Customer,
            PasswordHash = passHash, IdentityNumber = $"079{300000000 + i}", Address = $"{rnd.Next(1, 200)} Đường số {rnd.Next(1, 50)}, {tinh[rnd.Next(tinh.Length)]}"
        });
    users = users.Where(u => !db.Customers.Any(c => c.Email == u.Email || c.PhoneNumber == u.PhoneNumber)).ToList();
    db.Customers.AddRange(users);
    addedUsers += users.Count;

    (CarType T, string Brand, string Model, int Seats, int Price)[] catalog =
    {
        (CarType.Sedan, "Toyota", "Camry", 5, 900_000), (CarType.Sedan, "Honda", "City", 5, 550_000), (CarType.Sedan, "Kia", "K3", 5, 600_000),
        (CarType.SUV, "Hyundai", "Santa Fe", 7, 1_000_000), (CarType.SUV, "Mazda", "CX-5", 5, 850_000), (CarType.SUV, "VinFast", "VF e34", 5, 800_000),
        (CarType.Hatchback, "VinFast", "Fadil", 5, 420_000), (CarType.Hatchback, "Toyota", "Wigo", 5, 400_000),
        (CarType.MPV, "Suzuki", "Ertiga", 7, 600_000), (CarType.MPV, "Kia", "Carnival", 7, 1_300_000),
        (CarType.Pickup, "Mitsubishi", "Triton", 5, 850_000), (CarType.Luxury, "BMW", "520i", 5, 1_450_000), (CarType.Luxury, "Lexus", "RX350", 5, 1_500_000),
    };
    var cars = new List<Car>();
    for (var i = 1; i <= 45; i++)
    {
        var m = catalog[(i - 1) % catalog.Length];
        var price = Math.Clamp(m.Price + rnd.Next(-3, 4) * 10_000, 400_000, 1_500_000);
        var status = i % 15 == 0 ? CarStatus.BaoTri : i % 22 == 0 ? CarStatus.NgungHoatDong : CarStatus.SanSang;
        cars.Add(new Car
        {
            CarType = m.T, Brand = m.Brand, Model = m.Model, LicensePlate = $"{(i % 2 == 0 ? "51H" : "30L")}-{200 + i / 100}.{i % 100:00}",
            Seats = m.Seats, CarAgeYears = rnd.Next(0, 6), BasePricePerDay = price, Status = status, Description = $"{m.Brand} {m.Model} - xe demo số lượng lớn."
        });
    }
    cars = cars.Where(c => !db.Cars.Any(x => x.LicensePlate == c.LicensePlate)).ToList();
    db.Cars.AddRange(cars);
    addedCars += cars.Count;
    db.SaveChanges(); // cần Id trước khi tạo booking
    // Nạp lại từ DB để chạy lại sau lần lỗi giữa chừng (user/xe đã có nhưng chưa có booking) vẫn đủ dữ liệu.
    users = db.Customers.Where(c => c.Email.StartsWith("bulk") && c.Email.EndsWith("@demo.local")).ToList();
    cars = db.Cars.Where(c => c.Description != null && c.Description.EndsWith("xe demo số lượng lớn.")).ToList();

    var seq = 0;
    foreach (var car in cars.Where(c => c.Status == CarStatus.SanSang))
    {
        var day = -120 + rnd.Next(0, 10); // dựng lịch từ quá khứ tới tương lai, mỗi xe một dòng thời gian
        while (day < 60)
        {
            var len = rnd.Next(1, 8);
            var start = today.AddDays(day);
            var end = start.AddDays(len);
            day += len + rnd.Next(1, 9);
            var cust = users.Count > 0 ? users[rnd.Next(users.Count)] : kh1;
            var fb = rnd.Next(100) < 25;
            var predicted = fb ? (decimal?)null : Math.Round(car.BasePricePerDay * (decimal)(0.9 + rnd.NextDouble() * 0.3) / 1000m) * 1000m;
            var final = Math.Clamp(predicted ?? car.BasePricePerDay, 400_000m, 1_500_000m);

            BookingStatus st;
            if (end <= today) st = rnd.Next(100) < 12 ? BookingStatus.DaHuy : BookingStatus.DaTraXe;
            else if (start <= today) st = BookingStatus.DangThue;
            else st = rnd.Next(100) switch { < 15 => BookingStatus.DaHuy, < 55 => BookingStatus.ChoXacNhan, _ => BookingStatus.DaXacNhan };

            var b = new Booking
            {
                CustomerId = cust.Id, CarId = car.Id, StartDate = start, EndDate = end, PredictedPricePerDay = predicted,
                FinalPricePerDay = final, TotalPrice = final * len, IsFallback = fb, Status = st,
                CorrelationId = $"demo-bulk-{++seq:0000}", Note = rnd.Next(3) == 0 ? "Đi công tác / du lịch." : null,
                CreatedAt = start.AddDays(-rnd.Next(1, 10))
            };
            if (st == BookingStatus.DangThue) car.Status = CarStatus.DangThue;
            if (st == BookingStatus.DaXacNhan)
                b.Payments.Add(new Payment { Amount = b.TotalPrice * 0.3m, Method = PaymentMethod.ChuyenKhoan, Status = PaymentStatus.DaCoc, TransactionCode = $"BULK-DEP-{seq:0000}", PaidAt = DateTime.UtcNow.AddDays(-rnd.Next(0, 3)) });
            if (st is BookingStatus.DangThue or BookingStatus.DaTraXe)
                b.Payments.Add(new Payment { Amount = b.TotalPrice, Method = (PaymentMethod)rnd.Next(1, 5), Status = PaymentStatus.DaThanhToan, TransactionCode = $"BULK-PAY-{seq:0000}", PaidAt = start.AddDays(-1) });
            if (st == BookingStatus.DaTraXe)
            {
                var returnedAt = end.AddDays(rnd.Next(100) < 20 ? rnd.Next(1, 3) : 0);
                var damage = rnd.Next(100) < 10 ? rnd.Next(1, 6) * 100_000m : 0m;
                var (lateDays, lateFee, finalAmount) = CarRental.Application.Modules.Bookings.RentalService.CalculateReturn(
                    b.EndDate, returnedAt, b.FinalPricePerDay, b.TotalPrice, damage);
                b.ReturnRecord = new ReturnRecord
                {
                    ReturnedAt = returnedAt, LateDays = lateDays, LateFee = lateFee, DamageFee = damage,
                    DamageNote = damage > 0 ? "Trầy xước nhẹ." : null, FinalAmount = finalAmount, StaffId = staff.Id
                };
            }
            db.Bookings.Add(b);
            addedBookings++;
        }
    }
    db.SaveChanges();
}

// ---------- Thông số chi tiết xe (hộp số, nhiên liệu, năm sản xuất, mô tả) ----------
// Idempotent: chỉ điền cho xe chưa có hộp số. Năm sản xuất = năm hiện tại - tuổi xe.
var CarSpecs = new Dictionary<string, (Transmission T, FuelType F, string Desc)>
{
    ["BMW|520i"] = (Transmission.SoTuDong, FuelType.Xang, "Sedan hạng sang 5 chỗ, động cơ 2.0L turbo mạnh mẽ, nội thất da cao cấp, màn hình cong và hệ thống âm thanh chất lượng cao. Phù hợp đón tiếp đối tác, đám cưới và chuyến công tác quan trọng."),
    ["Ford|Ranger"] = (Transmission.SoTuDong, FuelType.Dau, "Bán tải máy dầu mạnh mẽ, thùng sau rộng, gầm cao và hệ dẫn động 2 cầu. Phù hợp chở hàng, đi đường đèo núi hoặc off-road nhẹ."),
    ["Honda|CR-V"] = (Transmission.SoTuDong, FuelType.Xang, "SUV 7 chỗ rộng rãi, vận hành êm, trang bị Honda Sensing hỗ trợ lái an toàn. Hàng ghế sau thoải mái, hợp đi gia đình đường dài."),
    ["Honda|City"] = (Transmission.SoTuDong, FuelType.Xang, "Sedan hạng B thiết kế trẻ trung, cabin rộng so với phân khúc, tiết kiệm nhiên liệu. Lựa chọn tốt để đi phố và đi tỉnh."),
    ["Hyundai|Accent"] = (Transmission.SoTuDong, FuelType.Xang, "Sedan gọn gàng, giá thuê dễ chịu, màn hình giải trí và camera lùi tiện dụng. Phù hợp đi làm, đi phố, chạy dịch vụ."),
    ["Hyundai|Grand i10"] = (Transmission.SoSan, FuelType.Xang, "Hatchback nhỏ gọn, dễ lái và dễ đỗ trong phố đông, rất tiết kiệm xăng. Phù hợp người mới lái và di chuyển nội thành."),
    ["Hyundai|Santa Fe"] = (Transmission.SoTuDong, FuelType.Dau, "SUV 7 chỗ máy dầu, nội thất sang trọng, nhiều tiện nghi và cửa sổ trời toàn cảnh. Hợp chuyến du lịch gia đình, nhóm bạn."),
    ["Kia|Carnival"] = (Transmission.SoTuDong, FuelType.Dau, "MPV 7 chỗ cao cấp, cửa lùa điện, hàng ghế thương gia ngả sâu. Rất phù hợp đưa đón nhóm khách, gia đình đông người."),
    ["Kia|K3"] = (Transmission.SoTuDong, FuelType.Xang, "Sedan hạng C thiết kế thể thao, vô lăng bọc da, màn hình cảm ứng và nhiều tính năng an toàn. Lái nhẹ và êm."),
    ["Kia|Morning"] = (Transmission.SoSan, FuelType.Xang, "Hatchback đô thị cỡ nhỏ, linh hoạt, chi phí vận hành thấp. Hợp di chuyển một mình hoặc đi cặp đôi."),
    ["Kia|Seltos"] = (Transmission.SoTuDong, FuelType.Xang, "SUV cỡ nhỏ 5 chỗ, gầm cao, màn hình lớn và nhiều tiện nghi. Cân bằng giữa đi phố và đi xa."),
    ["Lexus|RX350"] = (Transmission.SoTuDong, FuelType.Xang, "SUV hạng sang, động cơ V6 êm ái, cách âm xuất sắc, ghế massage và hệ thống âm thanh Mark Levinson. Trải nghiệm cao cấp cho dịp đặc biệt."),
    ["Mazda|CX-5"] = (Transmission.SoTuDong, FuelType.Xang, "SUV 5 chỗ thiết kế KODO, vô lăng chính xác, nội thất tinh tế. Được nhiều khách chọn cho các chuyến đi cuối tuần."),
    ["Mazda|Mazda3"] = (Transmission.SoTuDong, FuelType.Xang, "Sedan thiết kế đẹp, lái thú vị, nội thất chăm chút và cách âm tốt. Hợp người thích cảm giác lái."),
    ["Mercedes|C300 AMG"] = (Transmission.SoTuDong, FuelType.Xang, "Sedan hạng sang gói thể thao AMG, động cơ 2.0L turbo, nội thất ambient light 64 màu. Dành cho sự kiện và những dịp quan trọng."),
    ["Mitsubishi|Triton"] = (Transmission.SoTuDong, FuelType.Dau, "Bán tải bền bỉ, hệ dẫn động Super Select, khoang lái rộng. Phù hợp đi công trình, chở đồ và đường xấu."),
    ["Mitsubishi|Xpander"] = (Transmission.SoTuDong, FuelType.Xang, "MPV 7 chỗ phổ biến, khoang hành khách rộng, gầm cao dễ đi đường xấu. Lựa chọn kinh tế cho gia đình."),
    ["Suzuki|Ertiga"] = (Transmission.SoTuDong, FuelType.Xang, "MPV 7 chỗ nhỏ gọn, tiết kiệm nhiên liệu, giá thuê mềm. Hợp gia đình nhỏ hoặc nhóm bạn đi gần."),
    ["Toyota|Camry"] = (Transmission.SoTuDong, FuelType.Xang, "Sedan hạng D rộng rãi, vận hành êm ái, bền bỉ và giữ giá. Thích hợp đi công tác, đón khách."),
    ["Toyota|Fortuner"] = (Transmission.SoTuDong, FuelType.Dau, "SUV 7 chỗ máy dầu, gầm cao, khả năng vượt địa hình tốt. Chuyên đi đường dài, đi Tây Nguyên, đi núi."),
    ["Toyota|Innova"] = (Transmission.SoTuDong, FuelType.Xang, "MPV 7 chỗ bền bỉ, ghế ngồi thoải mái, chi phí vận hành thấp. Lựa chọn quen thuộc cho gia đình."),
    ["Toyota|Vios"] = (Transmission.SoTuDong, FuelType.Xang, "Sedan phổ thông tiết kiệm nhiên liệu, dễ lái, ít hỏng vặt. Phù hợp đi phố, đi tỉnh, người mới thuê xe."),
    ["Toyota|Wigo"] = (Transmission.SoTuDong, FuelType.Xang, "Hatchback nhỏ gọn, rất tiết kiệm xăng, dễ đỗ xe trong phố. Giá thuê thấp nhất phân khúc."),
    ["VinFast|Fadil"] = (Transmission.SoTuDong, FuelType.Xang, "Hatchback đô thị của VinFast, vô lăng nhẹ, màn hình 7 inch, nhiều tính năng an toàn. Hợp đi nội thành."),
    ["VinFast|VF e34"] = (Transmission.SoTuDong, FuelType.Dien, "SUV điện 5 chỗ, chạy khoảng 300 km mỗi lần sạc, vận hành êm và không phát thải. Chi phí quãng đường rất thấp."),
};
var thisYear = DateTime.UtcNow.Year;
int specsFilled = 0;
foreach (var c in db.Cars.Where(x => x.Transmission == null).ToList())
{
    if (!CarSpecs.TryGetValue($"{c.Brand}|{c.Model}", out var sp)) continue;
    c.Transmission = sp.T; c.FuelType = sp.F; c.ManufactureYear = thisYear - c.CarAgeYears; c.Description = sp.Desc;
    specsFilled++;
}
db.SaveChanges();
Console.WriteLine($"Đã điền thông số chi tiết cho {specsFilled} xe.");

SeedWave1();
SeedWave2();
SeedWave3();
SeedWave4();
SeedWave5();

Console.WriteLine($"Xong. Thêm mới: {addedUsers} tài khoản, {addedCars} xe, {addedBookings} booking.");
Console.WriteLine($"Tổng hiện có: {db.Customers.Count()} tài khoản, {db.Cars.Count()} xe, {db.Bookings.Count()} booking.");
Console.WriteLine();
Console.WriteLine($"Tài khoản DEMO (chỉ dùng demo) - mật khẩu chung: {DemoPassword}");
Console.WriteLine("  Admin   : admin@demo.local");
Console.WriteLine("  Staff   : staff@demo.local");
Console.WriteLine("  Khách 1 : an.nguyen@demo.local");
Console.WriteLine("  Khách 2 : binh.tran@demo.local");
Console.WriteLine("  Khách 3 : cuong.le@demo.local");
Console.WriteLine("  Staff 2 : staff2@demo.local");
Console.WriteLine("  Khách bị cảnh báo: warn@demo.local | Khách bị chặn: blocked@demo.local");
return 0;

// ---------- Đợt 1: phân quyền, danh sách chặn, xe chờ duyệt (idempotent) ----------
void SeedWave1()
{
    var admin = db.Customers.First(c => c.Email == "admin@demo.local");
    EnsureUser("Nhân viên Demo 2", "staff2@demo.local", "0900000012", UserRole.Staff, null, null);
    var warn = EnsureUser("Khách Cảnh Báo", "warn@demo.local", "0912000091", UserRole.Customer, "079200000091", "1 Nguyễn Huệ, Quận 1, TP.HCM");
    var blocked = EnsureUser("Khách Bị Chặn", "blocked@demo.local", "0912000092", UserRole.Customer, "079200000092", "2 Nguyễn Huệ, Quận 1, TP.HCM");

    void EnsureBlacklist(Customer c, BlacklistLevel level, string reason)
    {
        if (db.Set<BlacklistEntry>().Any(e => e.CustomerId == c.Id && e.RemovedAt == null)) return;
        db.Set<BlacklistEntry>().Add(new BlacklistEntry
        {
            CustomerId = c.Id, IdentityNumber = c.IdentityNumber, PhoneNumber = c.PhoneNumber,
            Level = level, Reason = reason, CreatedById = admin.Id
        });
        db.SaveChanges();
    }
    EnsureBlacklist(warn, BlacklistLevel.CanhBao, "Trả xe trễ nhiều lần (demo).");
    EnsureBlacklist(blocked, BlacklistLevel.Chan, "Làm hư hỏng xe và không bồi thường (demo).");

    // Xe do Staff đề xuất, đang chờ Admin duyệt
    EnsureCar(CarType.SUV, "Toyota", "Corolla Cross", "51A-100.13", 5, 1, 850_000, CarStatus.ChoDuyet, "Xe do nhân viên đề xuất, chờ quản trị viên duyệt giá.");
}

// ---------- Đợt 2: giấy tờ xác minh và điểm thưởng (idempotent) ----------
// Ảnh giấy tờ chỉ là key giả: chưa có file trong MinIO nên xem ảnh sẽ 404; nộp lại qua giao diện để có ảnh thật.
void SeedWave2()
{
    var admin = db.Customers.First(c => c.Email == "admin@demo.local");
    void EnsureDoc(Customer c, DocumentType type, string number, DocumentStatus status, string? reject = null)
    {
        if (db.Set<CustomerDocument>().Any(d => d.CustomerId == c.Id && d.Type == type && !d.IsDeleted)) return;
        var doc = new CustomerDocument
        {
            CustomerId = c.Id, Type = type, Number = number, Status = status, RejectReason = reject,
            FrontKey = $"private/documents/{c.Id}/demo-{(int)type}-front.webp",
            BackKey = $"private/documents/{c.Id}/demo-{(int)type}-back.webp",
            ExpiryDate = type == DocumentType.BangLai ? DateTime.Today.AddYears(5) : null,
            LicenseClass = type == DocumentType.BangLai ? "B2" : null
        };
        if (status != DocumentStatus.ChoDuyet) { doc.ReviewedById = admin.Id; doc.ReviewedAt = DateTime.UtcNow; }
        db.Set<CustomerDocument>().Add(doc);
        db.SaveChanges();
    }
    EnsureDoc(kh1, DocumentType.Cccd, "079200000001", DocumentStatus.DaDuyet);
    EnsureDoc(kh1, DocumentType.BangLai, "079200000001", DocumentStatus.DaDuyet);
    EnsureDoc(kh2, DocumentType.Cccd, "001200000002", DocumentStatus.ChoDuyet);
    EnsureDoc(kh3, DocumentType.Cccd, "048200000003", DocumentStatus.TuChoi, "Ảnh mờ, không đọc được số CCCD (demo).");

    // Điểm = 1% giá trị các đơn đã trả xe, cùng công thức với RentalServiceGuard
    var done = db.Bookings.Where(b => !b.IsDeleted && b.Status == BookingStatus.DaTraXe)
        .Select(b => new { b.Id, b.CustomerId, Amount = b.ReturnRecord != null ? b.ReturnRecord.FinalAmount : b.TotalPrice }).ToList();
    var awarded = db.Set<LoyaltyTransaction>().Where(t => t.BookingId != null && t.Reason == LoyaltyReason.TichDiem)
        .Select(t => t.BookingId!.Value).ToHashSet();
    foreach (var d in done.Where(x => !awarded.Contains(x.Id)))
    {
        var pts = LoyaltyPolicy.PointsFor(d.Amount);
        if (pts <= 0) continue;
        db.Set<LoyaltyTransaction>().Add(new LoyaltyTransaction { CustomerId = d.CustomerId, BookingId = d.Id, Points = pts, Reason = LoyaltyReason.TichDiem });
        var cu = db.Customers.Find(d.CustomerId)!;
        cu.LoyaltyPoints += pts; cu.LifetimePoints += pts;
    }
    db.SaveChanges();
    Console.WriteLine("Đã seed giấy tờ (An đã xác minh, Bình chờ duyệt, Cường bị từ chối) và điểm thưởng.");
}

// ---------- Đợt 3: cọc bảo đảm, biên bản bàn giao, gia hạn (idempotent) ----------
void SeedWave3()
{
    // Cọc: 15 triệu cho xe hạng sang, 5 triệu cho xe còn lại (chỉ điền xe chưa có cọc).
    foreach (var c in db.Cars.Where(x => x.SecurityDeposit == 0).ToList())
        c.SecurityDeposit = c.CarType == CarType.Luxury ? 15_000_000m : 5_000_000m;
    db.SaveChanges();

    var cars = db.Cars.ToDictionary(c => c.Id);
    void EnsureHandover(Booking b, HandoverType type, int odo, int fuel, string? note)
    {
        if (db.Set<HandoverRecord>().Any(h => h.BookingId == b.Id && h.Type == type)) return;
        db.Set<HandoverRecord>().Add(new HandoverRecord { BookingId = b.Id, Type = type, Odometer = odo, FuelLevel = fuel, ConditionNote = note, StaffId = staff.Id });
        db.SaveChanges();
    }
    void EnsureDepositCollected(Booking b)
    {
        if (db.Set<DepositTransaction>().Any(t => t.BookingId == b.Id && t.Type == DepositTxType.Thu)) return;
        db.Set<DepositTransaction>().Add(new DepositTransaction
        {
            BookingId = b.Id, Type = DepositTxType.Thu, Amount = cars[b.CarId].SecurityDeposit, Method = PaymentMethod.TienMat, StaffId = staff.Id
        });
        db.SaveChanges();
    }
    // Quyết toán như DepositService: trừ phần còn phải trả (tối đa bằng cọc) bằng Payment TruCoc, hoàn phần còn lại.
    void EnsureDepositSettled(Booking b)
    {
        EnsureDepositCollected(b);
        if (db.Set<DepositTransaction>().Any(t => t.BookingId == b.Id && t.Type != DepositTxType.Thu)) return;
        var held = cars[b.CarId].SecurityDeposit;
        var amountToPay = db.ReturnRecords.Where(r => r.BookingId == b.Id).Select(r => (decimal?)r.FinalAmount).FirstOrDefault() ?? b.TotalPrice;
        var paid = db.Payments.Where(p => p.BookingId == b.Id && (p.Status == PaymentStatus.DaCoc || p.Status == PaymentStatus.DaThanhToan)).Sum(p => p.Amount);
        var deduct = Math.Min(held, Math.Max(0, amountToPay - paid));
        if (deduct > 0)
        {
            db.Payments.Add(new Payment { BookingId = b.Id, Amount = deduct, Method = PaymentMethod.TruCoc, Status = PaymentStatus.DaThanhToan, PaidAt = DateTime.UtcNow, TransactionCode = "TRU-COC" });
            db.Set<DepositTransaction>().Add(new DepositTransaction { BookingId = b.Id, Type = DepositTxType.KhauTru, Amount = deduct, Method = PaymentMethod.TruCoc, StaffId = staff.Id, Note = "Trừ phí hư hại/phát sinh (demo)." });
        }
        if (held - deduct > 0)
            db.Set<DepositTransaction>().Add(new DepositTransaction { BookingId = b.Id, Type = DepositTxType.Hoan, Amount = held - deduct, Method = PaymentMethod.TienMat, StaffId = staff.Id, Note = "Hoàn cọc còn lại (demo)." });
        db.SaveChanges();
    }

    // Hai đơn đã trả xe có phí hư hại: 1,2 triệu (nhỏ hơn cọc 5 triệu) và 7 triệu (lớn hơn cọc).
    var b9 = EnsureBooking("demo-seed-09", kh1, ranger, -40, -37, BookingStatus.DaTraXe, "Đi Tây Nguyên.");
    if (b9 != null)
    {
        AddReturn(b9, b9.EndDate, 1_200_000m, "Móp cản trước, trầy gương chiếu hậu.");
        AddPayment(b9, b9.TotalPrice, PaymentMethod.ChuyenKhoan, PaymentStatus.DaThanhToan, "DEMO-PAY-009", 40);
    }
    var b10 = EnsureBooking("demo-seed-10", kh2, mazda3, -30, -27, BookingStatus.DaTraXe, "Sự cố va chạm.");
    if (b10 != null)
    {
        AddReturn(b10, b10.EndDate, 7_000_000m, "Hư hại thân xe nặng sau va chạm, cần sơn và thay đèn.");
        AddPayment(b10, b10.TotalPrice, PaymentMethod.TienMat, PaymentStatus.DaThanhToan, "DEMO-PAY-010", 30);
    }

    foreach (var key in new[] { "demo-seed-04", "demo-seed-05", "demo-seed-06", "demo-seed-08", "demo-seed-09", "demo-seed-10" })
    {
        var b = db.Bookings.FirstOrDefault(x => x.CorrelationId == key);
        if (b == null) continue;
        var odo = 20_000 + b.Id * 100;
        EnsureHandover(b, HandoverType.Giao, odo, 100, "Xe sạch, không trầy xước (demo).");
        EnsureDepositCollected(b);
        if (b.Status == BookingStatus.DaTraXe)
        {
            var dmg = db.ReturnRecords.Where(r => r.BookingId == b.Id).Select(r => r.DamageNote).FirstOrDefault();
            EnsureHandover(b, HandoverType.Nhan, odo + 420, 60, dmg ?? "Xe nguyên vẹn (demo).");
            if (key is "demo-seed-05" or "demo-seed-09" or "demo-seed-10") EnsureDepositSettled(b);
        }
    }

    // Yêu cầu gia hạn đang chờ duyệt cho đơn đã xác nhận của Bình (Fortuner), thêm 2 ngày.
    var b2 = db.Bookings.FirstOrDefault(x => x.CorrelationId == "demo-seed-02");
    if (b2 != null && !db.Set<BookingExtension>().Any(e => e.BookingId == b2.Id))
    {
        db.Set<BookingExtension>().Add(new BookingExtension
        {
            BookingId = b2.Id, OldEndDate = b2.EndDate, NewEndDate = b2.EndDate.AddDays(2),
            ExtraAmount = CarRental.Application.Common.PricingCalculator.ExtensionCharge(b2.FinalPricePerDay, 2), RequestedById = b2.CustomerId
        });
        db.SaveChanges();
    }
    Console.WriteLine("Đã seed cọc xe, biên bản bàn giao, quyết toán cọc (phí 1,2 triệu và 7 triệu) và 1 yêu cầu gia hạn chờ duyệt.");
}

// ---------- Đợt 4: chi nhánh, mã giảm giá, phụ phí, giá theo giờ (idempotent) ----------
void SeedWave4()
{
    foreach (var (name, addr) in new[] { ("Chi nhánh Quận 1", "12 Lê Lợi, Quận 1, TP.HCM"), ("Chi nhánh Sân bay Tân Sơn Nhất", "Đường Trường Sơn, Tân Bình, TP.HCM") })
        if (!db.Set<Branch>().Any(b => b.Name == name))
            db.Set<Branch>().Add(new Branch { Name = name, Address = addr, Phone = "02838000000" });
    db.SaveChanges();

    var branchIds = db.Set<Branch>().OrderBy(b => b.Id).Select(b => b.Id).Take(2).ToList();
    foreach (var c in db.Cars.Where(x => x.BranchId == null || x.PricePerHour == null).ToList())
    {
        c.BranchId ??= branchIds[c.Id % branchIds.Count];
        c.PricePerHour ??= Math.Round(c.BasePricePerDay / 8m / 1000m) * 1000m;
    }
    db.SaveChanges();

    var now = DateTime.UtcNow;
    void EnsureCoupon(string code, CouponType type, decimal value, decimal? max, decimal? min, DateTime from, DateTime to, int? limit)
    {
        if (db.Set<Coupon>().Any(c => c.Code == code)) return;
        db.Set<Coupon>().Add(new Coupon { Code = code, Type = type, Value = value, MaxDiscount = max, MinOrderAmount = min, ValidFrom = from, ValidTo = to, UsageLimit = limit });
        db.SaveChanges();
    }
    EnsureCoupon("WELCOME10", CouponType.PhanTram, 10, 300_000m, 500_000m, now.AddDays(-30), now.AddDays(180), 100);
    EnsureCoupon("GIAM200K", CouponType.SoTien, 200_000m, null, 1_000_000m, now.AddDays(-30), now.AddDays(60), 50);
    EnsureCoupon("HETHAN", CouponType.PhanTram, 20, null, null, now.AddDays(-90), now.AddDays(-1), 10);

    if (!db.Set<Surcharge>().Any())
    {
        db.Set<Surcharge>().Add(new Surcharge { Name = "Phụ phí cuối tuần", Type = SurchargeType.CoDinh, Amount = 100_000m, AutoApply = false });
        db.Set<Surcharge>().Add(new Surcharge { Name = "Phí dịch vụ", Type = SurchargeType.PhanTram, Amount = 2, AutoApply = true });
        db.SaveChanges();
    }
    Console.WriteLine("Đã seed đợt 4: chi nhánh, mã WELCOME10/GIAM200K/HETHAN, phụ phí, giá theo giờ.");
}

// ---------- Đợt 5: lịch đăng kiểm/bảo hiểm/bảo dưỡng và 1 đơn quá hạn trả (idempotent) ----------
void SeedWave5()
{
    var now = DateTime.UtcNow;
    var cars = db.Cars.Where(c => c.Status != CarStatus.ChoDuyet).OrderBy(c => c.Id).ToList();
    var m = db.Set<CarMaintenance>();
    if (!m.Any() && cars.Count >= 3)
    {
        foreach (var c in cars)
        {
            m.Add(new CarMaintenance { CarId = c.Id, Type = MaintenanceType.DangKiem, DueDate = today.AddDays(120 + c.Id % 60), Note = "Đăng kiểm định kỳ" });
            m.Add(new CarMaintenance { CarId = c.Id, Type = MaintenanceType.BaoHiem, DueDate = today.AddDays(200 + c.Id % 90), Note = "Bảo hiểm thân vỏ" });
        }
        m.Add(new CarMaintenance { CarId = cars[0].Id, Type = MaintenanceType.DangKiem, DueDate = today.AddDays(-5), Note = "QUÁ HẠN đăng kiểm (demo)" });
        m.Add(new CarMaintenance { CarId = cars[1].Id, Type = MaintenanceType.BaoHiem, DueDate = today.AddDays(5), Note = "Sắp hết hạn bảo hiểm (demo)" });
        m.Add(new CarMaintenance { CarId = cars[2].Id, Type = MaintenanceType.BaoDuong, DueDate = today.AddDays(10), DueOdometer = 20_000, Note = "Bảo dưỡng 20.000 km" });
        foreach (var c in cars.Where(x => x.Status == CarStatus.BaoTri))
            m.Add(new CarMaintenance { CarId = c.Id, Type = MaintenanceType.BaoDuong, DueDate = today.AddDays(-1), StartedAt = now.AddDays(-1), Status = MaintenanceStatus.DangThucHien, Note = "Đang bảo dưỡng (demo)" });
        db.SaveChanges();
    }

    // Đơn đang thuê đã quá hạn trả: chọn một xe sẵn sàng không có đơn nào trong khoảng đó.
    const string key = "demo-w5-overdue";
    if (!db.Bookings.Any(b => b.CorrelationId == key))
    {
        var start = today.AddDays(-6); var end = today.AddDays(-1);
        var car = cars.FirstOrDefault(c => c.Status == CarStatus.SanSang
            && !db.Bookings.Any(b => b.CarId == c.Id && b.StartDate < today && b.EndDate > start));
        if (car is not null)
        {
            EnsureBooking(key, kh1, car, -6, -1, BookingStatus.DangThue, "Khách chưa trả xe (demo dashboard).");
            car.Status = CarStatus.DangThue;
            db.SaveChanges();
        }
    }
    Console.WriteLine("Đã seed đợt 5: lịch đăng kiểm/bảo hiểm (1 quá hạn, 1 sắp hạn), bảo dưỡng và 1 đơn quá hạn trả.");
}
