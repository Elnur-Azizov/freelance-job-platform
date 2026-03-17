namespace Freelance_System.Models
{
        public class ProfileVM
        {
            // users
            public int UserId { get; set; }
            public string Username { get; set; } = "";
            public string Email { get; set; } = "";

            // private (only owner sees)
            public string? PhoneNo { get; set; }

            // profiles (common)
            public string? Bio { get; set; }
            public string? Skills { get; set; }
            public string? Location { get; set; }
            public string? Website { get; set; }
            public string? Github { get; set; }
            public string? Linkedin { get; set; }
            public string? AvatarUrl { get; set; }

            // freelancers (optional)
            public string? Title { get; set; }
            public string? PortfolioUrl { get; set; }
            public decimal? HourlyRate { get; set; }

            // clients (optional)
            public string? CompanyName { get; set; }
            public string? CompanyWebsite { get; set; }

            // helper
            public bool IsOwner { get; set; }
        }
    }
