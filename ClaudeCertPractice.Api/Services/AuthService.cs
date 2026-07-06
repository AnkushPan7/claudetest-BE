using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClaudeCertPractice.Api.Configuration;
using ClaudeCertPractice.Api.Data;
using ClaudeCertPractice.Api.Data.Entities;
using ClaudeCertPractice.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ClaudeCertPractice.Api.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly AuthSettings _auth;

    public AuthService(AppDbContext db, IOptions<AuthSettings> auth)
    {
        _db = db;
        _auth = auth.Value;
    }

    public async Task<AdminLoginResponse?> LoginAdminAsync(
        string email,
        string password,
        CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Email == normalizedEmail && u.Role == UserRoles.Admin,
                ct);

        if (user?.PasswordHash is null)
            return null;

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return null;

        var expiresAt = DateTime.UtcNow.AddHours(_auth.JwtExpiryHours);
        var token = GenerateToken(user, expiresAt);
        return new AdminLoginResponse(token, expiresAt);
    }

    private string GenerateToken(UserEntity user, DateTime expiresAt)
    {
        if (string.IsNullOrWhiteSpace(_auth.JwtSecret) || _auth.JwtSecret.Length < 32)
            throw new InvalidOperationException("Auth:JwtSecret must be at least 32 characters.");

        var claims = new[]
        {
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(ClaimTypes.Name, user.Name),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_auth.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
