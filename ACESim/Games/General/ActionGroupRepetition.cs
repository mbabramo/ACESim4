using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class ActionGroupRepetition
    {
        /// <summary>
        /// Consecutive items with this tag will be repeated.
        /// </summary>
        public string Tag;
        /// <summary>
        /// The number of repetitions for tagged items.
        /// </summary>
        public int Repetitions;
        /// <summary>
        /// An item that also has this tag will not be included in the first repetition.
        /// </summary>
        public string TagToOmitFirstTime; 
        /// <summary>
        /// An item that also has this tag will not be included in the last repetition.
        /// </summary>
        public string TagToOmitLastTime;
        /// <summary>
        /// If true, then the tagged items will be evolved Repetitions times consecutively, but there will not be extra instances of the items in execution.
        /// If false, then each repetition represents a different point in the execution chain, and each will also be evolved separately.
        /// </summary>
        public bool IsEvolutionRepetitionOnly;
    }
}
