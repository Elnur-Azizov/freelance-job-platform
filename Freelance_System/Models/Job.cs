namespace Freelance_System.Models
{
    public class Job
    {
        public string? client_username { get; set; }
        public int job_id {  get; set; }
        public int client_id { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public int budget { get; set; }
        public int requested_days { get; set; }
        public string status { get; set; } = "open";
        public DateTime created_at { get; set; }

    }
}
