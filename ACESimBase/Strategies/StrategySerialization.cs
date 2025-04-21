using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Collections;
using ACESimBase.Util.Serialization;
using ACESimBase.Util.Debugging;

namespace ACESim
{
    [Serializable]
    public class StrategySerializationInfo
    {
        public int NumStrategies;
        public int PlayerNumber;
        public List<string> HashCodes;
    }

    /// <summary>
    /// This class can be used to serialize the state of everything based on a strategy at a particular step in the simulation. We can then later DevelopStrategy again, without going through loading of all the evolution steps.
    /// </summary>
    public static class StrategyStateSerialization
    {
        private static string ComputeHash(params byte[] data)
        {
            //return MD5HashGenerator.GenerateKey(data);
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261;

                for (int i = 0; i < data.Length; i++)
                    hash = (hash ^ data[i]) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;
                return hash.ToString();
            }
        }

        public static string ComputeHashOfSpecificStrategy(Strategy theStrategy)
        {
            string theHash = ComputeHash(BinarySerialization.GetByteArray(theStrategy, true));
            return theHash;
        }

        static IEnumerable<Tuple<string, Strategy>> Flatten(this IDictionary dict)
        {
            foreach (DictionaryEntry kvp in dict)
            {
                var childDictionary = kvp.Value as IDictionary;
                if (childDictionary != null)
                {
                    foreach (var tuple in childDictionary.Flatten())
                        yield return tuple;
                }
                else
                    yield return Tuple.Create(kvp.Key.ToString(), kvp.Value as Strategy);
            }
        }

        public static void SerializeStrategyStateToFiles(IGameFactory gameFactory, GameDefinition gameDefinition, Strategy st, string path, string filenameBase)
        {
            StrategyState ss = st.RememberStrategyState(gameFactory, gameDefinition);
            int serializedStrategiesCount = ss.SerializedStrategies.Count();
            List<string> hashCodes = new List<string>();
            foreach (var stst in ss.SerializedStrategies)
                hashCodes.Add(ComputeHash(stst)); // improvement: use more sophisticated hashing scheme
            hashCodes.Add(ComputeHash(ss.SerializedGameFactory));
            hashCodes.Add(ComputeHash(ss.SerializedGameDefinition));
            BinarySerialization.SerializeObject(Path.Combine(path, filenameBase) + ".sti2", 
                new StrategySerializationInfo { 
                    NumStrategies = serializedStrategiesCount, 
                    PlayerNumber = st.PlayerInfo.PlayerIndex, 
                    HashCodes = hashCodes 
                });
            const int additionalThingsToSerialize = 4;
            for (int s = 0; s < serializedStrategiesCount + additionalThingsToSerialize; s++)
            {
                string filenameWithoutPath = "s" + s.ToString() + "-Hash" + hashCodes[s].ToString() + ".stg2";
                string filename = Path.Combine(path, filenameWithoutPath);
                try
                {
                    if (s < serializedStrategiesCount)
                        System.IO.File.WriteAllBytes(filename, ss.SerializedStrategies[s]);
                    else if (s == serializedStrategiesCount)
                        System.IO.File.WriteAllBytes(filename, ss.SerializedGameFactory);
                    else if (s == serializedStrategiesCount + 1)
                        System.IO.File.WriteAllBytes(filename, ss.SerializedGameDefinition);
                }
                catch
                {
                    TabbedText.WriteLine("WARNING: Could not serialize strategy " + s);
                }
            }
        }

