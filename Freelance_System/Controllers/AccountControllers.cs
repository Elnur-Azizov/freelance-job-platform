using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using System.Security.Cryptography;
using System.Text;

namespace Freelance_System.Controllers
{
    public class AccountController : Controller
    {
        private readonly string _connStr;
        private readonly string _adminEmail;
        private readonly string _adminPassHash;

        public AccountController(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DefaultConnection") ?? "";
            _adminEmail = config["Admin:Email"] ?? "";
            _adminPassHash = config["Admin:PasswordHash"] ?? "";
        }

        // Login/Register modal sayfası
        public IActionResult Auth(string tab = "login", string? returnUrl = null)
        {
            ViewBag.Tab = tab;
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            returnUrl ??= "/";

            string hash = Sha256(password);

            // ✅ Admin (appsettings) login
            if (!string.IsNullOrWhiteSpace(_adminEmail) &&
                !string.IsNullOrWhiteSpace(_adminPassHash) &&
                email == _adminEmail &&
                hash == _adminPassHash)
            {
                HttpContext.Session.SetInt32("is_admin", 1);
                HttpContext.Session.SetString("username", "Admin");
                return RedirectToAction("Index", "Admin");
            }

            try
            {
                await using var conn = new MySqlConnection(_connStr);
                await conn.OpenAsync();

                // ✅ is_banned kontrolü dahil
                const string sql = @"
SELECT user_id, username, is_banned
FROM users
WHERE email=@e AND password_hash=@p
LIMIT 1;";

                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@e", email);
                cmd.Parameters.AddWithValue("@p", hash);

                await using var reader = await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    int isBanned = reader.GetInt32("is_banned");
                    if (isBanned == 1)
                    {
                        TempData["ShowAuthModal"] = "1";
                        TempData["AuthTab"] = "login";
                        TempData["AuthError"] = "Bu hesap yönetici tarafından askıya alınmıştır.";
                        return RedirectSafe(returnUrl);
                    }

                    int userId = reader.GetInt32("user_id");
                    string username = reader.GetString("username");

                    HttpContext.Session.SetInt32("user_id", userId);
                    HttpContext.Session.SetString("username", username);

                    if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                        return Redirect(returnUrl);

                    return RedirectToAction("Index", "Jobs");
                }

                // yanlış email/şifre
                TempData["ShowAuthModal"] = "1";
                TempData["AuthTab"] = "login";
                TempData["AuthError"] = "E-posta veya şifre yanlış.";
                return RedirectSafe(returnUrl);
            }
            catch
            {
                TempData["ShowAuthModal"] = "1";
                TempData["AuthTab"] = "login";
                TempData["AuthError"] = "Giriş sırasında bir hata oluştu.";
                return RedirectSafe(returnUrl);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Register(string firstName, string lastName, string email, string password, string? returnUrl = null)
        {
            returnUrl ??= "/";

            string username = $"{firstName} {lastName}".Trim();
            string hash = Sha256(password);

            try
            {
                await using var conn = new MySqlConnection(_connStr);
                await conn.OpenAsync();

                const string sql = @"
INSERT INTO users (username, email, password_hash)
VALUES (@u, @e, @p);";

                await using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@e", email);
                cmd.Parameters.AddWithValue("@p", hash);

                await cmd.ExecuteNonQueryAsync();

                TempData["ShowAuthModal"] = "1";
                TempData["AuthTab"] = "login";
                TempData["AuthMsg"] = "Kayıt başarılı ✅ Şimdi giriş yapabilirsin.";
                return RedirectSafe(returnUrl);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                TempData["ShowAuthModal"] = "1";
                TempData["AuthTab"] = "register";
                TempData["AuthError"] = "Bu e-posta zaten kayıtlı.";
                return RedirectSafe(returnUrl);
            }
            catch
            {
                TempData["ShowAuthModal"] = "1";
                TempData["AuthTab"] = "register";
                TempData["AuthError"] = "Kayıt sırasında bir hata oluştu.";
                return RedirectSafe(returnUrl);
            }
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        private IActionResult RedirectSafe(string returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        private static string Sha256(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}