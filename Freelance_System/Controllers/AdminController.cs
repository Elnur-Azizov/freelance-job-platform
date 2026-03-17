using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace Freelance_System.Controllers
{
    public class AdminController : Controller
    {
        private readonly IConfiguration _configuration;
        public AdminController(IConfiguration configuration) => _configuration = configuration;

        private MySqlConnection OpenConn()
        {
            var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            conn.Open();
            return conn;
        }

        private bool IsAdmin()
        {
            return HttpContext.Session.GetInt32("is_admin") == 1;
        }

        private IActionResult AdminOnly()
        {
            if (!IsAdmin()) return RedirectToAction("Auth", "Account");
            return null;
        }

        // ===================== DASHBOARD =====================
        public IActionResult Index()
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            using var conn = OpenConn();
            using var cmd = new MySqlCommand("SELECT * FROM vw_admin_dashboard_counts;", conn);
            using var rdr = cmd.ExecuteReader();

            if (rdr.Read())
            {
                ViewBag.Users = rdr.GetInt32("total_users");
                ViewBag.JobsOpen = rdr.GetInt32("open_jobs");
                ViewBag.JobsClosed = rdr.GetInt32("closed_jobs");
                ViewBag.Applications = rdr.GetInt32("total_applications");
                ViewBag.ContractsActive = rdr.GetInt32("active_contracts");
            }

            return View();
        }

        // ===================== USERS =====================
        public IActionResult Users()
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            var list = new List<dynamic>();
            using var conn = OpenConn();

            var sql = @"
SELECT user_id, username, email, phone_no, is_banned, banned_at
FROM users
ORDER BY user_id DESC;";

            using var cmd = new MySqlCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();

            while (rdr.Read())
            {
                list.Add(new
                {
                    user_id = rdr.GetInt32("user_id"),
                    username = rdr.GetString("username"),
                    email = rdr.GetString("email"),
                    phone_no = rdr.IsDBNull(rdr.GetOrdinal("phone_no")) ? "" : rdr.GetString("phone_no"),
                    is_banned = rdr.GetInt32("is_banned"),
                    banned_at = rdr.IsDBNull(rdr.GetOrdinal("banned_at"))
                        ? (DateTime?)null
                        : rdr.GetDateTime("banned_at")
                });
            }

            return View(list);
        }
        [HttpPost]
        public IActionResult BanUser(int id)
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            using var conn = OpenConn();
            using var cmd = new MySqlCommand("CALL sp_ban_user(@id);", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return RedirectToAction("Users");
        }

        [HttpPost]
        public IActionResult UnbanUser(int id)
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            using var conn = OpenConn();
            using var cmd = new MySqlCommand("CALL sp_unban_user(@id);", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            return RedirectToAction("Users");
        }

        // ===================== BAN / UNBAN =====================
        [HttpPost]
        public IActionResult ToggleBan(int id)
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            using var conn = OpenConn();

            string sql = @"
UPDATE users
SET is_banned = CASE WHEN is_banned=0 THEN 1 ELSE 0 END,
    banned_at = CASE WHEN is_banned=0 THEN NOW() ELSE NULL END
