namespace Freelance_System.Models
{
    public class ApplicationRowVM
    {
        public int ProposalId { get; set; }
        public int JobId { get; set; }

        public int FreelancerId { get; set; }
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";

        public int OfferedPrice { get; set; }
        public int OfferedDays { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}