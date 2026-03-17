namespace Freelance_System.Models
{
    public class ContractsPageVM
    {
        public List<ContractVM> AsClient { get; set; } = new();
        public List<ContractVM> AsFreelancer { get; set; } = new();
    }
}