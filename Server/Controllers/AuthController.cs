using Microsoft.AspNetCore.Mvc;

namespace NCATAIBlazorFrontendTest.Server.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.IdentityModel.Tokens;
    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Text;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    //using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;
    using NCATAIBlazorFrontendTest.Server.Data;
    using Microsoft.EntityFrameworkCore;
    using NCATAIBlazorFrontendTest.Shared;

    // You will need to add these DTOs and a new `UserService` for a full implementation.
    // For this example, we'll use placeholder data.

    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly AppDbContext _context;

        public AuthController(IConfiguration config, AppDbContext context)
        {
            _config = config;
            _context = context;
        }

        [HttpGet("Authtestendpoint")]
        public async Task<IActionResult> Authtestendpoint()
        {
            return Content("auth endpoint working");
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            // Check if the user already exists
            try
            {
                if (await _context.Users.AnyAsync(u => u.Username == request.Username))
                {
                    return BadRequest(new { message = "Username already exists." });
                }

                PasswordHelper.CreatePasswordHash(request.Password, out string passwordHash, out string passwordSalt);

                var user = new User
                {
                    Username = request.Username,
                    PasswordHash = passwordHash,
                    PasswordSalt = passwordSalt,
                    Role = request.Role
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = "User registered successfully." });
            }
            catch(Exception ex){
                return Content(ex.Message);
            }
            
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await AuthenticateUser(request.Username, request.Password);

            if (user == null)
            {
                return Unauthorized(new { message = "Invalid username or password." });
            }

            var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role)
        };

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(30),
                signingCredentials: credentials);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            return Ok(new LoginResponse { Token = tokenString, Role = user.Role });
        }

        private async Task<User> AuthenticateUser(string username, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (user == null)
            {
                return null;
            }

            // Verify the provided password against the stored hash and salt
            if (!PasswordHelper.VerifyPasswordHash(password, user.PasswordHash, user.PasswordSalt))
            {
                return null;
            }

            return user;
        }
    }


}
