using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace ACESim
{
    [Serializable]
    public class Strategy
    {
        [NonSerialized]
        [XmlIgnore]
        internal List<Strategy> _allStrategies;
        public List<Strategy> AllStrategies { get { return _allStrategies; } set { _allStrategies = value; } }

        public PlayerInfo PlayerInfo;

        public NWayTreeStorageInternal<IGameState> InformationSetTree;
        public string GetInformationSetTreeString() => InformationSetTree.ToTreeString("Branch");
        public HistoryNavigationInfo Navigation;
        public ActionStrategies ActionStrategy;

        public byte ChooseActionBasedOnRandomNumber(GameProgress gameProgress, double randomNumber, byte numPossibleActions)
        {
            return ActionProbabilityUtilities.ChooseActionBasedOnRandomNumber(gameProgress, randomNumber, ActionStrategy, numPossibleActions, null, Navigation);
        }

        public Strategy()
        {
            // RegretsOrMoveProbabilities = new NWayTreeStorageInternal<double>();
        }

        public virtual Strategy DeepCopy()
        {
            Strategy theStrategy = new Strategy()
            {
                AllStrategies = AllStrategies.ToList(),
                PlayerInfo = PlayerInfo,
                InformationSetTree = InformationSetTree // not currently a deep copy -- may not be needed

            };
            return theStrategy;
        }


        public void CreateInformationSetTree(int numInitialActions)
        {
            InformationSetTree = new NWayTreeStorageInternal<IGameState>(null, numInitialActions);
        }

        // NOTE: Sometimes we preface the information set with a decisionIndex, so we have dedicated methods for this.

        public unsafe NWayTreeStorage<IGameState> SetInformationSetTreeValueIfNotSet(byte decisionIndex, byte* informationSet, bool historyComplete, Func<IGameState> setter)
        {
            var returnVal = InformationSetTree.SetValueIfNotSet((byte) (decisionIndex + 1), informationSet, historyComplete, setter);
            // System.Diagnostics.Debug.WriteLine($"{String.Join(",", informationSet)}: {PlayerInfo.PlayerName} {returnVal.StoredValue}");
            return returnVal;
        }

        public unsafe NWayTreeStorage<IGameState> SetInformationSetTreeValueIfNotSet(byte* informationSet, bool historyComplete, Func<IGameState> setter)
        {
            var returnVal = InformationSetTree.SetValueIfNotSet(informationSet, historyComplete, setter);
            // System.Diagnostics.Debug.WriteLine($"{String.Join(",", informationSet)}: {PlayerInfo.PlayerName} {returnVal.StoredValue}");
            return returnVal;
        }

        public unsafe IGameState GetInformationSetTreeValue(byte decisionIndex, byte* informationSet)
        {
            return InformationSetTree?.GetValue((byte)(decisionIndex + 1), informationSet);
        }

        public unsafe IGameState GetInformationSetTreeValue(byte* informationSet)
        {
            return InformationSetTree?.GetValue(informationSet);
        }

        public unsafe byte ChooseAction(byte* informationSet, Func<double> randomNumberGenerator)
        {
            throw new NotImplementedException();
        }

        public StrategyState RememberStrategyState(IGameFactory gameFactory, GameDefinition gameDefinition)
        {
            bool serializeStrategyItself = true;
            StrategyState s = new StrategyState
            {
                SerializedStrategies = new List<byte[]>(),
                UnserializedStrategies = new List<Strategy>()
            };
            // we'll just populate this with nulls for now
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
                    catch (OutOfMemoryException)
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
            s.SerializedGameFactory = BinarySerialization.GetByteArray(gameFactory);
            s.SerializedGameDefinition = BinarySerialization.GetByteArray(gameDefinition);
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
                if (PlayerInfo.PlayerIndex == i)
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
            foreach (var s2 in AllStrategies)
            {
                s2.AllStrategies = AllStrategies;
            }
        }


        public void RecallStrategyState(Strategy strategyWithStateAlreadyRecalled)
        {
            AllStrategies = strategyWithStateAlreadyRecalled.AllStrategies.ToList();
        }
        
        public void GetSerializedStrategiesPathAndFilenameBase(string baseOutputDirectory, string storedStrategiesSubdirectory, int numStrategyStatesSerialized, out string path, out string filenameBase)
        {
            path = Path.Combine(baseOutputDirectory, storedStrategiesSubdirectory);
            filenameBase = "strsta" + numStrategyStatesSerialized.ToString();
        }

        public List<(InformationSetNodeTally, int)> GetTallyNodes(GameDefinition gameDefinition)
        {
            List<(InformationSetNodeTally, int)> informationSets = new List<(InformationSetNodeTally, int)>();
            InformationSetTree.WalkTree((NWayTreeStorage<IGameState> tree) =>
             {
                 IGameState gameState = tree.StoredValue;
                 if (gameState != null && gameState is InformationSetNodeTally tally)
                     informationSets.Add((tally, gameDefinition.DecisionsExecutionOrder[tally.DecisionIndex].NumPossibleActions));
             });
             return informationSets;
        }

        public static List<Strategy> GetStarterStrategies(GameDefinition gameDefinition)
        {
            var strategies = new List<Strategy>();
            int numPlayers = gameDefinition.Players.Count();
            for (int i = 0; i < numPlayers; i++)
            {
                var aStrategy = new Strategy
                {
                    PlayerInfo = gameDefinition.Players[i]
                };
                strategies.Add(aStrategy);
            }
            for (int i = 0; i < numPlayers; i++)
            {
                strategies[i].AllStrategies = strategies;
            }
            return strategies;
        }

        public unsafe NWayTreeStorage<List<double>> GetRegretMatchingTree()
        {
            var regretMatchingTree = new NWayTreeStorageInternal<List<double>>(null, InformationSetTree.Branches.Length);
            var nodes = InformationSetTree.GetAllTreeNodes();
            foreach (var node in nodes)
            {
                if (node.storedValue is InformationSetNodeTally tallyNode)
                {
                    byte* sequencePointer = stackalloc byte[node.sequenceToHere.Count() + 1];
                    for (int i = 0; i < node.sequenceToHere.Count(); i++)
                        sequencePointer[i] = node.sequenceToHere[i];
                    sequencePointer[node.sequenceToHere.Count()] = 255;
                    regretMatchingTree.SetValueIfNotSet(sequencePointer, false, () => tallyNode.GetRegretMatchingProbabilities());
                }
            }
            return regretMatchingTree;
        }
    }
}
