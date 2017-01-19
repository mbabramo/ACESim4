using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESim
{
    public class BargainingGame1Inputs : GameInputs
    {
        // From the two below, we can calculate the actual result, i.e., player 1 and player 2's percentages of the pie.
        public double wastePct; // The proportion that will be wasted if there is no agreement
        public double player1PctOfNotWasted; // The proportion of what's left that player 1 will get
        // Each player will be able to estimate its own percentage of the pie and its opponent's in the event bargaining fails.
        // The accuracy of these estimates will depend on the amount of obfuscation. (The game will translate the obfuscated estimate
        // into an unbiased one by using the obfuscation game.)
        // But the degree to which we obfuscate may change. We define an obfuscation level
        // that determines the potential amount of obfuscation.
        public double obfuscationLevelForSelf;
        public double obfuscationLevelForOpponent;
        // The obfuscation level determines a realized obfuscation, a quantity that will be added to the
        // actual result to give a player a proxy for that result. High obfuscation levels will tend to
        // lead to high obfuscation realizeds. The players do not know these realized values, but may know
        // levels.
        public double obfuscationRealizedOfPlayer1ResultForPlayer1;
        public double obfuscationRealizedOfPlayer1ResultForPlayer2;
        public double obfuscationRealizedOfPlayer2ResultForPlayer1;
        public double obfuscationRealizedOfPlayer2ResultForPlayer2;
    }
}
