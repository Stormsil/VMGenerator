namespace VMGenerator.Models
{
    public class CloneItem
    {
        public string Name { get; set; } = "";
        public string Storage { get; set; } = "data";
        public string Format { get; set; } = "Raw disk image (raw)";
        public bool IsCompleted { get; set; } = false;
        public bool IsConfigured { get; set; } = false;
        public int? VmId { get; set; }
    }
}