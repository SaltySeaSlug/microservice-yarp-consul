using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Authentication.Shared
{
    public static class Class1
    {
        public static IServiceCollection AddJWTTokenAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication(x => {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(o => 
            {
                var Key = Encoding.UTF8.GetBytes(configuration.GetValue<string>("Jwt:Key"));
                o.SaveToken = true;
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false, // on production make it true
                    ValidateAudience = false, // on production make it true
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration.GetValue<string>("Jwt:Issuer"),
                    ValidAudience = configuration.GetValue<string>("Jwt:Audience"),
                    IssuerSigningKey = new SymmetricSecurityKey(Key),
                    ClockSkew = TimeSpan.Zero
                };
            });

            return services;
        }
        public static IServiceCollection AddCustomAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("SqlServerDbCon")));

            services.AddIdentity<IdentityUser, IdentityRole>(options => {
                options.Password.RequireUppercase = true; // on production add more secured options
                options.Password.RequireDigit = true;
                options.SignIn.RequireConfirmedEmail = true;
            }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();

          
            services.AddSingleton<IJWTManagerRepository, JWTManagerRepository>();
            services.AddScoped<IUserServiceRepository, UserServiceRepository>();

            return services;
        }

    }


    public class UserServiceRepository : IUserServiceRepository
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AppDbContext _db;

        public UserServiceRepository(UserManager<IdentityUser> userManager, AppDbContext db)
        {
            this._userManager = userManager;
            this._db = db;

            Create();
        }

        public void Create()
        {
            if (_userManager.FindByEmailAsync("test@test.com").Result == null)
            {
                IdentityUser user = new IdentityUser
                {
                    UserName = "test",
                    Email = "test@test.com"
                };

                IdentityResult result = _userManager.CreateAsync(user, "p@ssw0rD").Result;

                //if (result.Succeeded)
                //{
                //    _userManager.AddToRoleAsync(user, "Admin").Wait();
                //}
            }
        }

        public UserRefreshTokens AddUserRefreshTokens(UserRefreshTokens user)
        {
            _db.UserRefreshToken.Add(user);
            return user;
        }

        public void DeleteUserRefreshTokens(string username, string refreshToken)
        {
            var item = _db.UserRefreshToken.FirstOrDefault(x => x.UserName == username && x.RefreshToken == refreshToken);
            if (item != null)
            {
                _db.UserRefreshToken.Remove(item);
            }
        }

        public UserRefreshTokens GetSavedRefreshTokens(string username, string refreshToken)
        {
            return _db.UserRefreshToken.FirstOrDefault(x => x.UserName == username && x.RefreshToken == refreshToken && x.IsActive == true);
        }

        public int SaveCommit()
        {
            return _db.SaveChanges();
        }

        public async Task<bool> IsValidUserAsync(Users users)
        {
            var u = _userManager.Users.FirstOrDefault(o => o.UserName == users.Name);
            var result = await _userManager.CheckPasswordAsync(u, users.Password);
            return result;

        }
    }

    public interface IUserServiceRepository
    {
        Task<bool> IsValidUserAsync(Users users);

        UserRefreshTokens AddUserRefreshTokens(UserRefreshTokens user);

        UserRefreshTokens GetSavedRefreshTokens(string username, string refreshtoken);

        void DeleteUserRefreshTokens(string username, string refreshToken);

        int SaveCommit();
    }

    public class JWTManagerRepository : IJWTManagerRepository
    {
        private readonly IConfiguration iconfiguration;

        public JWTManagerRepository(IConfiguration iconfiguration)
        {
            this.iconfiguration = iconfiguration;
        }
        public Tokens GenerateToken(string userName)
        {
            return GenerateJWTTokens(userName);
        }

        public Tokens GenerateRefreshToken(string username)
        {
            return GenerateJWTTokens(username);
        }

        public Tokens GenerateJWTTokens(string userName)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var tokenKey = Encoding.UTF8.GetBytes(iconfiguration.GetValue<string>("Jwt:Key"));
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new Claim[]
                  {
                 new Claim(ClaimTypes.Name, userName)
                  }),
                    Expires = DateTime.Now.AddMinutes(1),
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(tokenKey), SecurityAlgorithms.HmacSha256Signature)
                };
                var token = tokenHandler.CreateToken(tokenDescriptor);
                var refreshToken = GenerateRefreshToken();
                return new Tokens { Access_Token = tokenHandler.WriteToken(token), Refresh_Token = refreshToken };
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Convert.ToBase64String(randomNumber);
            }
        }

        public ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
        {
            var Key = Encoding.UTF8.GetBytes(iconfiguration.GetValue<string>("Jwt:Key"));

            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Key),
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken securityToken);
            JwtSecurityToken jwtSecurityToken = securityToken as JwtSecurityToken;
            if (jwtSecurityToken == null || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new SecurityTokenException("Invalid token");
            }


            return principal;
        }

    }

    public interface IJWTManagerRepository
    {
        Tokens GenerateToken(string userName);
        Tokens GenerateRefreshToken(string userName);
        ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
    }

    public class AppDbContext : IdentityDbContext<IdentityUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            //if (!optionsBuilder.IsConfigured)
            //{
            //    optionsBuilder.UseSqlServer(optionsBuilder.con);
            //}
        }

        public virtual DbSet<UserRefreshTokens> UserRefreshToken { get; set; }
    }

    public class UserRefreshTokens
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string UserName { get; set; }
        [Required]
        public string RefreshToken { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class Tokens
    {
        public string Access_Token { get; set; }
        public string Refresh_Token { get; set; }
    }

    public class Users
    {
        public string Name { get; set; }
        public string Password { get; set; }
    }
    //public class SlidingExpirationMiddleware
    //{
    //    private readonly RequestDelegate _next;
    //    private readonly ILogger<SlidingExpirationMiddleware> _logger;

    //    public SlidingExpirationMiddleware(RequestDelegate next, ILogger<SlidingExpirationMiddleware> logger)
    //    {
    //        _next = next;
    //        _logger = logger;
    //    }

    //    // Here you can inject your services if needed
    //    // I've injected JwtTokenService to reissue jwt token if more than half of the timeout interval has elapsed

    //    public async Task InvokeAsync(HttpContext context, JwtTokenService jwtTokenService)
    //    {
    //        try
    //        {
    //            string authorization = context.Request.Headers["Authorization"];

    //            JwtSecurityToken token = null;
    //            if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer"))
    //                token = new JwtSecurityTokenHandler().ReadJwtToken(authorization[7..]); // trim 'Bearer ' from the start

    //            if (token != null && token.ValidTo > DateTime.UtcNow)
    //            {
    //                TimeSpan timeElapsed = DateTime.UtcNow.Subtract(token.ValidFrom);
    //                TimeSpan timeRemaining = token.ValidTo.Subtract(DateTime.UtcNow);

    //                //if more than half of the timeout interval has elapsed.
    //                if (timeRemaining < timeElapsed)
    //                    context.Response.Headers.Add("Set-Authorization", await jwtTokenService.ReissueTokenAsync(token.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value));
    //            }

    //        }
    //        catch (Exception e)
    //        {
    //            _logger.LogError(e, e.Message);
    //        }

    //        await _next(context);
    //    }
    //}
}


