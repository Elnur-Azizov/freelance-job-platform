using Freelance_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
public class JobsController : Controller
{
    private readonly IConfiguration _configuration;

    public JobsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    private MySqlConnection OpenConn()
    {
        var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        conn.Open();
        return conn;
    }
    public IActionResult Index(string sort = "date_desc", string q = "")
    {
        var list = new List<dynamic>();

        // ✅ Sıralama whitelist (SQL Injection önlemi)
        string orderBy = sort switch
        {
            "title_asc" => "j.title ASC",
            "title_desc" => "j.title DESC",
            "budget_asc" => "j.budget ASC",
            "budget_desc" => "j.budget DESC",
            "date_asc" => "j.created_at ASC",
            "date_desc" => "j.created_at DESC",
            _ => "j.created_at DESC"
        };

        using var conn = OpenConn();

        // ✅ WHERE dinamik: arama varsa ekle
        string where = "WHERE j.status='open'";
        if (!string.IsNullOrWhiteSpace(q))
            where += " AND (j.title LIKE @q OR j.description LIKE @q)";

        string sql = $@"
SELECT j.job_id, j.title, j.description, j.budget, j.requested_days, j.status, j.created_at
FROM jobs j
{where}
ORDER BY {orderBy};";

        using var cmd = new MySqlCommand(sql, conn);

        if (!string.IsNullOrWhiteSpace(q))
            cmd.Parameters.AddWithValue("@q", "%" + q.Trim() + "%");

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            list.Add(new
            {
                job_id = rdr.GetInt32("job_id"),
                title = rdr.GetString("title"),
                description = rdr.GetString("description"),
                budget = rdr.GetInt32("budget"),
                requested_days = rdr.GetInt32("requested_days"),
                status = rdr.GetString("status"),
                created_at = rdr.IsDBNull(rdr.GetOrdinal("created_at")) ? (DateTime?)null : rdr.GetDateTime("created_at")
            });
        }

        ViewBag.Sort = sort;
        ViewBag.Q = q ?? "";

