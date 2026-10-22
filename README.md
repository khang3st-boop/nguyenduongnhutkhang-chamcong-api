# KidGuard - nguyenduongnhutkhang_app

Backend: `api/` (.NET 10, SQL Server). Flutter app: `C:\Users\MY PC\StudioProjects\nguyenduongnhutkhang_app`.

## Cấu hình bắt buộc (user-secrets, KHÔNG commit)
Chạy trong thư mục `api`:

    dotnet user-secrets set "Smtp:User" "<gmail>"
    dotnet user-secrets set "Smtp:Password" "<Gmail App Password 16 ký tự>"
    dotnet user-secrets set "Google:ClientIds:0" "<Web client ID>.apps.googleusercontent.com"
    dotnet user-secrets set "Agora:AppId" "<Agora App ID>"
    dotnet user-secrets set "Agora:AppCertificate" "<Agora App Certificate>"
    dotnet user-secrets set "Admin:Email" "<email thật của admin>"
    dotnet user-secrets set "Admin:Password" "<mật khẩu admin>"

Admin hiện đang là email giả `admin@example.invalid` nên chưa nhận được OTP: đặt lại Admin:Email thật và xóa user `admin` trong DB để seed lại.

## Chạy
    cd api && dotnet run --launch-profile http        # http://0.0.0.0:5300 (Swagger tại /)
    flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5300 --dart-define=GOOGLE_SERVER_CLIENT_ID=<Web client ID>

Điện thoại thật: dùng IP LAN của máy chạy API. Đăng nhập Google Android cần tạo OAuth client Android (package `com.nguyenduongnhutkhang.nguyenduongnhutkhang_app` + SHA-1).
