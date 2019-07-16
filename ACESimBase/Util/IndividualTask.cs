using System;

namespace ACESim.Util
{
    [Serializable]
    public class IndividualTask
    {
        public string Name;
        public int ID;
        public int Repetition; 
        public DateTime? Started;
        public bool Complete;

        public IndividualTask(string name, int id, int repetition)
        {
            Name = name;
            ID = id;
            Repetition = repetition;
        }

        public override string ToString()
        {
            return $"{Name} {ID} {Repetition} Started:{Started} Complete:{Complete}";
        }
    }
}