        return View(list);
    }

    // JOB DETAILS
    public IActionResult Details(int id)
    {
        Job? job = null;

        string connStr = _configuration.GetConnectionString("DefaultConnection");

        using (var conn = new MySqlConnection(connStr))
        {
            conn.Open();

            string query = @"
SELECT 
    j.job_id, j.client_id, j.title, j.description, j.budget, j.requested_days, j.status, j.created_at,
    u.username AS client_username
FROM jobs j
JOIN users u ON u.user_id = j.client_id
WHERE j.job_id = @id
LIMIT 1;
";

            using (var cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        job = new Job
                        {
                            job_id = reader.GetInt32("job_id"),
                            client_id = reader.GetInt32("client_id"),
                            title = reader.GetString("title"),
                            description = reader.GetString("description"),
                            budget = reader.GetInt32("budget"),
                            requested_days = reader.GetInt32("requested_days"),
                            status = reader.GetString("status"),
                            created_at = reader.GetDateTime("created_at"),
                            client_username = reader.GetString("client_username")
                        };
                    }
                }
            }
        }

        if (job == null) return NotFound();

        return View(job);
    }

    // GET: /Jobs/Create
    [HttpGet]
    public IActionResult Create()
    {
        var userId = HttpContext.Session.GetInt32("user_id");

        if (userId == null)
        {
            TempData["LoginRequired"] = "İş ilanı oluşturmak için giriş yapmalısınız.";
            return RedirectToAction("Index", "Jobs");
        }

        return View();
    }
    // GET: /Jobs/Edit/5
    [HttpGet]
    public IActionResult Edit(int id)
    {
        var userId = HttpContext.Session.GetInt32("user_id");
        if (userId == null) return RedirectToAction("Index", "Jobs");

        string connStr = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new MySqlConnection(connStr);
        conn.Open();

        // ilanı çek + sahip kontrolü
        string sql = @"SELECT job_id, client_id, title, description, budget, requested_days, status, created_at
                   FROM jobs
                   WHERE job_id=@id
                   LIMIT 1;";

        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", id);

        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return NotFound();

        int clientId = rdr.GetInt32("client_id");
        if (clientId != userId.Value) return Forbid();

        var job = new Job
        {
            job_id = rdr.GetInt32("job_id"),
            client_id = clientId,
            title = rdr.GetString("title"),
            description = rdr.IsDBNull(rdr.GetOrdinal("description")) ? "" : rdr.GetString("description"),
            budget = rdr.GetInt32("budget"),
            requested_days = rdr.GetInt32("requested_days"),
            status = rdr.GetString("status"),
            created_at = rdr.GetDateTime("created_at")
        };

        return View(job);
    }

    // POST: /Jobs/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(int id, string title, string description, int budget, int requested_days, string status)
    {
        var userId = HttpContext.Session.GetInt32("user_id");
        if (userId == null) return RedirectToAction("Index", "Jobs");

        // basit doğrulama
        if (string.IsNullOrWhiteSpace(title)) ModelState.AddModelError("title", "Başlık zorunludur.");
        if (budget <= 0) ModelState.AddModelError("budget", "Bütçe 0'dan büyük olmalı.");
        if (requested_days <= 0) ModelState.AddModelError("requested_days", "Süre 0'dan büyük olmalı.");
        if (status != "open" && status != "closed") status = "open";

        if (!ModelState.IsValid)
        {
            // formu tekrar göstermek için model geri bas
            return View(new Job { job_id = id, title = title, description = description, budget = budget, requested_days = requested_days, status = status });
        }

        string connStr = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new MySqlConnection(connStr);
        conn.Open();

        // yetki kontrolü (owner mı?)
        string ownerSql = "SELECT client_id FROM jobs WHERE job_id=@id LIMIT 1;";
        using (var ownerCmd = new MySqlCommand(ownerSql, conn))
        {
            ownerCmd.Parameters.AddWithValue("@id", id);
            var ownerObj = ownerCmd.ExecuteScalar();
            if (ownerObj == null) return NotFound();
            if (Convert.ToInt32(ownerObj) != userId.Value) return Forbid();
        }

        string sql = @"
UPDATE jobs
SET title=@t, description=@d, budget=@b, requested_days=@rd, status=@s
WHERE job_id=@id;
";
        using (var cmd = new MySqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@t", title);
            cmd.Parameters.AddWithValue("@d", description ?? "");
            cmd.Parameters.AddWithValue("@b", budget);
            cmd.Parameters.AddWithValue("@rd", requested_days);
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        TempData["Success"] = "İlan güncellendi ✅";
        return RedirectToAction("Details", new { id = id });
    }
    public IActionResult Delete(int id)
    {
        var userId = HttpContext.Session.GetInt32("user_id");
        if (userId == null) return RedirectToAction("Index", "Jobs");

        string connStr = _configuration.GetConnectionString("DefaultConnection");
        using var conn = new MySqlConnection(connStr);
        conn.Open();

        // owner kontrolü
        string ownerSql = "SELECT client_id FROM jobs WHERE job_id=@id LIMIT 1;";
        using (var ownerCmd = new MySqlCommand(ownerSql, conn))
        {
            ownerCmd.Parameters.AddWithValue("@id", id);
            var ownerObj = ownerCmd.ExecuteScalar();
            if (ownerObj == null) return NotFound();
            if (Convert.ToInt32(ownerObj) != userId.Value) return Forbid();
        }

        // Fiziksel silmek yerine: status='closed' yapalım (daha güvenli)
        string sql = "UPDATE jobs SET status='closed' WHERE job_id=@id;";
        using (var cmd = new MySqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        TempData["Success"] = "İlan kapatıldı ✅";
        return RedirectToAction("Index");
    }
    public IActionResult Create(Job model)
    {
        // Basit doğrulama
        if (string.IsNullOrWhiteSpace(model.title))
            ModelState.AddModelError("title", "Başlık zorunludur.");
        if (model.budget <= 0)
            ModelState.AddModelError("budget", "Bütçe 0'dan büyük olmalıdır.");
        if (model.requested_days <= 0)
            ModelState.AddModelError("requested_days", "Süre 0'dan büyük olmalıdır.");

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Form hatalı. Lütfen alanları kontrol et.";
            return View(model);
        }

        // TODO: client_id'yi session'dan alacağız.
        // Şimdilik test için 1 veriyoruz (istersen değiştir).
        var userId = HttpContext.Session.GetInt32("user_id");

        if (userId == null)
        {
            TempData["LoginRequired"] = "İş ilanı oluşturmak için giriş yapmalısınız.";
            return RedirectToAction("Index", "Jobs");
        }

        int clientId = userId.Value;

        string connStr = _configuration.GetConnectionString("DefaultConnection");

        using (var conn = new MySqlConnection(connStr))
        {
            conn.Open();

            string query = @"
INSERT INTO jobs (client_id, title, description, budget, requested_days, status, created_at)
VALUES (@client_id, @title, @description, @budget, @requested_days, @status, NOW());
";

            using (var cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@client_id", clientId);
                cmd.Parameters.AddWithValue("@title", model.title);
                cmd.Parameters.AddWithValue("@description", model.description ?? "");
                cmd.Parameters.AddWithValue("@budget", model.budget);
                cmd.Parameters.AddWithValue("@requested_days", model.requested_days);
                cmd.Parameters.AddWithValue("@status", "open");

                cmd.ExecuteNonQuery();
            }
        }

        TempData["Success"] = "İş ilanı başarıyla oluşturuldu.";
        return RedirectToAction("Index");
    }
}