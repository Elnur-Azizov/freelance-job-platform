namespace Freelance_System.Models
{
    public class MyApplicationVM
    {
        public int application_id { get; set; }
        public int job_id { get; set; }
        public string job_title { get; set; }

        public int offered_price { get; set; }
        public int offered_days { get; set; }
        public string message { get; set; }

        public string status { get; set; }
        public DateTime? created_at { get; set; }
    }
}