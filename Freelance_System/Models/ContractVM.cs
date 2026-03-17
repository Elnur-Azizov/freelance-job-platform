namespace Freelance_System.Models
{
    public class ContractVM
    {
        public int contract_id { get; set; }
        public int job_id { get; set; }
        public string job_title { get; set; }

        public int freelancer_id { get; set; }
        public int agreed_price { get; set; }
        public string status { get; set; }
        public DateTime? created_at { get; set; }
        public int client_id { get; set; } // jobs tablosundan geliyor
        public string freelancer_username { get; set; }
        public string freelancer_email { get; set; }

        public string client_username { get; set; }
        public string client_email { get; set; }
    }
}