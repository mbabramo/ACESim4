using System;
using System.Collections.Generic;
using System.Text;

namespace ACESim
{
    [Serializable]
    public class InformationSetNodeCoreData
    {
        public int InformationSetNodeNumber;
        public double[,] NodeInformation;
        public int LastPastValueIndexRecorded = -1;
        public double[,] PastValues;
        public double[] PastValuesCumulativeStrategyDiscounts;
        public double[] MaxPossible, MinPossible;
        public double MaxPossibleThisPlayer, MinPossibleThisPlayer;

        public InformationSetNodeCoreData(InformationSetNode source)
        {
            CopyFromInformationSet(source);
        }

        public void CopyFromInformationSet(InformationSetNode informationSet)
        {
            InformationSetNodeNumber = informationSet.InformationSetNodeNumber;
            NodeInformation = informationSet.NodeInformation;
            PastValues = informationSet.PastValues;
            PastValuesCumulativeStrategyDiscounts = informationSet.PastValuesCumulativeStrategyDiscounts;
            LastPastValueIndexRecorded = informationSet.LastPastValueIndexRecorded;
            MaxPossible = informationSet.MaxPossible;
            MinPossible = informationSet.MinPossible;
            MaxPossibleThisPlayer = informationSet.MaxPossibleThisPlayer;
            MinPossibleThisPlayer = informationSet.MinPossibleThisPlayer;
        }
        public void CopyToInformationSet(InformationSetNode informationSet)
        {
            informationSet.InformationSetNodeNumber = InformationSetNodeNumber;
            informationSet.NodeInformation = NodeInformation;
            informationSet.PastValues = PastValues;
            informationSet.PastValuesCumulativeStrategyDiscounts = PastValuesCumulativeStrategyDiscounts;
            informationSet.LastPastValueIndexRecorded = LastPastValueIndexRecorded;
            informationSet.MaxPossible = MaxPossible;
            informationSet.MinPossible = MinPossible;
            informationSet.MaxPossibleThisPlayer = MaxPossibleThisPlayer;
            informationSet.MinPossibleThisPlayer = MinPossibleThisPlayer;
            //informationSet.SetAverageStrategyFromPastValues(); // uncomment this to see if using average strategy only from correlated observations makes a difference
        }
    }
}
