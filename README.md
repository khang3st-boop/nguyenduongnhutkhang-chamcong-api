# Chấm công nhân viên - nguyenduongnhutkhang_app

REST API (ASP.NET Core .NET 10, EF Core, Identity, JWT) cho ứng dụng chấm công. Mọi chức năng của app Flutter đều đi qua API này.

## Chức năng
- Đăng ký bằng email (OTP) hoặc Google OAuth2; đăng nhập username + mật khẩu + OTP email.
- Phân quyền: **Admin** (hệ thống), **Manager** (quản lý công ty), **Employee** (nhân viên).
- CRUD: nhân viên, địa điểm làm việc, lượt chấm công, tin nhắn.
- **Chấm công bằng quét QR** xoay vòng 30 giây (HMAC) + kiểm tra GPS trong bán kính địa điểm (chống chấm hộ).
- **Chia sẻ vị trí GPS** của nhân viên cho quản lý.
- **Video call nhóm** 4-5 người (Agora).
- Báo cáo tháng: ngày công, đi trễ, về sớm, tổng giờ.

## Cấu hình (biến môi trường)
`ConnectionStrings__DefaultConnection`, `Database__Provider` (Postgres|SqlServer), `JWTKey__Secret`, `Admin__Email`, `Admin__Password`,
`Brevo__ApiKey`, `Brevo__SenderEmail`, `Twilio__AccountSid`, `Twilio__AuthToken`, `Twilio__From` (SMS OTP), `Google__ClientIds__0`, `Agora__AppId`, `Agora__AppCertificate`, `PORT`.

Lưu ý: cơ sở dữ liệu phải **trống** (Postgres tự tạo bảng khi khởi động).

## Chạy
```
cd api && dotnet run
```
