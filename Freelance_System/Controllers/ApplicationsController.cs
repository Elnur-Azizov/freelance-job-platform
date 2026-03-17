using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Freelance_System.Models;
using System.Collections.Generic;
using System;

namespace Freelance_System.Controllers
{
    public class ApplicationsController : Controller
    {
        private readonly IConfiguration _configuration;

        public ApplicationsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Freelancer başvuru gönderir
        [HttpPost]
        public IActionResult Create(int jobId, decimal offeredPrice, int offeredDays, string message)
        {
            // Login kontrolü (session)
            var userId = HttpContext.Session.GetInt32("user_id");
            if (userId == null)
            {
                TempData["ShowAuthModal"] = "1";
                TempData["AuthTab"] = "login";
                TempData["AuthError"] = "Başvuru yapmak için giriş yapmalısın.";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }

            int freelancerId = userId.Value;

            string connStr = _configuration.GetConnectionString("DefaultConnection");

            // İlan açık mı kontrolü (DB)
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();

                string checkSql = "SELECT status, client_id FROM jobs WHERE job_id=@id LIMIT 1;";
                using var checkCmd = new MySqlCommand(checkSql, conn);
                checkCmd.Parameters.AddWithValue("@id", jobId);

                using var r = checkCmd.ExecuteReader();
                if (!r.Read())
                {
                    TempData["ApplyErr"] = "İlan bulunamadı.";
                    return RedirectToAction("Index", "Jobs");
                }

                string status = r.GetString("status");
                int ownerId = r.GetInt32("client_id");

                if (!string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["ApplyErr"] = "Bu ilan kapalı. Başvuru yapılamaz.";
                    return RedirectToAction("Details", "Jobs", new { id = jobId });
                }

                if (ownerId == freelancerId)
                {
                    TempData["ApplyErr"] = "Kendi ilanına başvuru yapamazsın.";
                    return RedirectToAction("Details", "Jobs", new { id = jobId });
                }
            }

            try
            {
                using (var conn = new MySqlConnection(connStr))
                {
                    conn.Open();

                    string sql = @"
INSERT INTO job_applications
(job_id, freelancer_id, offered_price, offered_days, message, status, created_at)
VALUES
(@job_id, @freelancer_id, @offered_price, @offered_days, @message, 'pending', NOW());
";

                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@job_id", jobId);
                        cmd.Parameters.AddWithValue("@freelancer_id", freelancerId);
                        cmd.Parameters.AddWithValue("@offered_price", offeredPrice);
                        cmd.Parameters.AddWithValue("@offered_days", offeredDays);
                        cmd.Parameters.AddWithValue("@message", message);

                        cmd.ExecuteNonQuery();
                    }
                }

                TempData["ApplyMsg"] = "Başvurun alındı ✅";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                TempData["ApplyErr"] = "Bu ilana zaten başvurmuşsun.";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }
            catch
            {
                TempData["ApplyErr"] = "Başvuru kaydedilirken hata oluştu.";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }
        }

        // Client: ilana gelen başvurular listesi
        [HttpGet]
        public IActionResult Index(int jobId)
        {
            var userId = HttpContext.Session.GetInt32("user_id");
            if (userId == null)
                return RedirectToAction("Index", "Jobs");

            int clientId = userId.Value;

            string connStr = _configuration.GetConnectionString("DefaultConnection");

            using var conn = new MySqlConnection(connStr);
            conn.Open();

            var list = new List<ApplicationRowVM>();

            string sql = @"
SELECT
    a.application_id AS ProposalId,
    a.job_id         AS JobId,
    a.freelancer_id  AS FreelancerId,
    u.username       AS Username,
    u.email          AS Email,
    a.offered_price  AS OfferedPrice,
    a.offered_days   AS OfferedDays,
    a.message        AS Message,
    a.created_at     AS CreatedAt
FROM job_applications a
JOIN users u ON u.user_id = a.freelancer_id
WHERE a.job_id = @jobId
ORDER BY a.created_at DESC;
";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@jobId", jobId);

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    list.Add(new ApplicationRowVM
                    {
                        ProposalId = rdr.GetInt32("ProposalId"),
                        JobId = rdr.GetInt32("JobId"),
                        FreelancerId = rdr.GetInt32("FreelancerId"),
                        Username = rdr.GetString("Username"),
                        Email = rdr.GetString("Email"),
                        OfferedPrice = rdr.GetInt32("OfferedPrice"),
                        OfferedDays = rdr.GetInt32("OfferedDays"),
                        Message = rdr.IsDBNull(rdr.GetOrdinal("Message")) ? null : rdr.GetString("Message"),
                        CreatedAt = rdr.GetDateTime("CreatedAt")
                    });
                }
            }

            return View(list);
        }

        // Client: tek bir başvurunun detayını görür
        [HttpGet]
        public IActionResult Details(int applicationId)
        {
            var userId = HttpContext.Session.GetInt32("user_id");
            if (userId == null) return RedirectToAction("Index", "Jobs");

            int clientId = userId.Value;

            string connStr = _configuration.GetConnectionString("DefaultConnection");
            using var conn = new MySqlConnection(connStr);
            conn.Open();

            // ✅ Yetki kontrolü: Bu başvuru, bu client’ın ilanına mı?
            string sql = @"
SELECT
    a.application_id AS ProposalId,
    a.job_id AS JobId,
    a.freelancer_id AS FreelancerId,
    u.username AS Username,
    u.email AS Email,
    a.offered_price AS OfferedPrice,
    a.offered_days AS OfferedDays,
    a.message AS Message,
    a.created_at AS CreatedAt,
    j.client_id AS ClientId,
    j.title AS JobTitle
FROM job_applications a
JOIN users u ON u.user_id = a.freelancer_id
JOIN jobs j ON j.job_id = a.job_id
WHERE a.application_id = @id
LIMIT 1;
";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", applicationId);

            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return NotFound();

            int ownerId = rdr.GetInt32("ClientId");
            if (ownerId != clientId) return Forbid();

            // ViewBag ile taşıyoruz
            ViewBag.ProposalId = rdr.GetInt32("ProposalId");
            ViewBag.JobId = rdr.GetInt32("JobId");
            ViewBag.FreelancerId = rdr.GetInt32("FreelancerId");
            ViewBag.Username = rdr.GetString("Username");
            ViewBag.Email = rdr.GetString("Email");
            ViewBag.OfferedPrice = rdr.GetInt32("OfferedPrice");
            ViewBag.OfferedDays = rdr.GetInt32("OfferedDays"); // ✅ EKLENDİ
            ViewBag.Message = rdr.IsDBNull(rdr.GetOrdinal("Message")) ? "" : rdr.GetString("Message");
            ViewBag.CreatedAt = rdr.GetDateTime("CreatedAt");
            ViewBag.JobTitle = rdr.GetString("JobTitle");

            return View();
        }
    }
}