using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Freelance_System.Controllers
{
    public class ContractsController : Controller
    {
        private readonly IConfiguration _configuration;

        public ContractsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private MySqlConnection OpenConn()
        {
            var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            conn.Open();
            return conn;
        }

        // GET: /Contracts/Create?jobId=7&freelancerId=2&proposalId=10
        [HttpGet]
        public IActionResult Create(int jobId, int freelancerId, int proposalId)
        {
            // 1) login kontrol
            var userId = HttpContext.Session.GetInt32("user_id");
            if (userId == null)
                return RedirectToAction("Index", "Jobs");

            int clientId = userId.Value;

            using var conn = OpenConn();

            // 2) Yetki + ilan açık mı + başvuru bu ilana mı ait?
            // (Burada c.contract_id yoktu, o yüzden fn_last_delivery_status kaldırıldı)
            string checkSql = @"
SELECT 
    j.client_id AS ClientId,
    j.status    AS JobStatus,
    a.offered_price AS OfferedPrice
FROM jobs j
JOIN job_applications a ON a.job_id = j.job_id
WHERE j.job_id = @jobId
  AND a.application_id = @proposalId
  AND a.freelancer_id = @freelancerId
LIMIT 1;
";
            using var checkCmd = new MySqlCommand(checkSql, conn);
            checkCmd.Parameters.AddWithValue("@jobId", jobId);
            checkCmd.Parameters.AddWithValue("@proposalId", proposalId);
            checkCmd.Parameters.AddWithValue("@freelancerId", freelancerId);

            using var rdr = checkCmd.ExecuteReader();
            if (!rdr.Read())
            {
                TempData["SelectErr"] = "Başvuru bulunamadı.";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }

            int ownerId = rdr.GetInt32("ClientId");
            string jobStatus = rdr.GetString("JobStatus");
            int agreedPrice = rdr.GetInt32("OfferedPrice");

            if (ownerId != clientId)
                return Forbid();

            if (!string.Equals(jobStatus, "open", StringComparison.OrdinalIgnoreCase))
            {
                TempData["SelectErr"] = "Bu ilan zaten kapalı.";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }

            rdr.Close();

            // 3) Transaction: contract + status update + job closed
            using var tx = conn.BeginTransaction();

            try
            {
                // 3.1 contract ekle
                string insertContract = @"
INSERT INTO contracts (job_id, freelancer_id, agreed_price, status, created_at)
VALUES (@jobId, @freelancerId, @price, 'active', NOW());
";
                using (var cmd = new MySqlCommand(insertContract, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@jobId", jobId);
                    cmd.Parameters.AddWithValue("@freelancerId", freelancerId);
                    cmd.Parameters.AddWithValue("@price", agreedPrice);
                    cmd.ExecuteNonQuery();
                }

                // 3.2 seçilen başvuru accepted
                string acceptSql = @"
UPDATE job_applications
SET status = 'accepted'
WHERE application_id = @proposalId;
";
                using (var cmd = new MySqlCommand(acceptSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@proposalId", proposalId);
                    cmd.ExecuteNonQuery();
                }

                // 3.3 diğer başvurular rejected
                string rejectSql = @"
UPDATE job_applications
SET status = 'rejected'
WHERE job_id = @jobId
  AND application_id <> @proposalId;
";
                using (var cmd = new MySqlCommand(rejectSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@jobId", jobId);
                    cmd.Parameters.AddWithValue("@proposalId", proposalId);
                    cmd.ExecuteNonQuery();
                }

                // 3.4 job kapat
                string closeJob = @"
UPDATE jobs
SET status = 'closed'
WHERE job_id = @jobId;
";
                using (var cmd = new MySqlCommand(closeJob, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@jobId", jobId);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();

                TempData["SelectMsg"] = "Freelancer seçildi ✅ İlan kapatıldı.";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                tx.Rollback();
                TempData["SelectErr"] = "Bu ilan için zaten contract oluşturulmuş olabilir.";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }
            catch
            {
                tx.Rollback();
                TempData["SelectErr"] = "Seçim yapılırken hata oluştu.";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }
        }

        // GET: /Contracts/Details/5
        public IActionResult Details(int id)
        {
            using var conn = OpenConn();

            // 1) Contract bilgisi
            string sql = @"
SELECT 
    c.contract_id,
    c.job_id,
    c.freelancer_id,
    c.agreed_price,
    c.status,
    fn_last_delivery_status(c.contract_id) AS delivery_status,
    j.client_id
FROM contracts c
JOIN jobs j ON j.job_id = c.job_id
WHERE c.contract_id = @id
LIMIT 1;
";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return NotFound();

            string delStatus = rdr.IsDBNull(rdr.GetOrdinal("delivery_status"))
                ? "none"
                : rdr.GetString("delivery_status");

            int contractId = rdr.GetInt32("contract_id");
            int clientId = rdr.GetInt32("client_id");

            // dynamic model
            var model = new
            {
                contract_id = contractId,
                job_id = rdr.GetInt32("job_id"),
                freelancer_id = rdr.GetInt32("freelancer_id"),
                agreed_price = rdr.GetInt32("agreed_price"),
                status = rdr.GetString("status"),
                delivery_status = delStatus,
                client_id = clientId,
                deliveries = new List<dynamic>()
            };

            rdr.Close();

            // 2) Deliveries listesi (file_url EKLENDİ ✅)
            string sqlDel = @"
SELECT 
    d.delivery_id,
    d.note,
    d.status,
    d.created_at,
    d.file_url,
    u.username AS submitted_by_name
FROM deliveries d
JOIN users u ON u.user_id = d.submitted_by
WHERE d.contract_id = @cid
ORDER BY d.created_at DESC;
";
            using var cmdDel = new MySqlCommand(sqlDel, conn);
            cmdDel.Parameters.AddWithValue("@cid", contractId);

            using var dr = cmdDel.ExecuteReader();
            while (dr.Read())
            {
                model.deliveries.Add(new
                {
                    delivery_id = dr.GetInt32("delivery_id"),
                    note = dr.IsDBNull(dr.GetOrdinal("note")) ? "" : dr.GetString("note"),
                    status = dr.GetString("status"),
                    created_at = dr.GetDateTime("created_at"),
                    submitted_by_name = dr.GetString("submitted_by_name"),
                    file_url = dr.IsDBNull(dr.GetOrdinal("file_url")) ? "" : dr.GetString("file_url")
                });
            }

            return View(model);
        }

        // (Opsiyonel) GET ekranı kullanmıyorsan hiç şart değil.
        [HttpGet]
        public IActionResult SubmitDelivery(int id) // id = contract_id
        {
            var userId = HttpContext.Session.GetInt32("user_id");
            if (userId == null) return RedirectToAction("Index", "Jobs");

            using var conn = OpenConn();
            string check = "SELECT freelancer_id FROM contracts WHERE contract_id=@id LIMIT 1;";
            using var cmd = new MySqlCommand(check, conn);
            cmd.Parameters.AddWithValue("@id", id);

            var freelancerIdObj = cmd.ExecuteScalar();
            if (freelancerIdObj == null) return NotFound();

            int freelancerId = Convert.ToInt32(freelancerIdObj);
            if (freelancerId != userId.Value) return Forbid();

            return View(new { contract_id = id });
        }

        [HttpPost]
        public IActionResult SubmitDelivery(int contract_id, string note, IFormFile file)
        {
            var userId = HttpContext.Session.GetInt32("user_id");
            if (userId == null) return RedirectToAction("Auth", "Account");

            // Yetki: sadece bu contract'ın freelancer'ı gönderebilsin
            using (var connCheck = OpenConn())
            {
                string check = "SELECT freelancer_id FROM contracts WHERE contract_id=@cid LIMIT 1;";
                using var cmdCheck = new MySqlCommand(check, connCheck);
                cmdCheck.Parameters.AddWithValue("@cid", contract_id);

                var freelancerIdObj = cmdCheck.ExecuteScalar();
                if (freelancerIdObj == null) return NotFound();

                int freelancerId = Convert.ToInt32(freelancerIdObj);
                if (freelancerId != userId.Value) return Forbid();
            }
            // Son teslim durumuna göre yeni teslim izni
            using (var connState = OpenConn())
            {
                string stateSql = @"
SELECT status
FROM deliveries
WHERE contract_id = @cid
ORDER BY delivery_id DESC
LIMIT 1;
";
                using var cmdState = new MySqlCommand(stateSql, connState);
                cmdState.Parameters.AddWithValue("@cid", contract_id);

                var lastStatusObj = cmdState.ExecuteScalar();

                if (lastStatusObj != null)
                {
                    string lastStatus = lastStatusObj.ToString()!.ToLowerInvariant();

                    if (lastStatus == "submitted")
                    {
                        TempData["Err"] = "Zaten bekleyen bir teslim var. Önce client incelemeli.";
                        return RedirectToAction("Details", new { id = contract_id });
                    }

                    if (lastStatus == "approved")
                    {
                        TempData["Err"] = "Bu işin teslimi zaten onaylanmış.";
                        return RedirectToAction("Details", new { id = contract_id });
                    }
                }
            }

            string fileUrl = null;

            if (file != null && file.Length > 0)
            {
                var allowed = new[] { ".pdf", ".zip", ".rar", ".png", ".jpg", ".jpeg", ".doc", ".docx" };
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

                if (!allowed.Contains(ext))
                {
                    TempData["Err"] = "Bu dosya tipi desteklenmiyor.";
                    return RedirectToAction("Details", new { id = contract_id });
                }

                var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "deliveries");
                Directory.CreateDirectory(uploadsRoot);

                var safeName = $"{Guid.NewGuid():N}{ext}";
                var savePath = Path.Combine(uploadsRoot, safeName);

                using (var stream = new FileStream(savePath, FileMode.Create))
                    file.CopyTo(stream);

                fileUrl = $"/uploads/deliveries/{safeName}";
            }

            using var conn = OpenConn();
            string sql = @"
INSERT INTO deliveries (contract_id, submitted_by, note, status, created_at, file_url)
VALUES (@cid, @uid, @note, 'submitted', NOW(), @fileUrl);
";
            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@cid", contract_id);
            cmd.Parameters.AddWithValue("@uid", userId.Value);
            cmd.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(note) ? (object)DBNull.Value : note);
            cmd.Parameters.AddWithValue("@fileUrl", (object?)fileUrl ?? DBNull.Value);
            cmd.ExecuteNonQuery();

            TempData["Msg"] = "Teslim gönderildi ✅";
            return RedirectToAction("Details", new { id = contract_id });
        }

        // Client: Revize/Onay (son teslimi veya belirli delivery’i güncelle)
        [HttpPost]
        [HttpPost]
        public IActionResult SetDeliveryStatus(int contract_id, string status, int? delivery_id)
        {
            var userId = HttpContext.Session.GetInt32("user_id");
            if (userId == null) return RedirectToAction("Auth", "Account");

            using var conn = OpenConn();

            // 1) client kontrol
            string ownerSql = @"
SELECT j.client_id
FROM contracts c
JOIN jobs j ON j.job_id = c.job_id
WHERE c.contract_id = @cid
LIMIT 1;
";
            using (var cmdOwner = new MySqlCommand(ownerSql, conn))
            {
                cmdOwner.Parameters.AddWithValue("@cid", contract_id);
                var ownerObj = cmdOwner.ExecuteScalar();
                if (ownerObj == null) return NotFound();

                int clientId = Convert.ToInt32(ownerObj);
                if (clientId != userId.Value) return Forbid();
            }

            status = (status ?? "").ToLowerInvariant();
            if (status != "revision" && status != "approved")
                return BadRequest("Invalid status");

            // 2) revize limiti kontrolü
            if (status == "revision")
            {
                string revSql = @"
SELECT revision_count, revision_limit
FROM contracts
WHERE contract_id = @cid
LIMIT 1;
";
                using var cmdRev = new MySqlCommand(revSql, conn);
                cmdRev.Parameters.AddWithValue("@cid", contract_id);

                using var revRdr = cmdRev.ExecuteReader();
                if (!revRdr.Read()) return NotFound();

                int revisionCount = revRdr.GetInt32("revision_count");
                int revisionLimit = revRdr.GetInt32("revision_limit");
                revRdr.Close();

                if (revisionCount >= revisionLimit)
                {
                    TempData["Err"] = $"Revize limiti doldu. En fazla {revisionLimit} kez revize istenebilir.";
                    return RedirectToAction("Details", new { id = contract_id });
                }
            }

            // 3) sadece seçilen teslimi güncelle, seçilmemişse son teslimi güncelle
            string updSql;
            if (delivery_id.HasValue)
            {
                // sadece submitted durumundaki teslim işlenebilsin
                updSql = @"
UPDATE deliveries
SET status = @st
WHERE contract_id = @cid
  AND delivery_id = @did
  AND status = 'submitted'
LIMIT 1;
";
            }
            else
            {
                updSql = @"
UPDATE deliveries
SET status = @st
WHERE contract_id = @cid
  AND status = 'submitted'
ORDER BY delivery_id DESC
LIMIT 1;
";
            }

            using (var cmdUpd = new MySqlCommand(updSql, conn))
            {
                cmdUpd.Parameters.AddWithValue("@st", status);
                cmdUpd.Parameters.AddWithValue("@cid", contract_id);
                if (delivery_id.HasValue)
                    cmdUpd.Parameters.AddWithValue("@did", delivery_id.Value);

                int affected = cmdUpd.ExecuteNonQuery();
                if (affected == 0)
                {
                    TempData["Err"] = "Bu teslim zaten işlenmiş olabilir veya uygun teslim bulunamadı.";
                    return RedirectToAction("Details", new { id = contract_id });
                }
            }

            // 4) revize sayısını artır
            if (status == "revision")
            {
                string incSql = @"
UPDATE contracts
SET revision_count = revision_count + 1
WHERE contract_id = @cid;
";
                using var cmdInc = new MySqlCommand(incSql, conn);
                cmdInc.Parameters.AddWithValue("@cid", contract_id);
                cmdInc.ExecuteNonQuery();
            }

            TempData["Msg"] = status == "approved"
                ? "Teslim onaylandı ✅"
                : "Revize istendi ✏️";

            return RedirectToAction("Details", new { id = contract_id });
        }
    }
}