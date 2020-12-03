using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimBase.GameSolvingSupport
{


    public class ModelPredictingUtilitiesDatum
    {
        public List<float>[] PrincipalComponentsWeightForEachPlayer;
        public float[] UtilitiesForEachPlayer;
        int NumPrincipalComponentsPerPlayer;
        int NumPlayers;

        public ModelPredictingUtilitiesDatum(List<double>[] principalComponentsWeightForEachPlayer, double[] utilitiesForEachPlayer)
        {
            PrincipalComponentsWeightForEachPlayer = principalComponentsWeightForEachPlayer.Select(x => x.Select(y => (float)y).ToList()).ToArray();
            UtilitiesForEachPlayer = utilitiesForEachPlayer?.Select(x => (float)x).ToArray();
            NumPrincipalComponentsPerPlayer = principalComponentsWeightForEachPlayer.First().Count();
            NumPlayers = PrincipalComponentsWeightForEachPlayer.Length;
        }


        public (float[] X, float Y, float W) Convert(byte playerIndex = 0)
        {
            float[] X = new float[NumPlayers * NumPrincipalComponentsPerPlayer];
            int index = 0;
            for (int p = 0; p < NumPlayers; p++)
                for (int pc = 0; pc < NumPrincipalComponentsPerPlayer; pc++)
                {
                    X[index++] = (float)PrincipalComponentsWeightForEachPlayer[p][pc];
                }
            float Y = UtilitiesForEachPlayer == null ? 0 : UtilitiesForEachPlayer[playerIndex];
            return (X, Y, 1.0F);
        }
    }
}
