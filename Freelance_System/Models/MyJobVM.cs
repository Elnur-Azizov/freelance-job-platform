namespace Freelance_System.Models
{
    public class MyJobVM
    {
        public int job_id { get; set; }
        public string title { get; set; }
        public int budget { get; set; }
        public int requested_days { get; set; }
        public string status { get; set; }
        public DateTime? created_at { get; set; }
    }
}