namespace ACESim
{
    public interface IAnyNode
    {
        public int GetInformationSetNodeNumber();
        public Decision Decision { get; set; }
        public bool IsChanceNode { get; }
        public bool IsUtilitiesNode { get; }
        public double[] GetNodeValues();
    }
}