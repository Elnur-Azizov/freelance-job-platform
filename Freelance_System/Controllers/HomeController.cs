using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using System;
using System.Collections.Generic;

namespace Freelance_System.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;

        public HomeController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private MySqlConnection OpenConn()
        {
            var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            conn.Open();
            return conn;
        }

        // GET: /
        public IActionResult Index(string q = "")
        {
            using var conn = OpenConn();

            int Count(string sql)
            {
                using var cmd = new MySqlCommand(sql, conn);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }

            // ✅ İstatistikler (üst özet kartı)
            ViewBag.Users = Count("SELECT COUNT(*) FROM users");
            ViewBag.ActiveJobs = Count("SELECT COUNT(*) FROM jobs WHERE status='open'");
            ViewBag.TotalApplications = Count("SELECT COUNT(*) FROM job_applications");
            ViewBag.ActiveContracts = Count("SELECT COUNT(*) FROM contracts WHERE status='active'");

            // ✅ Arama metni (istersen ana sayfada kullanırsın, kullanmazsan sorun değil)
            ViewBag.Q = q ?? "";

            // ✅ Son eklenen ilanlar (ana sayfada 2-3 tane göstermek için)
            var list = new List<dynamic>();

            string where = "";
            if (!string.IsNullOrWhiteSpace(q))
                where = " AND (j.title LIKE @q OR j.description LIKE @q)";

            string sql = $@"
SELECT j.job_id, j.title, j.description, j.budget, j.requested_days, j.created_at
FROM jobs j
WHERE j.status='open' {where}
ORDER BY j.created_at DESC
LIMIT 3;";

            using var cmd2 = new MySqlCommand(sql, conn);

            if (!string.IsNullOrWhiteSpace(q))
                cmd2.Parameters.AddWithValue("@q", "%" + q.Trim() + "%");

            using var rdr = cmd2.ExecuteReader();
            while (rdr.Read())
            {
                list.Add(new
                {
                    job_id = rdr.GetInt32("job_id"),
                    title = rdr.GetString("title"),
                    description = rdr.IsDBNull(rdr.GetOrdinal("description")) ? "" : rdr.GetString("description"),
                    budget = rdr.GetInt32("budget"),
                    requested_days = rdr.GetInt32("requested_days"),
                    created_at = rdr.IsDBNull(rdr.GetOrdinal("created_at")) ? (DateTime?)null : rdr.GetDateTime("created_at")
                });
            }

            return View(list);
        }

        public IActionResult Privacy()
        {
            return View();
        }
    }
}