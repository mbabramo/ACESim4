using ACESim;
using ACESimBase.Util.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Games.EFGFileGame
{
    public class EFGFileGameProgress : GameProgress
    {
        public EFGFileGameProgress(bool fullHistoryRequired) : base(fullHistoryRequired)
        {

        }

        public EFGFileGameDefinition EFGFileGameDefinition => (EFGFileGameDefinition)GameDefinition;
        public EFGFileGameOptions EFGFileGameOptions => EFGFileGameDefinition.Options;
        public EFGFileReader EFGFileReader => EFGFileGameDefinition.FileReader;

        public List<EFGFileGameMove> GameMoves = new List<EFGFileGameMove>();
        public List<int> GameActionsOnly = new List<int>();
        public double[] Outcome;

        public override EFGFileGameProgress DeepCopy()
        {
            EFGFileGameProgress copy = new EFGFileGameProgress(FullHistoryRequired);
            copy.GameMoves = GameMoves.ToList();
            return copy;
        }

        public void AddEFGFileGameMove(byte action)
        {
            var informationSet = EFGFileReader.GetEFGFileNode(GameActionsOnly).GetInformationSet();
            EFGFileGameMove move = new EFGFileGameMove(informationSet.InformationSetNumber, informationSet.PlayerNumber, action);
            GameMoves.Add(move);
            GameActionsOnly.Add(action);
        }

        public bool MovesIndicateCompleteGame() => EFGFileReader.IsComplete(GameActionsOnly);

        public override double[] GetNonChancePlayerUtilities()
        {
            if (Outcome != null)
                return Outcome;
            RecalculateGameOutcome();
            return Outcome ?? throw new Exception("Game incomplete");
        }
        public override FloatSet GetCustomResult()
        {
            double[] outcomes = GetNonChancePlayerUtilities();
            return new FloatSet(
                (float)outcomes[0],
                (float)(outcomes.Length > 1 ? (float)outcomes[1] : (float)0),
                (float)(outcomes.Length > 2 ? (float)outcomes[2] : (float)0),
                (float)(outcomes.Length > 3 ? (float)outcomes[3] : (float)0)
                );
        }
        public override void RecalculateGameOutcome()
        {
            Outcome = EFGFileReader.GetOutcomes(GameActionsOnly);
        }
    }
}
