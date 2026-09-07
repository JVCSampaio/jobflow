using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using JobFlow.Api.Data;
using JobFlow.Api.Data.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace JobFlow.Api.Services;

public class UserService
{
    private readonly JobFlowDbContext _db;
    private readonly ILogger<UserService> _logger;

    public UserService(JobFlowDbContext db, ILogger<UserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<User> RegisterAsync(string username, string email, string password)
    {
        if (await _db.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException("Username already taken.");
        if (await _db.Users.AnyAsync(u => u.Email == email))
            throw new InvalidOperationException("Email already taken.");

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null) return null;
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return null;
        return user;
    }

    public string IssueToken(User user)
    {
        var key = Encoding.UTF8.GetBytes(
            Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
            ?? throw new InvalidOperationException("JWT_SIGNING_KEY is not set."));

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: "jobflow",
            audience: "jobflow",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
