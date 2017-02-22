﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))]
    [ExportMetadata("GameName", "MyGame")] // put the name of the game class here: ExportMetadata("GameName", "XXX")
    public class MyGameDefinition : GameDefinition, ICodeBasedSettingGenerator, ICodeBasedSettingGeneratorName
    {
        public string CodeGeneratorName => "MyGameDefinition";

        public enum MyGamePlayers
        {
            Chance,
            Plaintiff,
            Defendant
        }

        public enum MyGameDecisions
        {
            POffer,
            DOffer
        }

        public object GenerateSetting(string options)
        {
            return new MyGameDefinition()
            {
                Players = new List<PlayerInfo>
                {
                    new PlayerInfo("C",(int) MyGamePlayers.Chance,true,false),
                    new PlayerInfo("P",(int) MyGamePlayers.Plaintiff,false,true),
                    new PlayerInfo("D",(int) MyGamePlayers.Defendant,false,true),
                },
                DecisionsExecutionOrder = new List<Decision>()
                {
                    new Decision("LitigationQuality", "Qual", (int) MyGamePlayers.Chance, 10),
                    new Decision("PlaintiffSignal", "PSig", (int) MyGamePlayers.Chance, 10),
                    new Decision("DefendantSignal", "DSig", (int) MyGamePlayers.Chance, 10),
                    new Decision("PlaintiffOffer", "PO", (int) MyGamePlayers.Plaintiff, 10),
                    new Decision("DefendantOffer", "DO", (int) MyGamePlayers.Defendant, 10),
                }
            };
        }
    }
}