        public static Strategy DeserializeStrategyStateFromFiles(string path, string filenameBase, List<Tuple<int,string>> replacementStrategies = null)
        {
            StrategySerializationInfo theInfo = BinarySerialization.GetSerializedObject(Path.Combine(path, filenameBase) + ".sti2") as StrategySerializationInfo;
            List<Byte[]> theStrategies = new List<Byte[]>();
            if (replacementStrategies == null)
                replacementStrategies = new List<Tuple<int, string>>();
            for (int s = 0; s < theInfo.NumStrategies; s++)
            {
                Tuple<int, string> match = replacementStrategies.FirstOrDefault(x => x.Item1 == s);
                if (match == null)
                {
                    string filename = Path.Combine(path, "s" + s.ToString() + "-Hash" + theInfo.HashCodes[s].ToString() + ".stg2");
                    theStrategies.Add(System.IO.File.ReadAllBytes(filename));
                }
                else
                {
                    StrategySerializationInfo theInfo2 = BinarySerialization.GetSerializedObject(match.Item2 + ".sti2") as StrategySerializationInfo;
                    string filename = Path.Combine(path, "s" + s.ToString() + "-Hash" + theInfo2.HashCodes[s].ToString() + ".stg2");
                    theStrategies.Add(System.IO.File.ReadAllBytes(filename)); // take same strategy from some other time
                }
            }
            string filename1 = Path.Combine(path, "s" + (theInfo.NumStrategies).ToString() + "-Hash" + theInfo.HashCodes[theInfo.NumStrategies].ToString() + ".stg2");
            string filename2 = Path.Combine(path, "s" + (theInfo.NumStrategies + 1).ToString() + "-Hash" + theInfo.HashCodes[theInfo.NumStrategies + 1].ToString() + ".stg2");
            string filename3 = Path.Combine(path, "s" + (theInfo.NumStrategies + 2).ToString() + "-Hash" + theInfo.HashCodes[theInfo.NumStrategies + 2].ToString() + ".stg2");
            string filename4 = Path.Combine(path, "s" + (theInfo.NumStrategies + 3).ToString() + "-Hash" + theInfo.HashCodes[theInfo.NumStrategies + 3].ToString() + ".stg2");
            StrategyState ss = new StrategyState
            {
                SerializedStrategies = theStrategies,
                SerializedGameFactory = System.IO.File.ReadAllBytes(filename1),
                SerializedGameDefinition = System.IO.File.ReadAllBytes(filename2),
            };
            Strategy mainStrategy = (Strategy) BinarySerialization.GetObjectFromByteArray(ss.SerializedStrategies[(int)theInfo.PlayerNumber]);
            mainStrategy.RecallStrategyState(ss);
            return mainStrategy;
        }
        
        
    }

    /// <summary>
    /// This class serializes the strategies only. It does not remember the state of anything else (e.g., the game player), so if these strategies are to be used again,
    /// we must rerun the entire ACESim program with the same settings files.
    /// </summary>
    public static class StrategySerialization
    {
        public static void SerializeInformationSets(List<InformationSetNode> informationSets, string path, string filename, bool azureEnabled)
        {
            InformationSetNodesCoreData d = new InformationSetNodesCoreData(informationSets);
            AzureBlob.SerializeToFileOrAzure(d, path, "strategies", filename + ".sis", azureEnabled);
         }

        public static void DeserializeInformationSets(List<InformationSetNode> informationSets, string path, string filename, bool azureEnabled)
        {
            InformationSetNodesCoreData d = AzureBlob.GetSerializedObjectFromFileOrAzure(path, "strategies", filename + ".sis", azureEnabled) as InformationSetNodesCoreData;
            d.CopyToInformationSets(informationSets);
        }

        // Note: Not fully implemented, and this takes a lot of time and huge amount of memory. Part of problem is that everything is serialized (including array command list) and also there are things we will have to mark as nonserialized (such as classes with SPan), which means that they won't serialize anyway.
        public static void SerializeStrategyDeveloper(StrategiesDeveloperBase developer, string filename)
        {
            BinarySerialization.SerializeObject(filename + ".sdb", developer);
        }

        public static StrategiesDeveloperBase DeserializeStrategyDeveloper(string filename)
        {
            return BinarySerialization.GetSerializedObject(filename + ".sdb") as StrategiesDeveloperBase;
        }

        public static void SerializeStrategies(Strategy[] strategies, string path, string filename, bool azureEnabled)
        {
            for (int s = -1; s < strategies.Count(); s++)
            {
                try
                {
                    if (s == -1)
                        AzureBlob.SerializeToFileOrAzure(new StrategySerializationInfo { NumStrategies = strategies.Count() }, path, "strategies", filename + ".sti", azureEnabled);
                    else
                        AzureBlob.SerializeToFileOrAzure(strategies[s], path, "strategies", filename + s.ToString() + ".stg", azureEnabled);
                }
                catch
                {
                    TabbedText.WriteLine("WARNING: Could not serialize strategy " + s);
                }
            }
        }

        public static Strategy[] DeserializeStrategies(string path, string filename, bool azureEnabled)
        {
            StrategySerializationInfo theInfo = AzureBlob.GetSerializedObjectFromFileOrAzure(path, "strategies", filename + ".sti", azureEnabled) as StrategySerializationInfo;
            Strategy[] theStrategies = new Strategy[theInfo.NumStrategies];
            for (int s = 0; s < theInfo.NumStrategies; s++)
                theStrategies[s] = AzureBlob.GetSerializedObjectFromFileOrAzure(path, "strategies", filename + "-" + s.ToString() + ".stg", azureEnabled) as Strategy;
            return theStrategies;
        }

    }
}
