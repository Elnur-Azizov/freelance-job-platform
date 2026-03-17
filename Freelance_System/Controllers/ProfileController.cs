using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Freelance_System.Models;
using System;

namespace Freelance_System.Controllers
{
    public class ProfileController : Controller
    {
        private readonly IConfiguration _configuration;

        public ProfileController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // /Profile  -> kendi profilin (login şart)
        // /Profile/Index?id=5 -> başkasının profili (public)
        [HttpGet]
        public IActionResult Index(int? id)
        {
            var sessionUserId = HttpContext.Session.GetInt32("user_id");
            if (sessionUserId == null)
                return RedirectToAction("Index", "Jobs");

            int targetUserId = id ?? sessionUserId.Value;

            var vm = LoadProfile(targetUserId);
            if (vm == null) return NotFound();

            vm.IsOwner = (targetUserId == sessionUserId.Value);

            // phone sadece sahibine görünsün
            if (!vm.IsOwner) vm.PhoneNo = null;

            return View(vm);
        }

        // GET /Profile/Edit
        [HttpGet]
        public IActionResult Edit()
        {
            var sessionUserId = HttpContext.Session.GetInt32("user_id");
            if (sessionUserId == null)
                return RedirectToAction("Index", "Jobs");

            var vm = LoadProfile(sessionUserId.Value) ?? new ProfileVM { UserId = sessionUserId.Value };
            vm.IsOwner = true;
            return View(vm);
        }

