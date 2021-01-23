using ACESim;
using ACESimBase.Games.EFGFileGame;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ACESimTest
{
    [TestClass]
    public class EFGFileTests
    {

        [TestMethod]
        public void EFGProcess_CorrectNumberOfDecisions()
        {
            string bayesEFG = "EFG 2 R \"General Bayes game, one stage\" { \"Player 1\" \"Player 2\" }\r\n\"\"\r\n\r\nc \"\" 1 \"(0,1)\" { \"1G\" 1/2 \"1B\" 1/2 } 0\r\nc \"\" 2 \"(0,2)\" { \"2g\" 1/2 \"2b\" 1/2 } 0\r\np \"\" 1 1 \"(1,1)\" { \"H\" \"L\" } 0\r\np \"\" 2 1 \"(2,1)\" { \"h\" \"l\" } 0\r\nt \"\" 1 \"Outcome 1\" { 10, 2 }\r\nt \"\" 2 \"Outcome 2\" { 0, 10 }\r\np \"\" 2 1 \"(2,1)\" { \"h\" \"l\" } 0\r\nt \"\" 3 \"Outcome 3\" { 2, 4 }\r\nt \"\" 4 \"Outcome 4\" { 4, 0 }\r\np \"\" 1 1 \"(1,1)\" { \"H\" \"L\" } 0\r\np \"\" 2 2 \"(2,2)\" { \"h\" \"l\" } 0\r\nt \"\" 5 \"Outcome 5\" { 10, 2 }\r\nt \"\" 6 \"Outcome 6\" { 0, 10 }\r\np \"\" 2 2 \"(2,2)\" { \"h\" \"l\" } 0\r\nt \"\" 7 \"Outcome 7\" { 2, 4 }\r\nt \"\" 8 \"Outcome 8\" { 4, 0 }\r\nc \"\" 3 \"(0,3)\" { \"2g\" 1/2 \"2b\" 1/2 } 0\r\np \"\" 1 2 \"(1,2)\" { \"H\" \"L\" } 0\r\np \"\" 2 1 \"(2,1)\" { \"h\" \"l\" } 0\r\nt \"\" 9 \"Outcome 9\" { 4, 2 }\r\nt \"\" 10 \"Outcome 10\" { 2, 10 }\r\np \"\" 2 1 \"(2,1)\" { \"h\" \"l\" } 0\r\nt \"\" 11 \"Outcome 11\" { 0, 4 }\r\nt \"ROOT\" 12 \"Outcome 12\" { 10, 2 }\r\np \"\" 1 2 \"(1,2)\" { \"H\" \"L\" } 0\r\np \"\" 2 2 \"(2,2)\" { \"h\" \"l\" } 0\r\nt \"\" 13 \"Outcome 13\" { 4, 2 }\r\nt \"\" 14 \"Outcome 14\" { 2, 10 }\r\np \"\" 2 2 \"(2,2)\" { \"h\" \"l\" } 0\r\nt \"\" 15 \"Outcome 15\" { 0, 4 }\r\nt \"\" 16 \"Outcome 16\" { 10, 0 }";

            EFGFileReader process = new EFGFileReader(bayesEFG);
            process.Decisions.Count().Should().Be(4);
        }

        [TestMethod]
        public void EFGProcess_PlayersReceivingInformation_PlayerDoesntKnowChanceDecision()
        {
            string playerDoesntKnowChanceGame = $@"
EFG 2 R ""Sample"" {{ ""Player 1"" ""Player 2"" }}
c ""ROOT"" 1 ""(0,1)"" {{ ""1G"" 0.500000 ""1B"" 0.500000 }} 0
p """" 1 1 """" {{ ""L"" ""R"" }} 0
t """" 1 ""Outcome 1"" {{ 2.000000 4.000000 }}
t """" 2 ""Outcome 2"" {{ 2.000000 4.000000 }}
p """" 1 1 """" {{ ""R"" ""R"" }} 0
t """" 3 ""Outcome 3"" {{ 4.000000 5.000000 }}
t """" 4 ""Outcome 4"" {{ 4.000000 1.000000 }}
";

            EFGFileReader process = new EFGFileReader();
            var tree1 = process.GetEFGFileNodesTree(playerDoesntKnowChanceGame);
            tree1.GetInformationSet().PlayersToInform.Count().Should().Be(0);
        }

        [TestMethod]
        public void EFGProcess_PlayersReceivingInformation_PlayerKnowsChanceDecision()
        {
            string playerKnowsChanceGame = $@"
EFG 2 R ""Sample"" {{ ""Player 1"" ""Player 2"" }}
c ""ROOT"" 1 ""(0,1)"" {{ ""1G"" 0.500000 ""1B"" 0.500000 }} 0
p """" 1 1 """" {{ ""L"" ""R"" }} 0
t """" 1 ""Outcome 1"" {{ 2.000000 4.000000 }}
t """" 2 ""Outcome 2"" {{ 2.000000 4.000000 }}
p """" 1 2 """" {{ ""R"" ""R"" }} 0
t """" 3 ""Outcome 3"" {{ 4.000000 5.000000 }}
t """" 4 ""Outcome 4"" {{ 4.000000 1.000000 }}
"; // difference is that set player information set has a different number

            var process = new EFGFileReader();
            var tree2 = process.GetEFGFileNodesTree(playerKnowsChanceGame);
            tree2.GetInformationSet().PlayersToInform.Count().Should().Be(1);
        }

        [TestMethod]
        public void EFGProcess_Overall()
        {

            string exampleGame = $@"
EFG 2 R ""General Bayes game, one stage"" {{ ""Player 1"" ""Player 2"" }}
c ""ROOT"" 1 ""(0,1)"" {{ ""1G"" 0.500000 ""1B"" 0.500000 }} 0
c """" 2 ""(0,2)"" {{ ""2g"" 0.500000 ""2b"" 0.500000 }} 0
p """" 1 1 ""(1,1)"" {{ ""H"" ""L"" }} 0
p """" 2 1 ""(2,1)"" {{ ""h"" ""l"" }} 0
t """" 1 ""Outcome 1"" {{ 10.000000 2.000000 }}
t """" 2 ""Outcome 2"" {{ 0.000000 10.000000 }}
p """" 2 1 ""(2,1)"" {{ ""h"" ""l"" }} 0
t """" 3 ""Outcome 3"" {{ 2.000000 4.000000 }}
t """" 4 ""Outcome 4"" {{ 4.000000 0.000000 }}
p """" 1 1 ""(1,1)"" {{ ""H"" ""L"" }} 0
p """" 2 2 ""(2,2)"" {{ ""h"" ""l"" }} 0
t """" 5 ""Outcome 5"" {{ 10.000000 2.000000 }}
t """" 6 ""Outcome 6"" {{ 0.000000 10.000000 }}
p """" 2 2 ""(2,2)"" {{ ""h"" ""l"" }} 0
t """" 7 ""Outcome 7"" {{ 2.000000 4.000000 }}
t """" 8 ""Outcome 8"" {{ 4.000000 0.000000 }}
c """" 3 ""(0,3)"" {{ ""2g"" 0.500000 ""2b"" 0.500000 }} 0
p """" 1 2 ""(1,2)"" {{ ""H"" ""L"" }} 0
p """" 2 1 ""(2,1)"" {{ ""h"" ""l"" }} 0
t """" 9 ""Outcome 9"" {{ 4.000000 2.000000 }}
t """" 10 ""Outcome 10"" {{ 2.000000 10.000000 }}
p """" 2 1 ""(2,1)"" {{ ""h"" ""l"" }} 0
t """" 11 ""Outcome 11"" {{ 0.000000 4.000000 }}
t """" 12 ""Outcome 12"" {{ 10.000000 2.000000 }}
p """" 1 2 ""(1,2)"" {{ ""H"" ""L"" }} 0
p """" 2 2 ""(2,2)"" {{ ""h"" ""l"" }} 0
t """" 13 ""Outcome 13"" {{ 4.000000 2.000000 }}
t """" 14 ""Outcome 14"" {{ 2.000000 10.000000 }}
p """" 2 2 ""(2,2)"" {{ ""h"" ""l"" }} 0
t """" 15 ""Outcome 15"" {{ 0.000000 4.000000 }}
t """" 16 ""Outcome 16"" {{ 10.000000 0.000000 }}
";
            EFGFileReader process = new EFGFileReader();
            var result = process.GetEFGFileNodesTree(exampleGame);
        }
    }
}
