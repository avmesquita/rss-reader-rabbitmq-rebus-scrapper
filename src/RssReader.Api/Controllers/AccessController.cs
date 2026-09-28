using Microsoft.AspNetCore.Mvc;

namespace RssReader.Api.Controllers;

[ApiController]
[Route("api/access")]
public sealed class AccessController(WriteAccessService access, IConfiguration configuration) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        configured = access.IsConfigured,
        authorized = access.ValidateToken(Request.Headers["X-Write-Token"].FirstOrDefault()),
        contactEmail = configuration["AccessControl:ContactEmail"]
    });

    [HttpPost("unlock")]
    public IActionResult Unlock(UnlockRequest request)
    {
        if (!access.IsConfigured)
            return Problem("O acesso de escrita não foi configurado pelo administrador.", statusCode: StatusCodes.Status503ServiceUnavailable);
        if (!access.VerifyPassword(request.Password ?? string.Empty))
            return Unauthorized();

        var (token, expiresAt) = access.CreateToken();
        return Ok(new { token, expiresAt });
    }
}