WHERE user_id=@id;";

            using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction("Users");
        }

        // ===================== JOBS =====================
        public IActionResult Jobs(string status = "all", string sort = "date_desc")
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            string where = "";
            if (status == "open") where = "WHERE status='open'";
            else if (status == "closed") where = "WHERE status='closed'";

            string orderBy = sort switch
            {
                "title_asc" => "title ASC",
                "title_desc" => "title DESC",
                "budget_asc" => "budget ASC",
                "budget_desc" => "budget DESC",
                "date_asc" => "created_at ASC",
                _ => "created_at DESC"
            };

            var list = new List<dynamic>();
            using var conn = OpenConn();

            var sql = $"SELECT * FROM vw_admin_jobs {where} ORDER BY {orderBy};";
            using var cmd = new MySqlCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();

            while (rdr.Read())
            {
                list.Add(new
                {
                    job_id = rdr.GetInt32("job_id"),
                    title = rdr.GetString("title"),
                    budget = rdr.GetInt32("budget"),
                    requested_days = rdr.GetInt32("requested_days"),
                    status = rdr.GetString("status"),
                    created_at = rdr.IsDBNull(rdr.GetOrdinal("created_at")) ? (DateTime?)null : rdr.GetDateTime("created_at"),
                    client_name = rdr.GetString("client_name"),
                    client_email = rdr.GetString("client_email"),
                    application_count = rdr.GetInt32("application_count")
                });
            }

            ViewBag.Filter = status;
            ViewBag.Sort = sort;
            return View(list);
        }

        [HttpPost]
        public IActionResult CloseJob(int id)
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            using var conn = OpenConn();
            using var cmd = new MySqlCommand(
                "UPDATE jobs SET status='closed' WHERE job_id=@id;", conn);

            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction("Jobs");
        }

        // ===================== APPLICATIONS =====================
        public IActionResult Applications(string status = "all")
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            string where = "";
            if (status != "all") where = "WHERE status=@st";

            var list = new List<dynamic>();
            using var conn = OpenConn();

            var sql = $"SELECT * FROM vw_admin_applications {where} ORDER BY application_id DESC;";
            using var cmd = new MySqlCommand(sql, conn);
            if (status != "all") cmd.Parameters.AddWithValue("@st", status);

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                list.Add(new
                {
                    application_id = rdr.GetInt32("application_id"),
                    job_id = rdr.GetInt32("job_id"),
                    job_title = rdr.GetString("job_title"),
                    status = rdr.GetString("status"),
                    offered_price = rdr.GetInt32("offered_price"),
                    offered_days = rdr.GetInt32("offered_days"),
                    created_at = rdr.IsDBNull(rdr.GetOrdinal("created_at")) ? (DateTime?)null : rdr.GetDateTime("created_at"),
                    freelancer_name = rdr.GetString("freelancer_name"),
                    freelancer_email = rdr.GetString("freelancer_email"),
                    client_name = rdr.GetString("client_name"),
                    client_email = rdr.GetString("client_email")
                });
            }

            ViewBag.Filter = status;
            return View(list);
        }
        [HttpPost]
        public IActionResult AcceptApplication(int id)
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            using var conn = OpenConn();
            using var cmd = new MySqlCommand("CALL sp_accept_application(@id);", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction("Applications");
        }
        [HttpPost]
        public IActionResult RejectApplication(int id)
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            using var conn = OpenConn();
            using var cmd = new MySqlCommand("UPDATE job_applications SET status='rejected' WHERE application_id=@id;", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            return RedirectToAction("Applications");
        }

        // ===================== CONTRACTS =====================
        public IActionResult Contracts(string status = "all")
        {
            var gate = AdminOnly();
            if (gate != null) return gate;

            var list = new List<dynamic>();
            using var conn = OpenConn();

            string where = "";
            if (status != "all") where = "WHERE c.status=@st";

            var sql = $@"
SELECT c.contract_id, c.job_id, j.title AS job_title,
       c.status, c.agreed_price, c.created_at,
       uf.username AS freelancer_name, uf.email AS freelancer_email,
       uc.username AS client_name, uc.email AS client_email
FROM contracts c
INNER JOIN jobs j ON j.job_id = c.job_id
INNER JOIN users uf ON uf.user_id = c.freelancer_id
INNER JOIN users uc ON uc.user_id = j.client_id
{where}
ORDER BY c.contract_id DESC;";

            using var cmd = new MySqlCommand(sql, conn);
            if (status != "all")
                cmd.Parameters.AddWithValue("@st", status);

            using var rdr = cmd.ExecuteReader();

            while (rdr.Read())
            {
                list.Add(new
                {
                    contract_id = rdr.GetInt32("contract_id"),
                    job_id = rdr.GetInt32("job_id"),
                    job_title = rdr.GetString("job_title"),
                    status = rdr.GetString("status"),
                    agreed_price = rdr.GetInt32("agreed_price"),
                    created_at = rdr.IsDBNull(rdr.GetOrdinal("created_at"))
                        ? (DateTime?)null
                        : rdr.GetDateTime("created_at"),
                    freelancer_name = rdr.GetString("freelancer_name"),
                    freelancer_email = rdr.GetString("freelancer_email"),
                    client_name = rdr.GetString("client_name"),
                    client_email = rdr.GetString("client_email")
                });
            }

            ViewBag.Filter = status;
            return View(list);
        }
    }
}