using System;
using System.Collections.Generic;
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
        public GamePlayer Player;

        public Decision Decision;

        public ActionGroup ActionGroup;

        public int DecisionNumber;

        [NonSerialized]
        [System.Xml.Serialization.XmlIgnore]
        internal List<Strategy> _allStrategies;
        public List<Strategy> AllStrategies { get { return _allStrategies; } set { _allStrategies = value; } }


        public double? GeneralOverrideValue = null; // if non-null, a constant value will always be returned from calculate. Takes precedence over threadlocal override value.

        internal double Calculate(List<double> inputs)
        {
            throw new NotImplementedException();
        }

        public bool UseBuiltInStrategy = false; // This is set to true for dummy decisions requiring no optimization, and signals the game module to use a built in strategy for the specific decision instead of calling the strategy.

        public EvolutionSettings EvolutionSettings;

        public Strategy()
        {

        }

        public virtual Strategy DeepCopy()
        {
            Strategy theStrategy = new Strategy()
            {
                SimulationInteraction = SimulationInteraction,
                Decision = Decision,
                EvolutionSettings = EvolutionSettings,
                DecisionNumber = DecisionNumber,
                AllStrategies = AllStrategies.ToList(),
                Player = null, /* do not copy -- we want the copy to create its own GamePlayer */
                
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
                if (DecisionNumber == i && !serializeStrategyItself)
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
                if (DecisionNumber == i)
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
            Player = new GamePlayer(
                AllStrategies,
                gameFactory,
                false,
                (GameDefinition)BinarySerialization.GetObjectFromByteArray(s.SerializedGameDefinition));
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
            ;

            Player = new GamePlayer(
                AllStrategies,
                strategyWithStateAlreadyRecalled.Player.gameFactory,
                false,
                strategyWithStateAlreadyRecalled.Player.gameDefinition);
            SimulationInteraction = strategyWithStateAlreadyRecalled.SimulationInteraction;
            foreach (var s2 in AllStrategies)
                s2.SimulationInteraction = SimulationInteraction;
        }


        public IEnumerable<GameProgress> PlayStrategy(
            Strategy strategy,
            List<GameProgress> preplayedGameProgressInfos,
            int numIterations,
            GameInputs[] gameInputsArray,
            IterationID[] iterationIDArray,
            bool returnCompletedGameProgressInfos = false)
        {
            // We are now playing the strategy as a whole. As a result, we do not yet need to do any translating of inputs.
            return Player.PlayStrategy(strategy, DecisionNumber, preplayedGameProgressInfos, numIterations, SimulationInteraction, gameInputsArray, iterationIDArray, returnCompletedGameProgressInfos, DecisionNumber);
        }

    }
}
