using ACESimBase.GameSolvingSupport.Symmetry;

namespace ACESim
{
    public interface ILitigGameStandardDisputeGenerator : ILitigGameDisputeGenerator
    {
        void GetActionsSetup(
            LitigGameDefinition gameDefinition,
            out byte prePrimaryChanceActions,
            out byte primaryActions,
            out byte postPrimaryChanceActions,
            out byte[] prePrimaryPlayersToInform,
            out byte[] primaryPlayersToInform,
            out byte[] postPrimaryPlayersToInform,
            out bool prePrimaryUnevenChance,
            out bool postPrimaryUnevenChance,
            out bool litigationQualityUnevenChance,
            out bool primaryActionCanTerminate,
            out bool postPrimaryChanceCanTerminate);

        (string name, string abbreviation) PrePrimaryNameAndAbbreviation { get; }
        (string name, string abbreviation) PrimaryNameAndAbbreviation { get; }
        (string name, string abbreviation) PostPrimaryNameAndAbbreviation { get; }

        bool MarkComplete(
            LitigGameDefinition gameDefinition,
            byte prePrimaryAction,
            byte primaryAction);

        bool MarkComplete(
            LitigGameDefinition gameDefinition,
            byte prePrimaryAction,
            byte primaryAction,
            byte postPrimaryAction);

        double[] GetPrePrimaryChanceProbabilities(LitigGameDefinition gameDefinition);

        double[] GetPostPrimaryChanceProbabilities(
            LitigGameDefinition gameDefinition,
            LitigGameStandardDisputeGeneratorActions disputeGeneratorActions);

        bool PostPrimaryDoesNotAffectStrategy();

        (bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetPrePrimaryUnrollSettings();

        (bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetPrimaryUnrollSettings();

        (bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetPostPrimaryUnrollSettings();

        (bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetLiabilityStrengthUnrollSettings();

        (bool unrollIdentical, SymmetryMapInput symmetryMapInput)
            GetDamagesStrengthUnrollSettings();
    }
}




