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
    }
}
