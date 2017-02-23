using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace ACESim
{
    [Export(typeof(ICodeBasedSettingGenerator))] // include the export and exportmetadata attributes if we implement GenerateSetting, setting up the GameModule by code instead of in the settings file.
    [ExportMetadata("CodeGeneratorName", "MyGameDefinition")]
    public class MyGameDefinition : GameDefinition, ICodeBasedSettingGenerator, ICodeBasedSettingGeneratorName
    {
        public string CodeGeneratorName => "MyGameDefinition";

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
