using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ACESim.Util;

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

        public NWayTreeStorageRoot<IGameState> InformationSetTree;
        public string GetInformationSetTreeString() => InformationSetTree.ToTreeString(x => "Branch");
        public HistoryNavigationInfo Navigation;
        public ActionStrategies ActionStrategy;

        public byte ChooseActionBasedOnRandomNumber(GameProgress gameProgress, double randomNumber, double secondRandomNumber, byte numPossibleActions)
        {
            return ActionProbabilityUtilities.ChooseActionBasedOnRandomNumber(gameProgress, randomNumber, secondRandomNumber, ActionStrategy, numPossibleActions, null, Navigation);
        }

        public Strategy()
        {
            // RegretsOrMoveProbabilities = new NWayTreeStorageInternal<double>();
            Crc32.InitializeIfNecessary();
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

        #region Information set tree

        public void CreateInformationSetTree(int numInitialActions, bool useDictionary)
        {
            InformationSetTree = new NWayTreeStorageRoot<IGameState>(null, numInitialActions, useDictionary);
        }

        // NOTE: Sometimes we preface the information set with a decisionIndex, so we have dedicated methods for this.

        public NWayTreeStorage<IGameState> SetInformationSetTreeValueIfNotSet(byte decisionIndex, Span<byte> informationSet, bool historyComplete, Func<IGameState> setter)
        {
            if (decisionIndex == 0)
                decisionIndex = DecisionIndexSubstitute; // a bit hacky -- we can't use a 0 prefix
            var returnVal = InformationSetTree.SetValueIfNotSet(new NWayTreeStorageKeyStackOnly(decisionIndex, informationSet), historyComplete, setter);
            // System.Diagnostics.TabbedText.WriteLine($"{String.Join(",", informationSet)}: {PlayerInfo.PlayerName} {returnVal.StoredValue}");
            return returnVal;
        }

        public NWayTreeStorage<IGameState> SetInformationSetTreeValueIfNotSet(Span<byte> informationSet, bool historyComplete, Func<IGameState> setter)
        {
            var returnVal = InformationSetTree.SetValueIfNotSet(new NWayTreeStorageKeyStackOnly(DecisionIndexSubstitute, informationSet), historyComplete, setter);
            // System.Diagnostics.TabbedText.WriteLine($"{String.Join(",", informationSet)}: {PlayerInfo.PlayerName} {returnVal.StoredValue}");
            return returnVal;
        }

        public byte DecisionIndexSubstitute = 235;

        public IGameState GetInformationSetTreeValue(byte decisionIndex, Span<byte> informationSet)
        {
            if (decisionIndex == 0)
                decisionIndex = DecisionIndexSubstitute; // a bit hacky -- we can't use a 0 prefix
            return InformationSetTree?.GetValue(new NWayTreeStorageKeyStackOnly(decisionIndex, informationSet));
        }

        public IGameState GetInformationSetTreeValue(Span<byte> informationSet)
        {
            return InformationSetTree?.GetValue(new NWayTreeStorageKeyStackOnly(DecisionIndexSubstitute, informationSet));
        }

        #endregion

        #region Serialization

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

        #endregion

        #region Strategy info

        public List<(InformationSetNode, int)> GetTallyNodes(GameDefinition gameDefinition)
        {
            List<(InformationSetNode, int)> informationSets = new List<(InformationSetNode, int)>();
            InformationSetTree.WalkTree((NWayTreeStorage<IGameState> tree) =>
             {
                 IGameState gameState = tree.StoredValue;
                 if (gameState != null && gameState is InformationSetNode tally)
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

        public NWayTreeStorage<List<double>> GetRegretMatchingTree()
        {
            var regretMatchingTree = new NWayTreeStorageRoot<List<double>>(null, InformationSetTree.Branches.Length, false);
            List<(IGameState storedValue, List<byte> sequenceToHere)> nodes = InformationSetTree.GetAllTreeNodes();
            foreach (var node in nodes)
            {
                if (node.storedValue is InformationSetNode tallyNode)
                {
                    Span<byte> sequencePointer = stackalloc byte[node.sequenceToHere.Count() + 1];
                    for (int i = 0; i < node.sequenceToHere.Count(); i++)
                        sequencePointer[i] = node.sequenceToHere[i];
                    sequencePointer[node.sequenceToHere.Count()] = 255;
                    regretMatchingTree.SetValueIfNotSet(new NWayTreeStorageKeyStackOnly(DecisionIndexSubstitute, sequencePointer), false, () => tallyNode.GetEqualProbabilitiesList());
                }
            }
            return regretMatchingTree;
        }

        #endregion
    }
}
