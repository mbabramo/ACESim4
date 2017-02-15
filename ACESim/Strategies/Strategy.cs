using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class Strategy
    {
        [NonSerialized]
        [System.Xml.Serialization.XmlIgnore]
        internal SimulationInteraction _simulationInteraction;
        public SimulationInteraction SimulationInteraction { get { return _simulationInteraction; } set { _simulationInteraction = value; } }

        [NonSerialized]
        [System.Xml.Serialization.XmlIgnore]
        internal List<Strategy> _allStrategies;
        public List<Strategy> AllStrategies { get { return _allStrategies; } set { _allStrategies = value; } }

        public EvolutionSettings EvolutionSettings;

        public int PlayerNumber;

        public Strategy()
        {

        }

        public virtual Strategy DeepCopy()
        {
            Strategy theStrategy = new Strategy()
            {
                SimulationInteraction = SimulationInteraction,
                EvolutionSettings = EvolutionSettings,
                AllStrategies = AllStrategies.ToList(),
                PlayerNumber = PlayerNumber
                
            };
            return theStrategy;
        }

        
        [Serializable]
        public class StrategyState
        {
            public List<Byte[]> SerializedStrategies;
            public List<Strategy> UnserializedStrategies; // this is a short cut to use when we don't need to deserialize
            public Byte[] SerializedGameFactory;
            public Byte[] SerializedGameDefinition;
            public Byte[] SerializedSimulationInteraction;
            public Byte[] SerializedFastPseudoRandom;

            public Dictionary<string, Strategy> GetAlreadyDeserializedStrategies(StrategySerializationInfo ssi)
            {
                Dictionary<string, Strategy> d = new Dictionary<string,Strategy>();
                for (int i = 0; i < UnserializedStrategies.Count; i++)
                    if (ssi.HashCodes[i] != null)
                        d.Add(ssi.HashCodes[i], UnserializedStrategies[i]);
                return d;
            }
        }

        public StrategyState RememberStrategyState()
        {
            bool serializeStrategyItself = true;
            StrategyState s = new StrategyState();
            s.SerializedStrategies = new List<byte[]>();
            s.UnserializedStrategies = new List<Strategy>(); // we'll just populate this with nulls for now
            for (int i = 0; i < AllStrategies.Count; i++)
            {
                if (!serializeStrategyItself)
                    s.SerializedStrategies.Add(null);
                else
                {
                    Strategy duplicatedStrategy = null;
                    try
                    {
                        duplicatedStrategy = AllStrategies[i].DeepCopy();
                    }
                    catch (OutOfMemoryException ome)
                    {
                        // ignore it -- we'll use the original strategy (will take longer because of undo preserialize)
                        duplicatedStrategy = null;
                    }
                    byte[] bytes = BinarySerialization.GetByteArray(duplicatedStrategy ?? AllStrategies[i]); // will preserialize
                    s.SerializedStrategies.Add(bytes);
                    // doesn't work TextFileCreate.CreateTextFile(@"C:\Temp\" + BinarySerialization.GetHashCodeFromByteArray(bytes), XMLSerialization.SerializeToString<Strategy>(AllStrategies[i])); 
                }
                s.UnserializedStrategies.Add(null);
            }
            s.SerializedGameFactory = BinarySerialization.GetByteArray(SimulationInteraction.CurrentExecutionInformation.GameFactory);
            s.SerializedGameDefinition = BinarySerialization.GetByteArray(SimulationInteraction.PreviouslyLoadedGameDefinition);
            s.SerializedSimulationInteraction = BinarySerialization.GetByteArray(SimulationInteraction);
            s.SerializedFastPseudoRandom = FastPseudoRandom.GetSerializedState();
            return s;
        }

        public void RecallStrategyState(StrategyState s)
        {
            AllStrategies = new List<Strategy>();
            if (s.UnserializedStrategies == null)
                s.UnserializedStrategies = (new Strategy[s.SerializedStrategies.Count()]).ToList();
            for (int i = 0; i < s.SerializedStrategies.Count; i++)
            {
                Strategy toAdd = null;
                if (PlayerNumber == i)
                    toAdd = this;
                else
                {
                    if (s.UnserializedStrategies != null && s.UnserializedStrategies[i] != null)
                        toAdd = null;
                    else if (s.SerializedStrategies[i] == null)
                        toAdd = null;
                    else
                    {
                        toAdd = (Strategy)BinarySerialization.GetObjectFromByteArray(s.SerializedStrategies[i]);
                    }

                }
                if (toAdd == null)
                    AllStrategies.Add(s.UnserializedStrategies[i]);
                else
                {
                    AllStrategies.Add(toAdd);
                    s.UnserializedStrategies[i] = toAdd; // we'll save this for later
                }
            }

            IGameFactory gameFactory = (IGameFactory)BinarySerialization.GetObjectFromByteArray(s.SerializedGameFactory);
            gameFactory.InitializeStrategyDevelopment(this);
            SimulationInteraction = (SimulationInteraction)BinarySerialization.GetObjectFromByteArray(s.SerializedSimulationInteraction);
            foreach (var s2 in AllStrategies)
            {
                s2.SimulationInteraction = SimulationInteraction;
                s2.AllStrategies = AllStrategies;
            }
            FastPseudoRandom.SetState(s.SerializedFastPseudoRandom);
        }
        
        public void RecallStrategyState(Strategy strategyWithStateAlreadyRecalled)
        {
            AllStrategies = strategyWithStateAlreadyRecalled.AllStrategies.ToList();

            SimulationInteraction = strategyWithStateAlreadyRecalled.SimulationInteraction;
            foreach (var s2 in AllStrategies)
                s2.SimulationInteraction = SimulationInteraction;
        }

        private List<double[]> GetSampleDecisionInputs(int minSuccesses)
        {
            throw new NotImplementedException();
        }

        public void GetSerializedStrategiesPathAndFilenameBase(int numStrategyStatesSerialized, out string path, out string filenameBase)
        {
            path = Path.Combine(SimulationInteraction.BaseOutputDirectory, SimulationInteraction.storedStrategiesSubdirectory);
            filenameBase = "strsta" + numStrategyStatesSerialized.ToString();
        }
    }
}
