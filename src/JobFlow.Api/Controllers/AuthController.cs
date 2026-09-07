using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JobFlow.Api.DTOs;
using JobFlow.Api.Services;

namespace JobFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserService _users;

    public AuthController(UserService users)
    {
        _users = users;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length < 3)
            return BadRequest(new { error = "username must be at least 3 characters" });
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { error = "valid email required" });
        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 8)
            return BadRequest(new { error = "password must be at least 8 characters" });

        var user = await _users.RegisterAsync(req.Username, req.Email, req.Password);
        var token = _users.IssueToken(user);
        return Created($"/api/auth/login", new AuthResponse(token, user.Username, user.Email));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _users.AuthenticateAsync(req.Username, req.Password);
        if (user is null)
            return Unauthorized(new { error = "invalid username or password" });

        var token = _users.IssueToken(user);
        return Ok(new AuthResponse(token, user.Username, user.Email));
    }
}
