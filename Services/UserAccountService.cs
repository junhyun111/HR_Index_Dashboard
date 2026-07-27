using System.Security.Cryptography;
using HRDashboard.Data;
using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Services;

public sealed class UserAccountService(CommonSettingsDbContext db)
{
    private const int Iterations=210_000;

    public async Task EnsureAdministratorAsync(CancellationToken ct=default)
    {
        if(await db.Users.AnyAsync(ct))return;
        var now=DateTimeOffset.UtcNow;
        db.Users.Add(new ApplicationUser
        {
            LoginId="admin",
            PasswordHash=HashPassword("1234"),
            Role="Administrator",
            Theme="light",
            IsActive=true,
            CreatedAtUtc=now,
            UpdatedAtUtc=now
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ApplicationUser?> AuthenticateAsync(string loginId,string password,CancellationToken ct)
    {
        var normalized=loginId.Trim();
        var user=await db.Users.FirstOrDefaultAsync(x=>x.LoginId==normalized&&x.IsActive,ct);
        return user!=null&&VerifyPassword(password,user.PasswordHash)?user:null;
    }

    public static string HashPassword(string password)
    {
        var salt=RandomNumberGenerator.GetBytes(16);
        var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password,string encoded)
    {
        try
        {
            var parts=encoded.Split('.');
            if(parts.Length!=4||parts[0]!="v1"||!int.TryParse(parts[1],out var iterations))return false;
            var salt=Convert.FromBase64String(parts[2]);
            var expected=Convert.FromBase64String(parts[3]);
            var actual=Rfc2898DeriveBytes.Pbkdf2(password,salt,iterations,HashAlgorithmName.SHA256,expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual,expected);
        }
        catch(FormatException){return false;}
    }
}
