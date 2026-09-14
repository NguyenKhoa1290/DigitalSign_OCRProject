// Script tạo BCrypt hash đúng cho Admin@123 và update DB
// Chạy: dotnet script fix_password.csx  (hoặc dùng dotnet-script)

using System;
using BCrypt.Net;

var hash = BCrypt.Net.BCrypt.HashPassword("Admin@123", workFactor: 12);
Console.WriteLine("Hash mới:");
Console.WriteLine(hash);
Console.WriteLine();
Console.WriteLine("Chạy SQL sau trên PostgreSQL:");
Console.WriteLine($"UPDATE \"AppUsers\" SET \"PasswordHash\" = '{hash}' WHERE \"Username\" = 'admin';");