        // POST /Profile/Edit
        [HttpPost]
        public IActionResult Edit(ProfileVM model)
        {
            var sessionUserId = HttpContext.Session.GetInt32("user_id");
            if (sessionUserId == null)
                return RedirectToAction("Index", "Jobs");

            if (model.UserId != sessionUserId.Value)
                return Forbid();

            string connStr = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new MySqlConnection(connStr);
            conn.Open();

            // users.phone_no (opsiyonel)
            using (var cmd = new MySqlCommand("UPDATE users SET phone_no=@p WHERE user_id=@id", conn))
            {
                cmd.Parameters.AddWithValue("@p", (object?)model.PhoneNo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", model.UserId);
                cmd.ExecuteNonQuery();
            }

            // profiles (1:1) -> upsert
            string upsertProfile = @"
INSERT INTO profiles (user_id, bio, skills, location, website, github, linkedin, avatar_url)
VALUES (@uid, @bio, @skills, @loc, @web, @gh, @li, @av)
ON DUPLICATE KEY UPDATE
  bio = VALUES(bio),
  skills = VALUES(skills),
  location = VALUES(location),
  website = VALUES(website),
  github = VALUES(github),
  linkedin = VALUES(linkedin),
  avatar_url = VALUES(avatar_url);
";
            using (var cmd = new MySqlCommand(upsertProfile, conn))
            {
                cmd.Parameters.AddWithValue("@uid", model.UserId);
                cmd.Parameters.AddWithValue("@bio", (object?)model.Bio ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@skills", (object?)model.Skills ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@loc", (object?)model.Location ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@web", (object?)model.Website ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@gh", (object?)model.Github ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@li", (object?)model.Linkedin ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@av", (object?)model.AvatarUrl ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // freelancers -> upsert (title zaten var, biz de güncelliyoruz)
            string upsertFreelancer = @"
INSERT INTO freelancers (user_id, title, portfolio_url, hourly_rate)
VALUES (@uid, @t, @p, @r)
ON DUPLICATE KEY UPDATE
  title = VALUES(title),
  portfolio_url = VALUES(portfolio_url),
  hourly_rate = VALUES(hourly_rate);
";
            using (var cmd = new MySqlCommand(upsertFreelancer, conn))
            {
                cmd.Parameters.AddWithValue("@uid", model.UserId);
                cmd.Parameters.AddWithValue("@t", (object?)model.Title ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@p", (object?)model.PortfolioUrl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@r", (object?)model.HourlyRate ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // clients -> upsert
            string upsertClient = @"
INSERT INTO clients (user_id, company_name, company_website)
VALUES (@uid, @c, @w)
ON DUPLICATE KEY UPDATE
  company_name = VALUES(company_name),
  company_website = VALUES(company_website);
";
            using (var cmd = new MySqlCommand(upsertClient, conn))
            {
                cmd.Parameters.AddWithValue("@uid", model.UserId);
                cmd.Parameters.AddWithValue("@c", (object?)model.CompanyName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@w", (object?)model.CompanyWebsite ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            TempData["ProfileMsg"] = "Profil güncellendi ✅";
            return RedirectToAction("Index", new { id = model.UserId });
        }

            private MySqlConnection OpenConn()
            {
                var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                return conn;
            }

            // ✅ Freelancer: Başvurularım
            public IActionResult MyApplications()
            {
                int? userId = HttpContext.Session.GetInt32("user_id");
                if (userId == null) return RedirectToAction("Auth", "Account");

                var list = new List<MyApplicationVM>();

                using var conn = OpenConn();

                string sql = @"
            SELECT a.application_id, a.job_id, j.title AS job_title,
                   a.offered_price, a.offered_days, a.message,
                   a.status, a.created_at
            FROM job_applications a
            INNER JOIN jobs j ON j.job_id = a.job_id
            WHERE a.freelancer_id = @uid
            ORDER BY a.created_at DESC;
        ";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@uid", userId.Value);

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    list.Add(new MyApplicationVM
                    {
                        application_id = rdr.GetInt32("application_id"),
                        job_id = rdr.GetInt32("job_id"),
                        job_title = rdr.GetString("job_title"),

                        offered_price = rdr.GetInt32("offered_price"),
                        offered_days = rdr.GetInt32("offered_days"),
                        message = rdr.IsDBNull(rdr.GetOrdinal("message")) ? "" : rdr.GetString("message"),

                        status = rdr.GetString("status"),
                        created_at = rdr.IsDBNull(rdr.GetOrdinal("created_at")) ? null : rdr.GetDateTime("created_at")
                    });
                }

                return View(list);
            }

            // ✅ Client: İş ilanlarım
            public IActionResult MyJobs()
            {
                int? userId = HttpContext.Session.GetInt32("user_id");
                if (userId == null) return RedirectToAction("Auth", "Account");

                var list = new List<MyJobVM>();

                using var conn = OpenConn();

                string sql = @"
            SELECT job_id, title, budget, requested_days, status, created_at
            FROM jobs
            WHERE client_id = @uid AND status <> 'closed'
            ORDER BY created_at DESC;
        ";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@uid", userId.Value);

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    list.Add(new MyJobVM
                    {
                        job_id = rdr.GetInt32("job_id"),
                        title = rdr.GetString("title"),
                        budget = rdr.GetInt32("budget"),
                        requested_days = rdr.GetInt32("requested_days"),
                        status = rdr.GetString("status"),
                        created_at = rdr.IsDBNull(rdr.GetOrdinal("created_at")) ? null : rdr.GetDateTime("created_at")
                    });
                }

                return View(list);
            }

        // ✅ Contracts: (client veya freelancer görüntülesin)
        public IActionResult Contracts()
        {
            int? userId = HttpContext.Session.GetInt32("user_id");
            if (userId == null) return RedirectToAction("Auth", "Account");

            var vm = new ContractsPageVM();

            using var conn = OpenConn();

            string sql = @"
    SELECT 
        c.contract_id, c.job_id, j.title AS job_title,
        c.freelancer_id, c.agreed_price, c.status, c.created_at,
        j.client_id,

        uf.username AS freelancer_username, uf.email AS freelancer_email,
        uc.username AS client_username, uc.email AS client_email
    FROM contracts c
    INNER JOIN jobs j ON j.job_id = c.job_id
    INNER JOIN users uf ON uf.user_id = c.freelancer_id
    INNER JOIN users uc ON uc.user_id = j.client_id
    WHERE c.freelancer_id = @uid OR j.client_id = @uid
    ORDER BY c.created_at DESC;
";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@uid", userId.Value);

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var item = new ContractVM
                {
                    contract_id = rdr.GetInt32("contract_id"),
                    job_id = rdr.GetInt32("job_id"),
                    job_title = rdr.GetString("job_title"),

                    freelancer_id = rdr.GetInt32("freelancer_id"),
                    agreed_price = rdr.GetInt32("agreed_price"),
                    status = rdr.GetString("status"),
                    created_at = rdr.IsDBNull(rdr.GetOrdinal("created_at")) ? null : rdr.GetDateTime("created_at"),

                    client_id = rdr.GetInt32("client_id"),

                    freelancer_username = rdr.GetString("freelancer_username"),
                    freelancer_email = rdr.GetString("freelancer_email"),
                    client_username = rdr.GetString("client_username"),
                    client_email = rdr.GetString("client_email")
                };

                // Ayırma
                if (item.client_id == userId.Value)
                    vm.AsClient.Add(item);
                else
                    vm.AsFreelancer.Add(item);
            }

            return View(vm);
        }

        private ProfileVM? LoadProfile(int userId)
        {
            string connStr = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new MySqlConnection(connStr);
            conn.Open();

            string sql = @"
SELECT
  u.user_id AS UserId,
  u.username AS Username,
  u.email AS Email,
  u.phone_no AS PhoneNo,

  p.bio AS Bio,
  p.skills AS Skills,
  p.location AS Location,
  p.website AS Website,
  p.github AS Github,
  p.linkedin AS Linkedin,
  p.avatar_url AS AvatarUrl,

  f.title AS Title,
  f.portfolio_url AS PortfolioUrl,
  f.hourly_rate AS HourlyRate,

  c.company_name AS CompanyName,
  c.company_website AS CompanyWebsite
FROM users u
LEFT JOIN profiles p ON p.user_id = u.user_id
LEFT JOIN freelancers f ON f.user_id = u.user_id
LEFT JOIN clients c ON c.user_id = u.user_id
WHERE u.user_id = @id
LIMIT 1;
";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", userId);

            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return null;

            decimal? hourly = null;
            if (!rdr.IsDBNull(rdr.GetOrdinal("HourlyRate")))
                hourly = rdr.GetDecimal("HourlyRate");

            return new ProfileVM
            {
                UserId = rdr.GetInt32("UserId"),
                Username = rdr.GetString("Username"),
                Email = rdr.GetString("Email"),
                PhoneNo = rdr.IsDBNull(rdr.GetOrdinal("PhoneNo")) ? null : rdr.GetString("PhoneNo"),

                Bio = rdr.IsDBNull(rdr.GetOrdinal("Bio")) ? null : rdr.GetString("Bio"),
                Skills = rdr.IsDBNull(rdr.GetOrdinal("Skills")) ? null : rdr.GetString("Skills"),
                Location = rdr.IsDBNull(rdr.GetOrdinal("Location")) ? null : rdr.GetString("Location"),
                Website = rdr.IsDBNull(rdr.GetOrdinal("Website")) ? null : rdr.GetString("Website"),
                Github = rdr.IsDBNull(rdr.GetOrdinal("Github")) ? null : rdr.GetString("Github"),
                Linkedin = rdr.IsDBNull(rdr.GetOrdinal("Linkedin")) ? null : rdr.GetString("Linkedin"),
                AvatarUrl = rdr.IsDBNull(rdr.GetOrdinal("AvatarUrl")) ? null : rdr.GetString("AvatarUrl"),

                Title = rdr.IsDBNull(rdr.GetOrdinal("Title")) ? null : rdr.GetString("Title"),
                PortfolioUrl = rdr.IsDBNull(rdr.GetOrdinal("PortfolioUrl")) ? null : rdr.GetString("PortfolioUrl"),
                HourlyRate = hourly,

                CompanyName = rdr.IsDBNull(rdr.GetOrdinal("CompanyName")) ? null : rdr.GetString("CompanyName"),
                CompanyWebsite = rdr.IsDBNull(rdr.GetOrdinal("CompanyWebsite")) ? null : rdr.GetString("CompanyWebsite"),
            };
        }
    }
}