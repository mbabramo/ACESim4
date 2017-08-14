using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{


    [Serializable]
    public class StrategyState
    {
        public List<Byte[]> SerializedStrategies;
        public List<Strategy> UnserializedStrategies; // this is a short cut to use when we don't need to deserialize
        public Byte[] SerializedGameFactory;
        public Byte[] SerializedGameDefinition;

        public Dictionary<string, Strategy> GetAlreadyDeserializedStrategies(StrategySerializationInfo ssi)
        {
            Dictionary<string, Strategy> d = new Dictionary<string, Strategy>();
            for (int i = 0; i < UnserializedStrategies.Count; i++)
                if (ssi.HashCodes[i] != null)
                    d.Add(ssi.HashCodes[i], UnserializedStrategies[i]);
            return d;
        }
    }
}
