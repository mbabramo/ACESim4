using System.Collections.Generic;

namespace ACESimBase.GameSolvingSupport
{
    public class DeepCFRIndependentVariables
    {
        /// <summary>
        /// A code identifying the player. It is possible to include multiple players within the same regression. A binary variable will be used to represent all players but one. 
        /// </summary>
        public byte Player;
        /// <summary>
        /// A code identifying the decision type. A single regression may include observations with different byte codes, for example representing different stages in a game. Binary variables will be used to represent the different possible decision byte codes (except for one).
        /// </summary>
        public byte DecisionByteCode;
        /// <summary>
        /// The information set will consist of actions of players that the player knows about, including but not limited to all of the player's own actions. Each of these will be an independent variable.
        /// </summary>
        public List<byte> InformationSet;
        /// <summary>
        /// If we change game parameters in each observation, then the game parameters for this observation can be specified here. If different game parameters are used for early iterations than for late iterations, then both sets can be included here. Game parameters may also include some indication of the extent to which each player takes other players' utilities into account.
        /// </summary>
        public List<double> GameParameters;
    }
}
