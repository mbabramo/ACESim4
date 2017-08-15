using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    // Explanation: When doing average strategy sampling, we may use subdivision regret matching for decisions along a continuum, where we can rule out discontinuous mixed strategies. That is, we break the decision down into a series of binary decisions. But we want to be sensible about which branches we explore. As a result, we regret match to get a particular result and also a result that is one off. We then 

    public struct CRMSubdivisionRegretMatchPath
    {
        public byte Level;
        byte RegretMatchResult;
        byte OneOff;

        public CRMSubdivisionRegretMatchPath(byte numLevels)
        {
            Level = numLevels;
            RegretMatchResult = 0;
            OneOff = 0;
        }

        public CRMSubdivisionRegretMatchPath(byte numLevels, bool setToComparisonAtLevel)
        {
            Level = numLevels;
            RegretMatchResult = 0;
            OneOff = 0;
            if (setToComparisonAtLevel)
                RecordHigherChoiceSelected(Level);
        }


        public void RecordHigherChoiceSelected(byte level)
        {
            if (level > 7)
                throw new NotImplementedException();
            byte bitAsSet = (byte)((byte)1 << level);
            RegretMatchResult = (byte)(RegretMatchResult | bitAsSet);
        }

        public (CRMSubdivisionRegretMatchPath mainPathToExplore, CRMSubdivisionRegretMatchPath secondPathToExplore, byte mainActionToExplore, bool alsoExploreActionTwo) GetPathsToExplore()
        {
            if (Level == 0)
                throw new NotImplementedException();
            (byte mainActionToExplore, bool alsoExploreActionTwo) = GetActionToExplore();
            CRMSubdivisionRegretMatchPath mainPathToExplore = this; // copies struct (i.e., by value)
            mainPathToExplore.Level--;
            CRMSubdivisionRegretMatchPath secondPathToExplore = this; // we will change this if we are going to explore this.
            secondPathToExplore.Level--;
            if (alsoExploreActionTwo)
            {
                // for both the main path and the second path, we now want to ensure RegretMatchResult == OneOff. That way, we'll see that there is no further exploration needed later.
                mainPathToExplore.OneOff = mainPathToExplore.RegretMatchResult; // we won't later find anything else to explore on main path
                secondPathToExplore.RegretMatchResult = secondPathToExplore.OneOff; // the second path will be different from the main path, but again there won't be anything later to explore
            }
            return (mainPathToExplore, secondPathToExplore, mainActionToExplore, alsoExploreActionTwo);
        }

        public (byte mainActionToExplore, bool alsoExploreActionTwo) GetActionToExplore()
        {
            byte bitAsSet = (byte)((byte)1 << Level);
            bool bitIsSet = (RegretMatchResult & bitAsSet) != 0;
            bool oneOffBitIsSet = (OneOff & bitAsSet) != 0;
            bool alsoMustExploreOpposite = oneOffBitIsSet != bitIsSet; // Note that the oneOffBit will be set in this scenario, because 
            byte actionToExplore = bitIsSet ? (byte)2 : (byte)1;
            return (actionToExplore, alsoMustExploreOpposite);
        }

        private bool IsMaxValue(byte numLevels)
        {
            switch (numLevels)
            {
                case 0:
                    return RegretMatchResult == 1;
                case 1:
                    return RegretMatchResult == 3;
                case 2:
                    return RegretMatchResult == 7;
                case 3:
                    return RegretMatchResult == 15;
                case 4:
                    return RegretMatchResult == 31;
                case 5:
                    return RegretMatchResult == 63;
                case 6:
                    return RegretMatchResult == 127;
                case 7:
                    return RegretMatchResult == 255;
                default:
                    throw new NotImplementedException();
            }
        }

        public void SetOneOff(bool preferablyHigher, byte numLevels)
        {
            if (preferablyHigher)
            {
                if (IsMaxValue(numLevels))
                    preferablyHigher = false;
            }
            else
            {
                if (RegretMatchResult == 0)
                    preferablyHigher = true;
            }
            if (preferablyHigher)
                OneOff = (byte)(RegretMatchResult + 1);
            else
            {
                // we'll make the other one lower. but we'll also reorder so that the first result is always the lower result.
                var temp = (byte)(RegretMatchResult - 1);
                OneOff = RegretMatchResult;
                RegretMatchResult = temp;
            }
        }
    }
}
